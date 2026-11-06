using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Engine;
using Meitou.Rendering;
using Meitou.Simulation;

namespace Meitou.Brutality.Rendering;

/// <summary>Warms a few likely head-burst targets without publishing geometry or preparing GPU assets.</summary>
internal sealed class HeadBurstPreparations : IDisposable
{
    const int Capacity = 2;
    const long ByteBudget = 48L << 20;
    const float RangeSquared = 80 * 80;
    const int ScanInterval = 8, CooldownFrames = 120, CooldownCapacity = 32;
    const float PreserveSeconds = 4;

    sealed class Entry(CharacterAppearance appearance)
    {
        public CharacterAppearance Appearance { get; } = appearance;
        public Task<PreparedHeadBurst?>? Task;
        public long Bytes;
        public long? PreserveUntil;
    }

    sealed record Running(CharacterId Owner, Entry Entry, Task<PreparedHeadBurst?> Task, CancellationTokenSource Cancellation);
    readonly record struct Candidate(CharacterSnapshot Character, CharacterInstance Instance);
    readonly record struct Cooldown(CharacterAppearance Appearance, long UntilFrame);

    readonly Dictionary<CharacterId, Entry> entries = [];
    readonly Dictionary<CharacterId, Cooldown> cooldowns = [];
    Running? running;
    long nextScan, lastFrame;
    bool disposed;

    internal Task<PreparedHeadBurst?>? For(CharacterId id)
    {
        Complete();
        return entries.TryGetValue(id, out var entry) ? entry.Task : null;
    }

    internal void YieldToCut(CharacterId? preparedTarget)
    {
        Complete();
        if (running is { } job && job.Owner != preparedTarget) Remove(job.Owner, cool: true);
    }

    internal void Capture(object host, CharacterDrawList list, WorldSession session, RenderResources resources)
    {
        Complete();
        if (disposed || list.Context is not { } gpu || gpu.Frame.Number < nextScan) return;
        lastFrame = gpu.Frame.Number;
        nextScan = lastFrame + ScanInterval;

        var views = BrutalityRuntime.For(session.World)?.ActorViews;
        long tick = session.ShownTick;
        var retained = new HashSet<CharacterId>();
        foreach (var (id, entry) in entries)
        {
            if (entry.PreserveUntil is null && views?.Any(view => view.Character == id && view.Head is { Mode: HeadCutMode.Burst }) == true)
                entry.PreserveUntil = tick + (long)MathF.Ceiling(PreserveSeconds / session.World.TickSeconds);
            if (entry.PreserveUntil > tick) retained.Add(id);
        }
        foreach (var (id, cooldown) in cooldowns.ToArray())
            if (cooldown.UntilFrame <= lastFrame) cooldowns.Remove(id);

        if (views is null || !views.Any(view => view.Level >= HeadCutPolicy.BurstLevel))
        {
            Trim(retained);
            return;
        }

        var byId = views.ToDictionary(view => view.Character);
        var visible = new Dictionary<long, CharacterInstance>();
        foreach (var instance in list.Items)
            if (!instance.Hidden && !instance.Appearance.Headless && !instance.Appearance.HeadOnly)
                visible.TryAdd(instance.Key, instance);
        var living = new List<Candidate>();
        foreach (var character in session.CurrentSnapshot.Characters)
            if (character.Body is { Dead: false } && RenderBridge.NativeDrawKey(host, character.Id) is { } key
                && visible.TryGetValue(key, out var instance))
                living.Add(new(character, instance));
        var attackers = living.Where(candidate => byId.TryGetValue(candidate.Character.Id, out var actor)
            && actor.Level >= HeadCutPolicy.BurstLevel && candidate.Character.Body is { Unconscious: false }).ToArray();
        if (attackers.Length == 0)
        {
            Trim(retained);
            return;
        }

        var relations = session.World.Systems.OfType<AggroSystem>().FirstOrDefault()?.Relations;
        var eligible = new List<(Candidate Candidate, int Priority, float Distance)>();
        foreach (var candidate in living)
        {
            var id = candidate.Character.Id;
            var look = candidate.Instance.Appearance;
            if (retained.Contains(id) || look.Animal is not null || look.HiddenLimbs != 0 || look.Race.GetBool("is robot")
                || cooldowns.TryGetValue(id, out var cooldown) && ReferenceEquals(cooldown.Appearance, look)
                || byId.TryGetValue(id, out var actor) && (actor.Head is not null || actor.Torso is not null
                    || actor.Limbs.Count != 0 || actor.ChestHole is not null || actor.SevereHead.Kind != SevereHeadWoundKind.None
                    || actor.Wounds.BellyHits >= 10 || actor.Wounds.Entries.Any(entry => entry.Region != -1 || entry.Mark.Segment != 8)))
                continue;
            int priority = int.MaxValue;
            float distance = RangeSquared;
            foreach (var attacker in attackers)
            {
                if (attacker.Character.Id == id) continue;
                float squared = Vector3.DistanceSquared(attacker.Instance.Position, candidate.Instance.Position);
                if (!(squared <= RangeSquared)) continue;
                var a = attacker.Character;
                var c = candidate.Character;
                bool opposed = a.IsPlayer != c.IsPlayer;
                bool hostile = opposed && (a.HostileToPlayer || c.HostileToPlayer)
                    || relations is not null && a.Faction >= 0 && c.Faction >= 0 && AggroSystem.IsEnemy(relations, a.Faction, c.Faction);
                if (!hostile && !opposed && a.Faction == c.Faction) continue;
                int rank = hostile ? 0 : 1;
                if (rank < priority || rank == priority && squared < distance) (priority, distance) = (rank, squared);
            }
            if (priority != int.MaxValue)
                eligible.Add((candidate, priority, distance * (entries.ContainsKey(id) ? .85f : 1)));
        }
        var selected = eligible.OrderBy(row => row.Priority).ThenBy(row => row.Distance)
            .Take(Capacity - retained.Count).Select(row => row.Candidate).ToArray();
        foreach (var candidate in selected) retained.Add(candidate.Character.Id);
        Trim(retained);

        foreach (var candidate in selected)
        {
            var id = candidate.Character.Id;
            var look = candidate.Instance.Appearance;
            if (entries.TryGetValue(id, out var previous) && !ReferenceEquals(previous.Appearance, look))
                Remove(id, cool: false);
            if (running is not null || entries.ContainsKey(id)) continue;
            long available = ByteBudget - entries.Values.Sum(entry => entry.Bytes);
            if (available <= 0) break;
            if (resources.Rigs.For(look) is not { } setup || resources.Mesh(look.BodyMesh) is not { } body)
            {
                Cool(id, look, failed: true);
                continue;
            }
            var snapshot = look.Snapshot();
            var adapter = resources.Geometry;
            var cancellation = new CancellationTokenSource();
            var token = cancellation.Token;
            var entry = new Entry(look);
            var task = CutGeometryWorker.StartPreparation<PreparedHeadBurst?>(() =>
            {
                token.ThrowIfCancellationRequested();
                var prepared = adapter.PrepareHeadBurst(snapshot, setup.Rig, body, resources.Mesh, token);
                token.ThrowIfCancellationRequested();
                return prepared.Bytes <= available ? prepared : null;
            });
            entry.Task = task;
            entries.Add(id, entry);
            running = new(id, entry, task, cancellation);
        }
    }

