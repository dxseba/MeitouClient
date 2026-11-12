using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data;
using Meitou.Game.Host;
using Meitou.Game.Rendering.Frame;
using Meitou.Modding;

namespace Meitou.Brutality;

/// <summary>Finite, display-only blood budgets for freshly detached parts.</summary>
internal sealed class StockDetachedPools(object host, WorldSession shown, int epoch)
{
    const int Capacity = 32, WritesPerFrame = 4, Stages = 4;
    const double BleedSeconds = 12, ContactInterval = .20, FallbackDelay = .65, WaitForLanding = 4;
    const float DriftSquared = 1.25f * 1.25f, SettleDriftSquared = .35f * .35f;
    static readonly bool Trace = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_TRACE_HITS") == "1";
    static readonly ConditionalWeakTable<object, StockDetachedPools> Displays = new();
    static FieldInfo? sessionField;

    enum Part { Limb, LooseLimb, Head }
    readonly record struct SourceId(Part Part, long Key);

    sealed class Source(long decal, float size, uint colour, double now)
    {
        public readonly long Decal = decal;
        public readonly float Size = size;
        public readonly uint Colour = colour;
        public double NextContact = now;
        public double Started = double.NaN;
        public double Waited;
        public int Stage;
        public Vector3 Anchor, DecalPosition;
        public Vector3? Floor, Candidate;
        public Func<Vector3?>? Contact;
        public Func<bool>? Present;
    }

    readonly StockPoolDecals decals = new(host);
    readonly WorldSession session = shown;
    readonly int worldEpoch = epoch;
    readonly Dictionary<SourceId, Source> sources = [];
    readonly HashSet<int> groundItems = [];
    readonly HashSet<int> registeredLimbs = [];
    long groundRevision = -1, serial;
    double previousTime;

    internal static bool Install(ModContext context)
    {
        try
        {
            var host = AccessTools.TypeByName("Meitou.Game.GameHost") ?? throw new TypeLoadException("GameHost");
            sessionField = StockHostParts.Field(host, "Session");
            var fill = AccessTools.Method(host, "FillDrawListCore", [typeof(CharacterDrawList)])
                ?? throw new MissingMethodException(host.Name, "FillDrawListCore");
            context.Harmony.Patch(fill, postfix: new HarmonyMethod(typeof(StockDetachedPools), nameof(AfterDraw)) { priority = Priority.Last });
            return true;
        }
        catch (Exception error)
        {
            context.Log($"Brutality detached pools unavailable: {error.GetBaseException().Message}");
            sessionField = null;
            return false;
        }
    }

    internal static void NativeLimb(object host, WorldSession shown, int item, Vector3 at, GameRecord race, bool leg) =>
        Register(host, shown, new(Part.Limb, item), race, leg, 1, nativeFloor: at);

    internal static void NativeHead(object host, WorldSession shown, long key, Vector3 at, GameRecord race) =>
        Register(host, shown, new(Part.Head, key), race, false, 1, nativeFloor: at);

    internal static void Limb(object host, WorldSession shown, int item, long key, GameRecord race, bool leg,
        Func<Vector3?> contact, Func<bool>? present) =>
        Register(host, shown, item >= 0 ? new(Part.Limb, item) : new(Part.LooseLimb, key), race, leg, 1, contact, present);

    internal static void Head(object host, WorldSession shown, long key, GameRecord race, float share,
        Func<Vector3?> contact, Func<bool> present) =>
        Register(host, shown, new(Part.Head, key), race, false, share, contact, present);

    internal static void RemoveNativeHead(object host, long key)
    {
        var id = new SourceId(Part.Head, key);
        if (Displays.TryGetValue(host, out var display) && display.sources.TryGetValue(id, out var source) && source.Contact is null)
            display.Stop(id, source, "removed");
    }

    internal static void ReplaceHead(object host, long key)
    {
        var id = new SourceId(Part.Head, key);
        if (Displays.TryGetValue(host, out var display) && display.sources.TryGetValue(id, out var source))
            display.Stop(id, source, "split");
    }

    internal static void ReturnLimbToNative(object host, int item, Vector3? contact)
    {
        var id = new SourceId(Part.Limb, item);
        if (!Displays.TryGetValue(host, out var display) || !display.sources.TryGetValue(id, out var source)) return;
        source.Contact = null;
        source.Present = null;
        source.Floor = contact;
    }

    internal static bool CanBleed(GameRecord race) =>
        !race.GetBool("is robot", race.GetInt("is robot", 0) != 0)
        && race.GetFloat("bleed rate", race.GetInt("bleed rate", 1)) > 0;

    static void Register(object host, WorldSession shown, SourceId id, GameRecord race, bool leg, float share,
        Func<Vector3?>? contact = null, Func<bool>? present = null, Vector3? nativeFloor = null)
    {
        if (sessionField is null || !CanBleed(race)) return;
        var display = For(host, shown);
        if (!display.sources.TryGetValue(id, out var source))
        {
            if (display.sources.Count >= Capacity || !display.decals.Available) return;
            if (id.Part == Part.Limb && !display.registeredLimbs.Add((int)id.Key)) return;
            bool head = id.Part == Part.Head;
            bool hiver = race.BaseName.Contains("Hive", StringComparison.OrdinalIgnoreCase);
            // The renderer's size is a diameter multiplier. Scale its square to preserve the requested pool area ratios.
            float area = (leg ? 1.25f : 1) * (hiver ? head ? .80f : .35f : 1) * share;
            float size = (head ? .058f : .064f) * MathF.Sqrt(area);
            uint rgb = unchecked((uint)race.GetInt("blood colour", 0x800000)) & 0xFFFFFF;
            uint colour = 0xFF000000 | (rgb >> 16) | (rgb & 0xFF00) | ((rgb & 0xFF) << 16);
            double now = Clock(shown);
            source = new(long.MinValue + ++display.serial, size, colour, now) { Floor = nativeFloor };
            if (contact is null) source.NextContact += FallbackDelay;
            display.sources.Add(id, source);
        }
        if (contact is null) return;
        source.Contact = contact;
        source.Present = present;
        source.Floor = null;
        source.Candidate = null;
        source.NextContact = Clock(shown);
    }

