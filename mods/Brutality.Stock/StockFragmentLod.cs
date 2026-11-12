using Meitou.Game.Rendering.Frame;
using System.Numerics;

namespace Meitou.Brutality;

/// <summary>A single inexpensive distant level. Open edges are pinned and all levels share the original vertices.</summary>
internal static class StockFragmentLod
{
    readonly record struct Cell(int X, int Y, int Z, uint Bones, int Weights, int Normal, int U, int V);

    internal static uint[] Reduce(ModelPart part, float spacing, out float deviation)
    {
        deviation = 0;
        var vertices = part.Vertices;
        var indices = part.Indices;
        if (indices.Length < 1800) return indices;

        // Match coincident UV and normal seams before identifying the boundary of the actual surface.
        var points = new Dictionary<Vector3, int>(vertices.Length);
        var pointOf = new int[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            var position = vertices[i].Position;
            if (!float.IsFinite(position.LengthSquared())) return indices;
            if (!points.TryGetValue(position, out int point)) points.Add(position, point = points.Count);
            pointOf[i] = point;
        }
        var edges = new Dictionary<(int, int), byte>();
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            int a = pointOf[indices[i]], b = pointOf[indices[i + 1]], c = pointOf[indices[i + 2]];
            if (a == b || a == c || b == c) continue;
            Edge(a, b); Edge(b, c); Edge(c, a);
        }
        var fixedPoint = new bool[points.Count];
        foreach (var (edge, count) in edges)
            if (count != 2) fixedPoint[edge.Item1] = fixedPoint[edge.Item2] = true;

        var cells = new Dictionary<Cell, int>();
        var map = new uint[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            map[i] = (uint)i;
            if (fixedPoint[pointOf[i]]) continue;
            var v = vertices[i];
            int weights = 0;
            for (int k = 0; k < 4; k++) weights |= Math.Clamp((int)MathF.Round(v.Weights[k] * 8), 0, 8) << (k * 4);
            var p = v.Position / spacing;
            var n = Vector3.Abs(v.Normal);
            int normal = n.X >= n.Y && n.X >= n.Z ? (v.Normal.X >= 0 ? 0 : 1)
                : n.Y >= n.Z ? (v.Normal.Y >= 0 ? 2 : 3) : v.Normal.Z >= 0 ? 4 : 5;
            var cell = new Cell(Bin(p.X), Bin(p.Y), Bin(p.Z), v.Bones, weights, normal, Bin(v.Uv.X * 16), Bin(v.Uv.Y * 16));
            if (cells.TryGetValue(cell, out int other))
            {
                if (Vector3.Dot(v.Normal, vertices[other].Normal) < .75f) continue;
                map[i] = (uint)other;
                deviation = MathF.Max(deviation, Vector3.Distance(v.Position, vertices[other].Position));
            }
            else cells.Add(cell, i);
        }

        var reduced = new List<uint>(indices.Length);
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            uint a = map[indices[i]], b = map[indices[i + 1]], c = map[indices[i + 2]];
            if (a == b || a == c || b == c) continue;
            reduced.Add(a); reduced.Add(b); reduced.Add(c);
        }
        return reduced.Count > 0 && reduced.Count < indices.Length * .86f ? reduced.ToArray() : indices;

        static int Bin(float number) => (int)MathF.Floor(number);
        void Edge(int a, int b)
        {
            var key = a < b ? (a, b) : (b, a);
            edges[key] = (byte)Math.Min(3, edges.GetValueOrDefault(key) + 1);
        }
    }
}
