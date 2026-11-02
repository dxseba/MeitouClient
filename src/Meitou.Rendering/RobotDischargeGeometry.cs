using System.Numerics;

namespace Meitou.Rendering;

/// <summary>A transient electrical source in world space. Age is the host's paused game-time clock.</summary>
public readonly record struct RobotDischargeAnchor(Vector3 Position, float AgeSeconds, float DurationSeconds,
    uint Seed, float Intensity = 1);

internal readonly record struct RobotDischargeStroke(Vector3 Head, Vector3 Tail, float Width, Vector4 Colour, bool Arc);

/// <summary>Bounded analytic arcs and spark streaks; no particle units, persistent simulation or per-anchor buffers.</summary>
internal static class RobotDischargeGeometry
{
    public const int AnchorLimit = 64, QuadLimit = 1024;

    public static int Build(ReadOnlySpan<RobotDischargeAnchor> anchors, Span<RobotDischargeStroke> output)
    {
        Span<int> active = stackalloc int[AnchorLimit];
        int count = 0;
        for (int i = 0; i < anchors.Length && count < active.Length; i++)
            if (Valid(anchors[i])) active[count++] = i;
        int capacity = Math.Min(output.Length, QuadLimit);
        if (count == 0 || capacity == 0) return 0;
        int share = Math.Min(128, capacity / count), written = 0;
        if (share == 0) return 0;
        for (int i = 0; i < count; i++)
        {
            var anchor = anchors[active[i]];
            var writer = new Writer(output.Slice(written, share));
            float fade = Math.Clamp((anchor.DurationSeconds - anchor.AgeSeconds)
                / MathF.Min(8, anchor.DurationSeconds * 0.35f), 0, 1);
            float strength = Math.Clamp(anchor.Intensity, 0, 4);
            int arcBudget = Math.Min(share, Math.Clamp(share / 4, 6, 18));
            Arcs(anchor, fade, strength, arcBudget, ref writer);
            Sparks(anchor, fade, strength, share - writer.Count, ref writer);
            written += writer.Count;
        }
        return written;
    }

    static bool Valid(in RobotDischargeAnchor anchor) => Finite(anchor.Position)
        && float.IsFinite(anchor.AgeSeconds) && anchor.AgeSeconds >= 0
        && float.IsFinite(anchor.DurationSeconds) && anchor.DurationSeconds > 0 && anchor.AgeSeconds < anchor.DurationSeconds
        && float.IsFinite(anchor.Intensity) && anchor.Intensity > 0;

    static void Arcs(in RobotDischargeAnchor anchor, float fade, float strength, int budget, ref Writer writer)
    {
        if (budget == 0) return;
        uint phase = (uint)MathF.Floor(anchor.AgeSeconds * 16);
        uint seed = Mix(anchor.Seed ^ phase * 0x9E3779B9u);
        float flicker = 0.55f + Unit(seed) * 0.45f;
        var colour = new Vector4(-2.7f, -5.0f, -8.0f, fade * flicker * strength);
        var origin = anchor.Position + Vector3.UnitY * 0.06f;
        var axis = Direction(seed);
        var side = Vector3.Normalize(Vector3.Cross(axis, MathF.Abs(axis.Y) < 0.8f ? Vector3.UnitY : Vector3.UnitX));
        var up = Vector3.Cross(axis, side);
        var end = origin + axis * (0.9f + Unit(seed + 1) * 2.0f);
        int segments = Math.Min(budget, Math.Clamp(budget - 4, 3, 10));
        Span<Vector3> points = stackalloc Vector3[11];
        points[0] = origin;
        for (int i = 1; i <= segments; i++)
        {
            float t = (float)i / segments;
            uint at = Mix(seed + (uint)i * 97);
            float bend = MathF.Sin(t * MathF.PI);
            points[i] = Vector3.Lerp(origin, end, t)
                + (side * (Unit(at) - 0.5f) + up * (Unit(at + 1) - 0.5f)) * (1.2f * bend);
            writer.Add(points[i], points[i - 1], 0.32f + Unit(at + 2) * 0.16f, colour, arc: true);
        }
        for (int branch = 0; branch < budget && writer.Count < budget; branch++)
        {
            uint at = Mix(seed + (uint)branch * 137 + 17);
            int start = 1 + branch % Math.Max(1, segments - 1);
            var branchEnd = points[start] + Direction(at) * (0.45f + Unit(at + 2) * 0.9f);
            var middle = Vector3.Lerp(points[start], branchEnd, 0.5f) + side * ((Unit(at + 3) - 0.5f) * 0.5f);
            writer.Add(middle, points[start], 0.22f, colour * 0.8f, arc: true);
            if (writer.Count < budget) writer.Add(branchEnd, middle, 0.18f, colour * 0.65f, arc: true);
        }
    }

    static void Sparks(in RobotDischargeAnchor anchor, float fade, float strength, int count, ref Writer writer)
    {
        for (int i = 0; i < count; i++)
        {
            uint seed = Mix(anchor.Seed + (uint)i * 0x85EBCA6Bu);
            float life = 0.18f + Unit(seed) * 0.47f;
            float clock = anchor.AgeSeconds / life + Unit(seed + 1);
            uint birth = (uint)MathF.Floor(clock);
            float age = (clock - MathF.Floor(clock)) * life;
            uint at = Mix(seed ^ birth * 0xC2B2AE35u);
            var velocity = Direction(at) * (3.0f + Unit(at + 1) * 8.5f);
            var head = anchor.Position + velocity * age - Vector3.UnitY * (12 * age * age);
            var tangent = velocity - Vector3.UnitY * (24 * age);
            var tail = head - tangent * (0.014f + Unit(at + 2) * 0.035f);
            float alpha = fade * strength * MathF.Sqrt(MathF.Max(0, 1 - age / life));
            var colour = new Vector4(7.0f, 2.6f + Unit(at + 3) * 1.5f, 0.35f, alpha);
            writer.Add(head, tail, 0.025f + Unit(at + 4) * 0.035f, colour, arc: false);
        }
    }

    static Vector3 Direction(uint seed)
    {
        var direction = new Vector3(Unit(seed + 11) * 2 - 1, 0.20f + Unit(seed + 23) * 0.8f, Unit(seed + 41) * 2 - 1);
        return Vector3.Normalize(direction);
    }

    static float Unit(uint seed) => (Mix(seed) >> 8) * (1f / 16777216f);
    static uint Mix(uint value)
    {
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        return value ^ value >> 16;
    }

    static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);

    ref struct Writer
    {
        readonly Span<RobotDischargeStroke> output;
        public int Count;

        public Writer(Span<RobotDischargeStroke> target)
        {
            output = target;
            Count = 0;
        }
        public void Add(Vector3 head, Vector3 tail, float width, Vector4 colour, bool arc)
        {
            if (Count >= output.Length || !Finite(head) || !Finite(tail) || Vector3.DistanceSquared(head, tail) < 1e-8f) return;
            output[Count++] = new(head, tail, width, colour, arc);
        }
    }
}