    static StockDetachedPools For(object host, WorldSession shown)
    {
        int epoch = StockHostParts.WorldEpoch(host);
        if (Displays.TryGetValue(host, out var current))
        {
            if (current.worldEpoch == epoch && ReferenceEquals(current.session.World, shown.World)) return current;
            current.Clear();
            Displays.Remove(host);
        }
        var display = new StockDetachedPools(host, shown, epoch);
        Displays.Add(host, display);
        return display;
    }

    static double Clock(WorldSession shown) =>
        (shown.ShownTick + (shown.TimeScale > 0 ? shown.SimulationAlpha : 0)) * shown.World.TickSeconds;

    static void AfterDraw(object __instance)
    {
        if (!Displays.TryGetValue(__instance, out var display) || (display.sources.Count == 0 && display.registeredLimbs.Count == 0)
            || sessionField?.GetValue(__instance) is not WorldSession shown) return;
        For(__instance, shown).Advance(shown);
    }

    void Advance(WorldSession shown)
    {
        var snapshot = shown.CurrentSnapshot;
        if (snapshot.GroundItemsRevision != groundRevision)
        {
            groundRevision = snapshot.GroundItemsRevision;
            groundItems.Clear();
            foreach (var item in snapshot.GroundItems) groundItems.Add(item.Id);
            registeredLimbs.RemoveWhere(id => !groundItems.Contains(id));
        }
        if (sources.Count == 0) return;
        if (!decals.Available)
        {
            Clear();
            return;
        }
        double now = Clock(shown);
        // Physics advances only a bounded amount per frame. A slow or accelerated render can
        // advance the shown clock by much more, so don't spend the landing wait at that rate.
        double landingStep = Math.Clamp((now - previousTime) / Math.Max(1, shown.TimeScale), 0, .05);
        previousTime = now;
        int writes = WritesPerFrame;
        foreach (var (id, source) in sources)
        {
            if ((id.Part == Part.Limb && !groundItems.Contains((int)id.Key)) || source.Present?.Invoke() == false)
            {
                Stop(id, source, "picked-up-or-removed");
                continue;
            }
            if (shown.TimeScale <= 0) continue;
            if (source.Stage == 0) source.Waited += landingStep;
            if (now < source.NextContact || writes == 0) continue;
            if (Advance(id, source, now)) writes--;
        }
    }

    bool Advance(SourceId id, Source source, double now)
    {
        source.NextContact = now + ContactInterval;
        var floor = source.Contact is { } contact ? contact() : source.Floor;
        if (floor is not { } at || !float.IsFinite(at.LengthSquared()))
        {
            source.Candidate = null;
            if (source.Stage > 0 && now - source.Started >= BleedSeconds) Stop(id, source, "dry off the floor");
            if (source.Stage == 0 && source.Waited >= WaitForLanding) Stop(id, source, "no stable ground contact");
            return false;
        }
        source.Floor = at;
        if (source.Stage == 0)
        {
            // Two nearby contacts separate a stable landing from a brief impact during a bounce.
            if (source.Contact is not null
                && (source.Candidate is not { } candidate || Vector3.DistanceSquared(at, candidate) > SettleDriftSquared))
            {
                source.Candidate = at;
                if (source.Waited >= WaitForLanding) Stop(id, source, "unstable-landing");
                return false;
            }
            source.Started = now;
            source.Anchor = source.DecalPosition = at;
        }
        else if (Vector3.DistanceSquared(at, source.Anchor) > DriftSquared)
        {
            Stop(id, source, "moved");
            return false;
        }
        double elapsed = now - source.Started;
        int stage = Math.Min(Stages, 1 + (int)(elapsed / (BleedSeconds / (Stages - 1))));
        if (stage <= source.Stage) return false;
        float growth = source.Size * (MathF.Sqrt(stage / (float)Stages) - MathF.Sqrt(source.Stage / (float)Stages));
        if (!decals.Grow(source.Decal, ref source.DecalPosition, growth, source.Colour, source.Stage == 0))
        {
            Stop(id, source, "no-decal-surface");
            return false;
        }
        if (Trace && source.Stage == 0)
            Console.WriteLine(FormattableString.Invariant(
                $"Brutality detached pool start {id.Part} id={id.Key} at={source.Anchor} size={source.Size:F3} time={now:F2}"));
        source.Stage = stage;
        if (stage == Stages) Stop(id, source, FormattableString.Invariant($"dry after {elapsed:F2}s"));
        return true;
    }

    void Stop(SourceId id, Source source, string reason)
    {
        if (source.Stage > 0) decals.Stop(source.Decal);
        if (Trace) Console.WriteLine($"Brutality detached pool stop {id.Part} id={id.Key} stage={source.Stage} {reason}");
        sources.Remove(id);
    }

    void Clear()
    {
        foreach (var source in sources.Values)
            if (source.Stage > 0) decals.Stop(source.Decal);
        sources.Clear();
    }
}
