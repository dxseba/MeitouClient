using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.Game;

/// <summary>One persistent, display-only short circuit. Times are game seconds; the owner may already be medically dead.</summary>
internal readonly record struct RobotDischarge(CharacterId Victim, int Part, bool LowStrike, Vector3 FallbackPosition,
    Vector2 Travel, float Force, double Born, double Expires, ulong Seed, long Serial);

/// <summary>Display-thread lifecycle only: one bounded discharge per owner/part, independent of physics and combat randomness.</summary>
internal sealed class RobotDischargeTracker
{
    public const int Capacity = 64;
    public const double MinimumSeconds = 20, MaximumSeconds = 30;

    readonly Dictionary<(CharacterId Victim, int Part), RobotDischarge> entries = new(Capacity);
    readonly List<(CharacterId Victim, int Part)> stale = new(Capacity);
    long serial;

    public int Count => entries.Count;
    public IReadOnlyCollection<RobotDischarge> Active => entries.Values;

    /// <summary>Called for a new hit, not each displayed frame. A later hit refreshes that part without adding another entry.</summary>
    public bool Observe(in CombatEvent hit, bool robot, double gameSeconds, Vector3 fallbackPosition, ulong worldSeed = 0)
    {
        if (!RobotCutEffects.EmitsSparks(hit, robot) || hit.Victim.IsNone || hit.Part < 0
            || !ValidTime(gameSeconds) || !Finite(fallbackPosition)) return false;

        var key = (hit.Victim, hit.Part);
        ulong seed = VisualSeed(hit, worldSeed);
        if (entries.TryGetValue(key, out var previous) && previous.Seed == seed) return false;
        double lifetime = MinimumSeconds + (MaximumSeconds - MinimumSeconds) * (uint)Mix(seed) / uint.MaxValue;
        double expires = gameSeconds + lifetime;
        if (!double.IsFinite(expires) || expires <= gameSeconds) return false;

        float length = hit.Direction.LengthSquared();
        var travel = float.IsFinite(length) && length > 1e-6f ? hit.Direction / MathF.Sqrt(length) : Vector2.UnitX;
        var discharge = new RobotDischarge(hit.Victim, hit.Part, hit.LowStrike, fallbackPosition, travel,
            Math.Clamp(hit.Damage.Cut / 70, 0, 1), gameSeconds, expires, seed, ++serial);
        if (!entries.ContainsKey(key) && entries.Count >= Capacity)
            entries.Remove(entries.MinBy(entry => entry.Value.Serial).Key);
        entries[key] = discharge;
        return true;
    }

    /// <summary>Owner validity means the same slot/generation still exists; medical death never ends a discharge.</summary>
    public void Prune(double gameSeconds, Func<CharacterId, bool> ownerValid)
    {
        if (!ValidTime(gameSeconds)) return;
        stale.Clear();
        foreach (var (key, discharge) in entries)
            if (gameSeconds >= discharge.Expires || !ownerValid(discharge.Victim)) stale.Add(key);
        foreach (var key in stale) entries.Remove(key);
        stale.Clear();
    }

    /// <summary>Called when the displayed world changes; no entries or timing history belong to the next world.</summary>
    public void Clear()
    {
        entries.Clear();
        stale.Clear();
        serial = 0;
    }

    static ulong VisualSeed(in CombatEvent hit, ulong worldSeed)
    {
        static ulong Identity(CharacterId id) => ((ulong)(uint)id.Slot << 32) | (uint)id.Generation;
        return Mix(worldSeed ^ Mix(Identity(hit.Victim)) ^ Mix(Identity(hit.Attacker))
            ^ Mix((ulong)hit.Tick) ^ Mix((uint)hit.Part + 0x524F424F5453504BUL)
            ^ Mix(BitConverter.SingleToUInt32Bits(hit.Damage.Cut)) ^ Mix((uint)hit.CutDirection));
    }

    static ulong Mix(ulong value)
    {
        unchecked
        {
            value ^= value >> 30;
            value *= 0xBF58476D1CE4E5B9UL;
            value ^= value >> 27;
            value *= 0x94D049BB133111EBUL;
            return value ^ value >> 31;
        }
    }

    static bool ValidTime(double seconds) => double.IsFinite(seconds) && seconds >= 0;
    static bool Finite(Vector3 point) => float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);
}
