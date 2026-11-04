using Meitou.Simulation.Combat;
using Meitou.Simulation;
using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Simulation.Bodies;

namespace Meitou.Brutality;

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

}
