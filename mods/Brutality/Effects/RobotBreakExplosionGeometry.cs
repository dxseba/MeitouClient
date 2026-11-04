using System.Numerics;

namespace Meitou.Brutality.Effects;

/// <summary>A one-shot flash at the initial cut, with soot following its fragment, aged by the paused game clock.</summary>
public readonly record struct RobotBreakExplosion(Vector3 Position, float Age, uint Seed, float Scale = 1)
{
    public Vector3? SmokePosition { get; init; }
}

internal enum RobotExplosionQuadKind { Flash, Spark, Smoke }

internal readonly record struct RobotExplosionQuad(Vector3 Position, Vector3 Direction, float Width, float Height,
    float Rotation, Vector4 Colour, RobotExplosionQuadKind Kind);

/// <summary>Local analytic flash, radial sparks and soot, with fixed global geometry and source budgets.</summary>
public static class RobotBreakExplosionGeometry
{
    public const float Lifetime = 2f, FlashLifetime = 0.16f;
    public const int SourceLimit = 16, CoreQuadLimit = 320, SmokeLimit = 64, QuadLimit = CoreQuadLimit + SmokeLimit;
    const int SparksPerSource = 48, SmokePerSource = 4;

    internal static int Build(ReadOnlySpan<RobotBreakExplosion> sources, Span<RobotExplosionQuad> output)
    {
        Span<int> active = stackalloc int[SourceLimit];
        int count = 0;
        for (int i = 0; i < sources.Length && count < SourceLimit; i++)
            if (Valid(sources[i])) active[count++] = i;
        int capacity = Math.Min(output.Length, QuadLimit);
        if (count == 0 || capacity == 0) return 0;
        int coreCapacity = Math.Min(CoreQuadLimit, capacity), written = 0;
        for (int i = 0; i < count; i++)
        {
            int quota = Math.Min(SparksPerSource + 2, Share(coreCapacity, count, i));
            var source = sources[active[i]];
            AddFlash(source, quota, output, ref written);
            AddSparks(source, Math.Max(0, quota - 2), output, ref written);
        }
        int smokeCapacity = Math.Min(SmokeLimit, capacity - coreCapacity);
        for (int i = 0; i < count; i++)
            AddSmoke(sources[active[i]], Math.Min(SmokePerSource, Share(smokeCapacity, count, i)), output, ref written);
        return written;
    }

    static int Share(int capacity, int count, int index) => capacity / count + (index < capacity % count ? 1 : 0);

    static void AddFlash(in RobotBreakExplosion source, int quota, Span<RobotExplosionQuad> output, ref int written)
    {
        if (source.Age >= FlashLifetime || quota == 0) return;
        float scale = Math.Clamp(source.Scale, 0.2f, 2), phase = source.Age / FlashLifetime;
        float size = (1.3f + 0.9f * MathF.Sqrt(phase)) * scale;
        float alpha = (1 - phase) * (1 - phase);
        Add(new(source.Position, Vector3.Zero, size * (0.8f + Unit(source.Seed + 1) * 0.3f), size, Unit(source.Seed) * MathF.Tau,
            new(3.0f, 5.5f, 8, alpha), RobotExplosionQuadKind.Flash), output, ref written);
        if (quota < 2 || source.Age >= 0.11f) return;
        float core = (0.8f + phase * 0.25f) * scale;
        Add(new(source.Position, Vector3.Zero, core, core, 0,
            new(9, 6.5f, 2.6f, 1 - source.Age / 0.11f), RobotExplosionQuadKind.Flash), output, ref written);
    }

    static void AddSparks(in RobotBreakExplosion source, int quota, Span<RobotExplosionQuad> output, ref int written)
    {
        float scale = Math.Clamp(source.Scale, 0.2f, 2);
        for (int i = 0; i < quota; i++)
        {
            uint seed = Mix(source.Seed ^ ((uint)i + 1) * 0x9E3779B9u);
            float life = 0.35f + Unit(seed) * 0.7f;
            if (source.Age >= life) continue;
            var velocity = Direction(seed) * ((6 + Unit(seed + 1) * 13) * scale);
            float age = source.Age;
            var tangent = velocity - Vector3.UnitY * (24 * age);
            // Derive the streak in local space: subtracting translated world endpoints changes its direction and length.
            var along = tangent * (0.022f + Unit(seed + 2) * 0.035f);
            float length = along.Length();
            if (!float.IsFinite(length) || length < 1e-4f) continue;
            float fade = MathF.Sqrt(1 - age / life);
            var colour = i % 3 == 0 ? new Vector4(-3, -5.5f, -9, fade)
                : new Vector4(8, 3.5f + Unit(seed + 3) * 2, 0.6f, fade);
            var offset = velocity * age - Vector3.UnitY * (12 * age * age) - along * 0.5f;
            var position = source.Position + offset;
            Add(new(position, along / length, (0.025f + Unit(seed + 4) * 0.04f) * scale,
                length, 0, colour, RobotExplosionQuadKind.Spark), output, ref written);
        }
    }

    static void AddSmoke(in RobotBreakExplosion source, int quota, Span<RobotExplosionQuad> output, ref int written)
    {
        float scale = Math.Clamp(source.Scale, 0.2f, 2);
        for (int i = 0; i < quota; i++)
        {
            uint seed = Mix(source.Seed ^ ((uint)i + 1) * 0x85EBCA6Bu);
            float age = source.Age, phase = age / Lifetime;
            var radial = new Vector3(Unit(seed + 1) - 0.5f, 0, Unit(seed + 2) - 0.5f);
            var origin = source.SmokePosition is { } smoke && Finite(smoke) ? smoke : source.Position;
            var position = origin + radial * ((0.1f + age * 0.18f) * scale)
                + Vector3.UnitY * ((0.08f + age * 0.4f) * scale);
            float size = (0.6f + MathF.Sqrt(phase) * 1.6f) * scale;
            float alpha = (0.2f + Unit(seed + 3) * 0.08f) * MathF.Pow(1 - phase, 0.8f);
            Add(new(position, Vector3.Zero, size, size, Unit(seed + 4) * MathF.Tau + age * 0.25f,
                new(0.015f, 0.018f, 0.022f, alpha), RobotExplosionQuadKind.Smoke), output, ref written);
        }
    }

    static void Add(in RobotExplosionQuad value, Span<RobotExplosionQuad> output, ref int written)
    {
        if (written >= output.Length || !Finite(value.Position) || !float.IsFinite(value.Position.LengthSquared()) || !Finite(value.Direction)
            || !float.IsFinite(value.Width) || value.Width <= 0 || !float.IsFinite(value.Height) || value.Height <= 0
            || !float.IsFinite(value.Colour.W) || value.Colour.W <= 0) return;
        output[written++] = value;
    }

    static bool Valid(in RobotBreakExplosion source) => Finite(source.Position) && float.IsFinite(source.Age)
        && source.Age >= 0 && source.Age < Lifetime && float.IsFinite(source.Scale) && source.Scale > 0;
    static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    static Vector3 Direction(uint seed)
    {
        float y = Unit(seed + 11) * 2 - 1, angle = Unit(seed + 23) * MathF.Tau;
        float radius = MathF.Sqrt(MathF.Max(0, 1 - y * y));
        return new(MathF.Cos(angle) * radius, y, MathF.Sin(angle) * radius);
    }
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
}