    void Complete()
    {
        if (running is not { } job || !job.Task.IsCompleted) return;
        running = null;
        job.Cancellation.Dispose();
        if (job.Task.Exception?.GetBaseException() is { } error && error is not OperationCanceledException)
            Console.Error.WriteLine("Brutality head burst preparation: " + error.Message);
        if (!entries.TryGetValue(job.Owner, out var entry) || !ReferenceEquals(entry, job.Entry)) return;
        if (job.Task.IsCompletedSuccessfully && job.Task.Result is { } ready)
            entry.Bytes = ready.Bytes;
        else { Remove(job.Owner, cool: false); Cool(job.Owner, entry.Appearance, failed: true); }
    }

    void Trim(HashSet<CharacterId> retained)
    {
        foreach (var id in entries.Keys.ToArray())
            if (!retained.Contains(id)) Remove(id, cool: true);
    }

    void Remove(CharacterId id, bool cool)
    {
        if (!entries.Remove(id, out var entry)) return;
        if (ReferenceEquals(running?.Entry, entry)) running!.Cancellation.Cancel();
        if (cool) Cool(id, entry.Appearance);
    }

    void Cool(CharacterId id, CharacterAppearance appearance, bool failed = false)
    {
        if (cooldowns.Count >= CooldownCapacity && !cooldowns.ContainsKey(id))
            cooldowns.Remove(cooldowns.MinBy(pair => pair.Value.UntilFrame).Key);
        cooldowns[id] = new(appearance, failed ? long.MaxValue : lastFrame + CooldownFrames);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        entries.Clear();
        cooldowns.Clear();
        if (running is not { } job) return;
        running = null;
        job.Cancellation.Cancel();
        _ = job.Task.ContinueWith(task =>
        {
            _ = task.Exception;
            job.Cancellation.Dispose();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
