using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Simulation.Bodies;

namespace Meitou.Simulation.Wounds;

/// <summary>Deterministic visual variation; no combat random stream or medical outcome changes.</summary>
public static class LimbCutPolicy
{
    public static LimbCutState FromHit(int limb, int direction, bool lowStrike, Vector2 travel, float yaw, ulong seed)
    {
        ulong roll = Rng.Mix(seed ^ (ulong)(limb / 2) * 0x9E3779B97F4A7C15UL);
        float Unit(int shift) => ((roll >> shift) & 0xffff) / 65535f;
        float centre = lowStrike || direction == 4 ? .76f : direction is 2 or 3 or 9 ? .49f : .23f;
        float fraction = Math.Clamp(centre + (Unit(0) - .5f) * .25f, .08f, .92f);
        if (!float.IsFinite(yaw)) yaw = 0;
        float length = travel.LengthSquared();
        var along = float.IsFinite(length) && length > 1e-6f ? travel / MathF.Sqrt(length) : Vector2.UnitX;
        float localX = along.X * MathF.Cos(yaw) - along.Y * MathF.Sin(yaw);
        float sweep = direction is 2 or 9 ? 1 : direction == 3 ? -1 : localX;
        float tilt = sweep * (.2f + Unit(16) * .5f);
        var slant = new Vector2(tilt * (limb % 2 == 0 ? 1 : -1), (Unit(32) - .5f) * .7f);
        return new(limb, fraction, slant);
    }

    internal static void Note(CharacterCold owner, int limb, int direction, bool lowStrike, Vector2 travel, float yaw, ulong seed)
    {
        var cut = FromHit(limb, direction, lowStrike, travel, yaw, seed);
        if (!cut.IsValid) return;
        owner.LimbSeverances = [.. owner.LimbSeverances.Where(c => c.LimbSlot != limb).Append(cut).OrderBy(c => c.LimbSlot)];
    }

    internal static void Hash(ref StateHasher hash, IReadOnlyList<LimbCutState> cuts)
    {
        if (cuts.Count == 0) return;
        hash.Add(0x4C494D42); hash.Add(cuts.Count);
        foreach (var cut in cuts)
        { hash.Add(cut.LimbSlot); hash.Add(cut.Fraction); hash.Add(cut.Slant.X); hash.Add(cut.Slant.Y); }
    }

    /// <summary>A fitted replacement no longer clips against its previous organic stump.</summary>
    internal static IReadOnlyList<LimbCutState> Active(CharacterCold? owner)
    {
        if (owner is null || owner.LimbSeverances.Count == 0) return [];
        bool Present(LimbCutState cut) => owner.Medical?.Parts.Any(p => p.Template.LimbSlot == cut.LimbSlot && p.Limb == LimbState.Stump) == true;
        return owner.LimbSeverances.All(Present) ? owner.LimbSeverances : [.. owner.LimbSeverances.Where(Present)];
    }
}
