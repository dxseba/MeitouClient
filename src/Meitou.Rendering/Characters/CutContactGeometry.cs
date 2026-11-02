using System.Numerics;

namespace Meitou.Rendering.Characters;

/// <summary>A point and outward direction on the last drawn cut surface.</summary>
public readonly record struct CharacterCutContact(Vector3 Position, Vector3 Normal);

/// <summary>Small immutable rim samples, selected only when normal cap geometry is built.</summary>
internal static class CutContactGeometry
{
    public const int SampleLimit = 48;

    public static Vertex[] Sample(ReadOnlySpan<Vertex> cap)
    {
        var seen = new HashSet<Vector3>();
        var rim = new List<Vertex>();
        foreach (var vertex in cap)
            if (vertex.Tangent.W == TorsoCutSurface.Layer && vertex.Uv.X <= 0.5f && vertex.Uv.Y <= 0.5f
                && Finite(vertex.Position) && seen.Add(vertex.Position)) rim.Add(vertex);
        if (rim.Count <= SampleLimit) return rim.ToArray();
        var samples = new Vertex[SampleLimit];
        for (int i = 0; i < samples.Length; i++) samples[i] = rim[i * rim.Count / samples.Length];
        return samples;
    }

    public static CharacterCutContact? Pose(ReadOnlySpan<Vertex> rim, ReadOnlySpan<Matrix4x4> bones, Matrix4x4 world)
    {
        var centre = Vector3.Zero;
        var normal = Vector3.Zero;
        int count = Math.Min(rim.Length, SampleLimit);
        if (count == 0) return null;
        for (int i = 0; i < count; i++)
        {
            var vertex = rim[i];
            var point = Vector3.Zero;
            var direction = Vector3.Zero;
            float sum = 0;
            for (int k = 0; k < 4; k++)
            {
                int bone = (int)(vertex.Bones >> (k * 8) & 255);
                float weight = vertex.Weights[k];
                if (bone >= bones.Length || !(weight > 0) || !float.IsFinite(weight)) continue;
                point += Vector3.Transform(vertex.Position, bones[bone]) * weight;
                direction += Vector3.TransformNormal(vertex.Normal, bones[bone]) * weight;
                sum += weight;
            }
            centre += Vector3.Transform(sum > 0 ? point / sum : vertex.Position, world) / count;
            normal += Vector3.TransformNormal(sum > 0 ? direction / sum : vertex.Normal, world);
        }
        if (!Finite(centre) || !Finite(normal) || normal.LengthSquared() < 1e-10f) return null;
        return new(centre, Vector3.Normalize(normal));
    }

    static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
}
