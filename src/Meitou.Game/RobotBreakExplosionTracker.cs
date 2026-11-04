using System.Numerics;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Rendering;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.Game;

internal sealed class RobotBreakBurst(CharacterId victim, int part, int kind, bool lowStrike,
    Vector3 fallback, double born, uint seed, float scale, long serial)
{
    public CharacterId Victim { get; } = victim;
    public int Part { get; } = part;
    public int Kind { get; } = kind;
    public bool LowStrike { get; } = lowStrike;
    public Vector3 Fallback { get; } = fallback;
    public double Born { get; } = born;
    public uint Seed { get; } = seed;
    public float Scale { get; } = scale;
    public long Serial { get; } = serial;
    public Vector3? Position { get; private set; }
    public Vector3? SmokePosition { get; private set; }

    public void FollowSmoke(Vector3 point)
    {
        if (float.IsFinite(point.LengthSquared())) SmokePosition = point;
    }

    // An explosion remains where it happened; only its first origin uses the newly posed wound.
    public void Capture(Vector3 point)
    {
        if (Position is null && float.IsFinite(point.LengthSquared()))
        {
            Position = point;
            SmokePosition ??= point;
        }
    }
}

/// <summary>A cosmetic burst per actual mechanical rupture, with bounded effects and replay history.</summary>
internal sealed class RobotBreakExplosionTracker
{
    public const int Capacity = RobotBreakExplosionGeometry.SourceLimit, HistoryLimit = 256;
    readonly List<RobotBreakBurst> active = new(Capacity);
    readonly HashSet<(CharacterId, int, int)> seen = [];
    readonly Queue<(CharacterId, int, int)> history = new(HistoryLimit);
    long serial;
    public IReadOnlyList<RobotBreakBurst> Active => active;
    public int Count => active.Count;
    public int HistoryCount => history.Count;

    public static int Kind(in CombatEvent hit, CharacterCold? victim)
    {
        if (victim is not { Race.IsRobot: true, Medical: { } medical }
            || (uint)hit.Part >= (uint)medical.Parts.Count) return -1;
        var part = medical.Parts[hit.Part].Template;
        if (hit.Kind == CombatEventKind.BodyBisected && part.Type == BodyPartType.Torso) return 2;
        if (part.Type == BodyPartType.Head)
            return hit.Kind is CombatEventKind.Hit or CombatEventKind.PartSevered
                && (medical.Decapitated || victim.HeadCut is { IsValid: true }) ? 1 : -1;
        return hit.Kind == CombatEventKind.PartSevered && part.LimbSlot >= 0 ? 0 : -1;
    }

    public bool Observe(in CombatEvent hit, CharacterCold? victim, double now, Vector3 fallback, ulong worldSeed)
    {
        int kind = Kind(hit, victim);
        if (kind < 0 || hit.Victim.IsNone || !double.IsFinite(now) || now < 0
            || !float.IsFinite(fallback.LengthSquared())) return false;
        var marker = (hit.Victim, hit.Part, kind);
        if (!seen.Add(marker)) return false;
        if (history.Count >= HistoryLimit) seen.Remove(history.Dequeue());
        history.Enqueue(marker);
        if (active.Count >= Capacity) active.RemoveAt(0);
        ulong seed = Rng.Mix(worldSeed ^ Rng.Key(hit.Victim) ^ (ulong)hit.Tick ^ ((ulong)(uint)hit.Part << 32));
        active.Add(new(hit.Victim, hit.Part, kind, hit.LowStrike, fallback, now,
            (uint)(seed ^ seed >> 32), kind == 2 ? 1.4f : kind == 1 ? 1 : .8f, ++serial));
        return true;
    }

    public void Prune(double now, Func<CharacterId, bool> validOwner)
    {
        if (!double.IsFinite(now) || now < 0) return;
        active.RemoveAll(b => now - b.Born >= RobotBreakExplosionGeometry.Lifetime || !validOwner(b.Victim));
    }

    public void Clear()
    {
        active.Clear(); seen.Clear(); history.Clear(); serial = 0;
    }
}
