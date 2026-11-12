// Adapted from integrated GPL-3.0-or-later source 3a15eeb9f5523b9d106b40fd0ec99ccdb2658efa.
using System.Numerics;
using System.Runtime.InteropServices;

namespace Meitou.Brutality.Rendering.Surface;

internal static partial class NativeSectionGeometry
{
    /// <summary>Projected skin bins make repeated depth samples local to the section rather than rescanning the whole body.</summary>
    sealed class SectionSkin
    {
        const float CellScale = 2;
        readonly Vector3 u, v;
        readonly Dictionary<(int X, int Y), List<int>> bins = [];
        readonly List<int> broad = [];
        public Vector3[] Vertices { get; }
        public SectionSkin(Vector3[] vertices, Vector3 normal)
        {
            Vertices = vertices;
            var axis = MathF.Abs(normal.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
            u = Vector3.Normalize(Vector3.Cross(axis, normal));
            v = Vector3.Cross(normal, u);
            Vector2 Project(Vector3 point) => new(Vector3.Dot(point, u), Vector3.Dot(point, v));
            for (int triangle = 0; triangle + 2 < vertices.Length; triangle += 3)
            {
                var a = Project(vertices[triangle]); var b = Project(vertices[triangle + 1]); var c = Project(vertices[triangle + 2]);
                var min = Vector2.Min(a, Vector2.Min(b, c)) - new Vector2(0.0001f);
                var max = Vector2.Max(a, Vector2.Max(b, c)) + new Vector2(0.0001f);
                if (!float.IsFinite(min.X) || !float.IsFinite(min.Y) || !float.IsFinite(max.X) || !float.IsFinite(max.Y)) continue;
                int x0 = (int)MathF.Floor(min.X * CellScale), x1 = (int)MathF.Floor(max.X * CellScale);
                int y0 = (int)MathF.Floor(min.Y * CellScale), y1 = (int)MathF.Floor(max.Y * CellScale);
                long width = (long)x1 - x0 + 1, height = (long)y1 - y0 + 1;
                if (width <= 0 || height <= 0 || width > 4096 || height > 4096 || width * height > 4096)
                {
                    broad.Add(triangle);
                    continue;
                }
                for (int x = x0; x <= x1; x++)
                    for (int y = y0; y <= y1; y++)
                    {
                        if (!bins.TryGetValue((x, y), out var list)) bins[(x, y)] = list = [];
                        list.Add(triangle);
                    }
            }
            if (broad.Count > 0)
                foreach (var bin in bins.Values) bin.AddRange(broad);
        }
        public ReadOnlySpan<int> Candidates(Vector3 origin)
        {
            var cell = ((int)MathF.Floor(Vector3.Dot(origin, u) * CellScale), (int)MathF.Floor(Vector3.Dot(origin, v) * CellScale));
            return bins.TryGetValue(cell, out var list) ? CollectionsMarshal.AsSpan(list) : CollectionsMarshal.AsSpan(broad);
        }
    }
}
