using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Gameplay;
using Meitou.Data.Save;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Saving;

namespace Meitou.Brutality;

/// <summary>Owns the stock build's XP on the simulation thread and publishes values for the HUD.</summary>
internal sealed class StockProgression(GameConstants constants, BodyOptions options) : ICombatHitObserver, IModSaveData, IStateHashed
{
    const int SaveVersion = 1, MaxSavedActors = 100_000;
    sealed class Actor(BrutalityProgress progress, Hand? key = null)
    {
        public BrutalityProgress Progress = progress;
        public Hand? Key = key;
    }

    static readonly ConditionalWeakTable<World, StockProgression> worlds = new();
    readonly ConditionalWeakTable<CharacterCold, Actor> retained = new();
    readonly Dictionary<CharacterId, Actor> live = [];
    readonly Dictionary<Hand, Actor> durable = [];
    readonly bool trace = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_TRACE_HITS") == "1";
    readonly float xpMultiplier = MathF.Max(0, constants.ExpGainMultiplier);
    volatile IReadOnlyDictionary<CharacterId, float> published = new Dictionary<CharacterId, float>();
    long lastSpawn = -1;
    bool changed;

    internal XpService Xp { get; } = new(constants, options);
    internal static StockProgression? For(World world) => worlds.TryGetValue(world, out var progression)
        ? progression : world.Systems.OfType<StockProgression>().FirstOrDefault();
    internal float LevelFor(World world, CharacterId id) => Find(world, id)?.Progress.Level ?? 0;
    internal bool TryExperience(CharacterId id, out float experience) => published.TryGetValue(id, out experience);

    internal static void Install(ModContext context)
    {
        StockTrainingHook.Install(context);
        var write = typeof(SaveCapture).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(method => method.Name == "WriteCharacter" && method.ReturnType == typeof(SaveCharacter)
                && method.GetParameters().Any(parameter => parameter.ParameterType == typeof(CharacterCold)));
        context.Harmony.Patch(write, postfix: new HarmonyMethod(typeof(StockProgression), nameof(CharacterWritten)));
        var population = context.Population;
        context.AddSystem("progression", _ => new StockProgression(population.Bodies.Constants, population.BodyOptions),
            SystemAnchor.AfterPart(StandardParts.Combat));
        StockProgressionUi.Install(context);
        context.AddDebugCommand("Brutality level (0-100)", debug =>
        {
            if (debug.Selected.Count == 0) return "Select a character.";
            if (!float.TryParse(debug.Field, NumberStyles.Float, CultureInfo.InvariantCulture, out float level)
                || !float.IsFinite(level) || level is < 0 or > 100) return "Enter a level from 0 to 100.";
            debug.Enqueue(new SetLevelCommand(debug.Selected.ToArray(), level) { Tick = debug.Tick });
            return $"Brutality level {level:0.##} queued for {debug.Selected.Count} character(s).";
        }, hasField: true);
    }

