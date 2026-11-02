using System.Numerics;

namespace Meitou.Rendering;

internal readonly record struct RobotSmokePuff(Vector3 Position, float Size, float Rotation, float Alpha);

/// <summary>Small deterministic smoke puffs driven entirely by the host's paused discharge age.</summary>
internal static class RobotDischargeSmokeGeometry
{
    public const int AnchorLimit = 64, PuffLimit = 128, PuffsPerAnchor = 3;

    public static int Build(ReadOnlySpan<RobotDischargeAnchor> anchors, Span<RobotSmokePuff> output)
    {
        Span<int> active = stackalloc int[AnchorLimit];
        int count = 0;
        for (int i = 0; i < anchors.Length && count < active.Length; i++)
            if (Valid(anchors[i])) active[count++] = i;
        int capacity = Math.Min(output.Length, PuffLimit);
        if (count == 0 || capacity == 0) return 0;
        int share = Math.Min(PuffsPerAnchor, capacity / count);
        int remainder = capacity - share * count;
        int written = 0;
        for (int i = 0; i < count; i++)
        {
            int quota = Math.Min(PuffsPerAnchor, share + (i < remainder ? 1 : 0));
            var anchor = anchors[active[i]];
            float fade = Math.Clamp((anchor.DurationSeconds - anchor.AgeSeconds)
                / MathF.Min(8, anchor.DurationSeconds * 0.35f), 0, 1);
            float strength = Math.Clamp(anchor.Intensity, 0, 1);
            for (int puff = 0; puff < quota; puff++)
            {
                float age = anchor.AgeSeconds - puff * 0.23f;
                if (age < 0) continue;
                var value = Puff(anchor.Position, age, anchor.Seed, puff, fade * strength);
                if (!Finite(value.Position) || !float.IsFinite(value.Rotation) || !float.IsFinite(value.Alpha)) continue;
                output[written++] = value;
            }
        }
        return written;
    }

    static RobotSmokePuff Puff(Vector3 origin, float age, uint ownerSeed, int index, float opacity)
    {
        uint seed = Mix(ownerSeed ^ ((uint)index + 1) * 0x9E3779B9u);
        float life = 2.4f + Unit(seed) * 0.6f;
        float clock = age / life;
        float whole = MathF.Floor(clock);
        float phase = clock - whole;
        float elapsed = phase * life;
        uint cycle = Mix(seed ^ BitConverter.SingleToUInt32Bits(whole));
        float speed = 1.3f + Unit(cycle + 1) * 0.35f;
        var drift = new Vector3(Unit(cycle + 2) - 0.5f, 0, Unit(cycle + 3) - 0.5f) * (elapsed * 0.28f);
        var sway = new Vector3(MathF.Sin(elapsed * 1.7f), 0, MathF.Sin(elapsed * 1.3f + 1.8f)) * (0.12f * phase);
        var position = origin + drift + sway + Vector3.UnitY * (0.15f + elapsed * speed);
        float size = 1.5f + phase * 1.5f + Unit(cycle + 4) * 0.3f;
        float rotation = Unit(cycle + 5) * MathF.Tau + elapsed * (Unit(cycle + 6) - 0.5f) * 0.32f;
        float envelope = MathF.Sin(phase * MathF.PI);
        float alpha = (0.17f + Unit(cycle + 7) * 0.09f) * envelope * opacity;
        return new(position, size, rotation, alpha);
    }

    static bool Valid(in RobotDischargeAnchor anchor) => Finite(anchor.Position)
        && float.IsFinite(anchor.AgeSeconds) && anchor.AgeSeconds >= 0
        && float.IsFinite(anchor.DurationSeconds) && anchor.DurationSeconds > 0 && anchor.AgeSeconds < anchor.DurationSeconds
        && float.IsFinite(anchor.Intensity) && anchor.Intensity > 0;

    static float Unit(uint seed) => (Mix(seed) >> 8) * (1f / 16777216f);
    static uint Mix(uint value)
    {
        unchecked
        {
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            return value ^ value >> 16;
        }
    }

    static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
}
