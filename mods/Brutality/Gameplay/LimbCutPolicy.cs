using System.Numerics;
using Meitou.Simulation;

namespace Meitou.Brutality;

/// <summary>Deterministic visual variation; no combat random stream or medical outcome changes.</summary>
public static class LimbCutPolicy
{
    public static LimbCutState FromHit(int limb, int direction, bool lowStrike, Vector2 travel, float yaw, ulong seed)
    {
        ulong roll = Rng.Mix(seed ^ (ulong)(limb / 2) * 0x9E3779B97F4A7C15UL);
        float Unit(int shift) => ((roll >> shift) & 0xfff) / 4095f;
        float fraction = Station(direction, lowStrike, Unit(0)) + (Unit(12) - .5f) * .10f;

        if (!float.IsFinite(yaw)) yaw = 0;
        float length = travel.LengthSquared();
        var approach = new Vector2(0, -1);
        if (float.IsFinite(length) && length > 1e-6f)
        {
            travel /= MathF.Sqrt(length);
            var (sine, cosine) = MathF.SinCos(yaw);
            approach = new(travel.X * cosine - travel.Y * sine, travel.X * sine + travel.Y * cosine);
        }
        float sweep = direction switch { 2 or 9 => 1, 3 => -1, _ => approach.X };
        float heading = MathF.Atan2(approach.Y * .7f, sweep * .7f + approach.X * .3f);
        heading += (Unit(24) - .5f) * 1.15f;

        // Keep steep cuts closer to the middle of the limb, where the whole opening fits inside the mesh.
        float slope = Slope(Unit(36), Unit(48)) * (1 - .60f * MathF.Abs(fraction - .50f));
        var (depth, lateral) = MathF.SinCos(heading);
        var slant = new Vector2(lateral * (limb % 2 == 0 ? 1 : -1), depth) * slope;
        return new(limb, fraction, slant);
    }

    /// <summary>
    /// Low strikes favour the wrist or ankle, sweeps favour the middle, and other strikes favour the shoulder or hip.
    /// </summary>
    static float Station(int direction, bool lowStrike, float roll)
    {
        if (lowStrike || direction == 4) return roll switch { < .18f => .50f, < .58f => .66f, _ => .81f };
        if (direction is 2 or 3 or 9) return roll switch { < .18f => .34f, < .65f => .50f, _ => .66f };
        return roll switch { < .30f => .18f, < .74f => .34f, _ => .50f };
    }

    /// <summary>
    /// Chooses a near-square, diagonal or steep diagonal section. The plane builder treats these values as slopes.
    /// </summary>
    static float Slope(float profile, float variation) => profile switch
    {
        < .20f => .06f + variation * .15f,
        < .76f => .25f + variation * .43f,
        _ => .73f + variation * .31f,
    };
}