    sealed record SetLevelCommand(CharacterId[] Characters, float Level) : SimCommand;

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands)
            if (command is SetLevelCommand level)
                foreach (var id in level.Characters) SetLevel(world, id, level.Level);
    }

    internal void SetLevel(World world, CharacterId id, float level)
    {
        if (Find(world, id) is not { } actor) return;
        actor.Progress = BrutalityProgress.FromLevel(level);
        changed = true;
    }

    void Bind(World world) => worlds.GetValue(world, _ => this);

    public void Schedule(World world)
    {
        Bind(world);
        if (lastSpawn == world.Characters.Spawns) return;
        lastSpawn = world.Characters.Spawns;
        // Seed NPCs from their starting combat skills, before this tick awards any native combat XP.
        for (int slot = 0; slot < world.Characters.HighWater; slot++)
            if (world.Characters.Next[slot] is { Alive: true } next) Find(world, new CharacterId(slot, next.Generation));
    }

    public void SlowWorld(World world)
    {
        if (StockHeadRules.Enabled) StockHeadRules.Step(world);
        if (StockTorsoRules.Enabled) StockTorsoRules.Step(world);
        if (StockLimbRules.Enabled) StockLimbRules.Step(world);
        foreach (var id in live.Keys.Where(id => !world.Characters.TryResolveNext(id, out _)).ToArray())
            changed |= live.Remove(id);
        if (!changed) return;
        published = live.ToDictionary(pair => pair.Key, pair => pair.Value.Progress.Experience);
        changed = false;
    }

    public void HitResolved(World world, in CombatHit hit)
    {
        if (trace && hit.Source == CombatHitSource.Melee && hit.VictimWasAlive
            && world.Characters.TryResolveNext(hit.Attacker, out int sourceSlot)
            && world.Characters.Cold(sourceSlot)?.Fighter is { } fighter
            && world.Characters.TryResolveNext(hit.Victim, out int targetSlot)
            && world.Characters.Cold(targetSlot)?.Medical is { } target && (uint)hit.Part < (uint)target.Parts.Count)
        {
            var part = target.Parts[hit.Part];
            Console.WriteLine(FormattableString.Invariant($"Brutality stock resolved tick={hit.Tick} actor={hit.Attacker} target={hit.Victim} level={LevelFor(world, hit.Attacker):F1} part={part.Template.Name} direction={hit.CutDirection} unarmed={fighter.Unarmed} cut={hit.Damage.Cut:F1} blunt={hit.Damage.Blunt:F1} pierce={hit.Damage.Pierce:F1} hp={part.Flesh:F1}/{part.MaxHp:F1} dead={target.Dead}"));
        }
        if (StockWeaponStains.Enabled) StockWeaponStains.Observe(world, hit);
        if (StockHeadRules.Enabled) StockHeadRules.Observe(world, hit);
        if ((StockTorsoRules.Enabled || StockWoundRules.Enabled) && hit.Source == CombatHitSource.Melee && hit.VictimWasAlive
            && Find(world, hit.Attacker) is { } source)
        {
            if (StockWoundRules.Enabled) StockWoundRules.Observe(world, hit, source.Progress.Level);
            if (StockTorsoRules.Enabled) StockTorsoRules.Observe(world, hit, source.Progress.Level);
        }
        if (hit.Source != CombatHitSource.Melee || !hit.VictimWasAlive || !StockTrainingHook.TryConsume(world, hit, out var contact)
            || Find(world, hit.Attacker) is not { } attacker) return;
        float gained = attacker.Progress.Award(BrutalityPolicy.UsefulDamage(hit.Damage, contact.Remaining)
            * Math.Clamp(contact.Challenge, 0, 6) * xpMultiplier);
        if (!(gained > 0)) return;
        changed = true;
        if (trace) Console.WriteLine(FormattableString.Invariant($"Brutality stock training tick={hit.Tick} attacker={hit.Attacker} xp={gained:F3} total={attacker.Progress.Experience:F3}"));
    }

    Actor? Find(World world, CharacterId id)
    {
        if (!world.Characters.TryResolveNext(id, out int slot) || world.Characters.Cold(slot) is not { } cold) return null;
        if (live.TryGetValue(id, out var actor)) return actor;
        live.Add(id, actor = ForCold(world, cold));
        changed = true;
        return actor;
    }

    Actor ForCold(World world, CharacterCold cold) => retained.GetValue(cold, owner =>
    {
        Hand? key = owner.Save is { Source.Handle.IsNull: false } saved ? saved.Source.Handle : null;
        if (key is { } known && durable.TryGetValue(known, out var remembered)) return remembered;
        var progress = !owner.IsPlayer && (world.Player.Faction < 0 || owner.Faction != world.Player.Faction)
            && owner.Fighter is not { IsAnimal: true } && owner.Stats is { } stats
            ? BrutalityProgress.FromCombat(stats) : new BrutalityProgress();
        var actor = new Actor(progress, key);
        if (key is { } existing) durable[existing] = actor;
        return actor;
    });

    static void CharacterWritten(World world, CharacterCold cold, SaveCharacter __result)
    {
        if (__result.Handle.IsNull) return;
        var progression = For(world) ?? world.Systems.OfType<StockProgression>().FirstOrDefault();
        if (progression is null) return;
        var actor = progression.ForCold(world, cold);
        if (actor.Key is { } old && old != __result.Handle && progression.durable.TryGetValue(old, out var previous)
            && ReferenceEquals(previous, actor)) progression.durable.Remove(old);
        actor.Key = __result.Handle;
        progression.durable[__result.Handle] = actor;
    }

    public void Write(Stream stream)
    {
        if (durable.Count > MaxSavedActors) throw new InvalidDataException("Too many saved Brutality actors.");
        WriteRows(stream, durable.Count, SavedRows().Select(row => (row.Key, row.Value.Progress.Experience)));
    }

    internal static byte[] MigrationBytes(IEnumerable<(Hand Key, float Experience)> source)
    {
        var rows = source.OrderBy(row => (row.Key.Type, row.Key.C, row.Key.Cs, row.Key.I, row.Key.S)).ToArray();
        using var stream = new MemoryStream();
        WriteRows(stream, rows.Length, rows);
        return stream.ToArray();
    }

    static void WriteRows(Stream stream, int count, IEnumerable<(Hand Key, float Experience)> rows)
    {
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(SaveVersion);
        writer.Write(count);
        foreach (var (key, experience) in rows)
        {
            writer.Write(key.Type); writer.Write(key.C); writer.Write(key.Cs); writer.Write(key.I); writer.Write(key.S);
            writer.Write(experience);
        }
    }

    public void Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        if (reader.ReadInt32() != SaveVersion) throw new InvalidDataException("Unsupported stock Brutality save version.");
        int count = reader.ReadInt32();
        if (count is < 0 or > MaxSavedActors || stream.CanSeek && stream.Length - stream.Position != 24L * count)
            throw new InvalidDataException("Invalid stock Brutality save length.");
        var restored = new Dictionary<Hand, float>();
        for (int i = 0; i < count; i++)
        {
            var key = new Hand(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
            float experience = reader.ReadSingle();
            if (key.IsNull || experience != BrutalityProgress.ValidExperience(experience) || !restored.TryAdd(key, experience))
                throw new InvalidDataException("Invalid stock Brutality save entry.");
        }
        foreach (var (key, experience) in restored)
        {
            if (!durable.TryGetValue(key, out var actor)) durable.Add(key, actor = new(new BrutalityProgress(), key));
            actor.Progress = new(experience);
        }
        changed = true;
    }

    public void Hash(ref StateHasher hash)
    {
        foreach (var (id, actor) in live.OrderBy(pair => Rng.Key(pair.Key)))
        {
            if (!(actor.Progress.Experience > 0)) continue;
            hash.Add(id);
            actor.Progress.Hash(ref hash);
        }
        var active = live.Values.ToHashSet();
        foreach (var (key, actor) in SavedRows())
        {
            if (active.Contains(actor) || !(actor.Progress.Experience > 0)) continue;
            hash.Add(key.Type); hash.Add(key.C); hash.Add(key.Cs); hash.Add(key.I); hash.Add(key.S);
            actor.Progress.Hash(ref hash);
        }
    }

    IEnumerable<KeyValuePair<Hand, Actor>> SavedRows() =>
        durable.OrderBy(pair => (pair.Key.Type, pair.Key.C, pair.Key.Cs, pair.Key.I, pair.Key.S));
}
