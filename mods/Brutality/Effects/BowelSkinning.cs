using Meitou.Game.Rendering.Frame;
using System.Numerics;
using Meitou.Brutality.Physics;

namespace Meitou.Brutality.Effects;

/// <summary>Four-node tissue blending of the installed bowel mesh; the initial pose preserves its original shape.</summary>
internal sealed class BowelSkinning
{
    readonly Vertex[] source;
    readonly (int[] Nodes, Vector4 Weights)[] links;
    public BowelRigGeometry Geometry { get; }

    public BowelSkinning(Vertex[] vertices)
    {
        source = vertices;
        var min = vertices.Aggregate(new Vector3(float.MaxValue), (p, v) => Vector3.Min(p, v.Position));
        var max = vertices.Aggregate(new Vector3(float.MinValue), (p, v) => Vector3.Max(p, v.Position));
        var extent = (max - min) * 0.5f;
        var axes = Enumerable.Range(0, 3).OrderByDescending(i => extent[i]).ToArray();
        var nodes = new Vector3[9];
        for (int row = 0; row < 3; row++)
            for (int column = 0; column < 3; column++)
            {
                var point = Vector3.Zero;
                point[axes[0]] = (column - 1) * extent[axes[0]] * 0.72f;
                point[axes[1]] = (row - 1) * extent[axes[1]] * 0.72f;
                nodes[row * 3 + column] = point;
            }
        float spacing = MathF.Min(extent[axes[0]], extent[axes[1]]) * 0.72f;
        float radius = MathF.Max(0.08f, MathF.Max(extent[axes[2]] * 1.02f, spacing * 0.45f));
        Geometry = new(nodes, radius);
        links = new (int[], Vector4)[vertices.Length];
        float softness = radius * radius * 0.25f;
        for (int v = 0; v < vertices.Length; v++)
        {
            var nearest = Enumerable.Range(0, nodes.Length)
                .OrderBy(n => Vector3.DistanceSquared(vertices[v].Position, nodes[n])).ThenBy(n => n).Take(4).ToArray();
            var weights = new Vector4();
            float total = 0;
            for (int k = 0; k < 4; k++)
            {
                weights[k] = 1 / MathF.Max(softness + Vector3.DistanceSquared(vertices[v].Position, nodes[nearest[k]]), 1e-8f);
                total += weights[k];
            }
            links[v] = (nearest, weights / total);
        }
    }

    /// <summary>Rest-mesh weights and four packed node indices for the tissue vertex shader.</summary>
    internal (Vector2 Weights, Vector2 WeightAndNodes) PackedInfluences(int index)
    {
        var (nodes, weights) = links[index];
        int packed = nodes[0] | nodes[1] << 4 | nodes[2] << 8 | nodes[3] << 12;
        return (new(weights.X, weights.Y), new(weights.Z, packed));
    }

    /// <summary>Writes coordinates relative to the physics pose's centre, without changing UVs or vertex metadata.</summary>
    public bool Deform(ReadOnlySpan<Matrix4x4> transforms, Span<Vertex> result)
    {
        if (transforms.Length != Geometry.RestNodes.Length || result.Length != source.Length) return false;
        for (int i = 0; i < source.Length; i++)
        {
            var point = Vector3.Zero;
            var normal = Vector3.Zero;
            var link = links[i];
            for (int k = 0; k < 4; k++)
            {
                var matrix = transforms[link.Nodes[k]];
                point += Vector3.Transform(source[i].Position, matrix) * link.Weights[k];
                normal += Vector3.TransformNormal(source[i].Normal, matrix) * link.Weights[k];
            }
            if (!Finite(point) || !Finite(normal)) return false;
            if (normal.LengthSquared() < 1e-12f) normal = source[i].Normal;
            if (normal.LengthSquared() < 1e-12f) normal = Vector3.UnitY;
            result[i] = source[i] with { Position = point, Normal = Vector3.Normalize(normal) };
        }
        return true;
    }

    static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
}
