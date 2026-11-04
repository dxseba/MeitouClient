using System.Numerics;
using Meitou.Data.Particles;

namespace Meitou.Brutality.Effects;

/// <summary>Shared endpoint tangents and widths keep adjacent liquid quads joined while retaining the particle batch layout.</summary>
internal static class LiquidBloodStripGeometry
{
    public static bool Valid(in LiquidBloodStream stream) => Finite(stream.Head) && Finite(stream.Tail)
        && float.IsFinite(stream.Width) && stream.Width > 0 && float.IsFinite(stream.TailWidth)
        && Finite(stream.Colour) && Vector3.DistanceSquared(stream.Head, stream.Tail) is > 1e-10f and < 100;

    public static ParticleInstance Pack(IReadOnlyList<LiquidBloodStream> streams, int index, Vector3 eye)
    {
        var stream = streams[index];
        var tangent = Vector3.Normalize(stream.Tail - stream.Head);
        bool previous = index > 0 && Joined(streams[index - 1], stream);
        bool next = index + 1 < streams.Count && Joined(stream, streams[index + 1]);
        var head = previous ? Joint(streams[index - 1], stream) : tangent;
        var tail = next ? Joint(stream, streams[index + 1]) : tangent;
        var headOct = Encode(head);
        var tailOct = Encode(tail);
        float tailWidth = stream.TailWidth > 0 ? stream.TailWidth : stream.Width;
        // Mode 6 reuses the four vec4 instance attributes as endpoints/widths, colour, and two octahedral tangents.
        return new ParticleInstance
        {
            Position = stream.Head - eye, Rotation = previous ? stream.Width : -stream.Width,
            Direction = stream.Tail - eye, Width = next ? tailWidth : -tailWidth,
            Colour = stream.Colour, Height = headOct.X, Depth = headOct.Y, Pad0 = tailOct.X, Pad1 = tailOct.Y,
        };
    }

    static bool Joined(in LiquidBloodStream a, in LiquidBloodStream b) => Valid(a) && Valid(b)
        && Vector3.DistanceSquared(a.Tail, b.Head) < 1e-8f && a.Colour == b.Colour;

    static Vector3 Joint(in LiquidBloodStream incoming, in LiquidBloodStream outgoing)
    {
        var a = Vector3.Normalize(incoming.Tail - incoming.Head);
        var b = Vector3.Normalize(outgoing.Tail - outgoing.Head);
        var sum = a + b;
        return sum.LengthSquared() > 1e-8f ? Vector3.Normalize(sum) : a;
    }

    internal static Vector2 Encode(Vector3 normal)
    {
        var oct = new Vector2(normal.X, normal.Y) / (MathF.Abs(normal.X) + MathF.Abs(normal.Y) + MathF.Abs(normal.Z));
        if (normal.Z < 0)
            oct = new Vector2((1 - MathF.Abs(oct.Y)) * Sign(oct.X), (1 - MathF.Abs(oct.X)) * Sign(oct.Y));
        return oct;
    }

    static float Sign(float value) => value >= 0 ? 1 : -1;
    static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
    static bool Finite(Vector4 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z) && float.IsFinite(p.W);
}
