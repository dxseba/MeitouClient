using System.Numerics;

namespace Meitou.Rendering.Characters;

/// <summary>Index-only interior detail levels; original vertices, skinning, layers and close-up geometry stay unchanged.</summary>
internal static class AnatomyMeshLod
{
    static readonly float[] Cells = [0.06f, 0.12f, 0.24f];
    readonly record struct Key(int X, int Y, int Z, uint Bones, Vector4 Weights, Vector4 Colour, float Layer,
        int U, int V, int Normal);

    public static uint[][] Build(Vertex[] vertices)
    {
        var full = Enumerable.Range(0, vertices.Length).Select(i => (uint)i).ToArray();
        if (vertices.Length < 6000) return [full];
        return [full, .. Cells.Select(cell => Cluster(vertices, cell))];
    }

    public static int LevelForPixels(float pixels, int levels) => Math.Clamp(
        pixels >= 650 ? 0 : pixels >= 400 ? 1 : pixels >= 260 ? 2 : 3, 0, Math.Max(0, levels - 1));

    static int NormalGroup(Vector3 normal)
    {
        var size = Vector3.Abs(normal);
        int axis = size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.Z ? 1 : 2;
        return axis * 2 + (normal[axis] < 0 ? 1 : 0);
    }

    static uint[] Cluster(Vertex[] vertices, float cell)
    {
        var representatives = new Dictionary<Key, uint>();
        var remap = new uint[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            ref readonly var v = ref vertices[i];
            var key = new Key((int)MathF.Floor(v.Position.X / cell), (int)MathF.Floor(v.Position.Y / cell),
                (int)MathF.Floor(v.Position.Z / cell), v.Bones, v.Weights, v.Colour, v.Tangent.W,
                (int)MathF.Floor(v.Uv.X * 2), (int)MathF.Floor(v.Uv.Y * 2), NormalGroup(v.Normal));
            if (!representatives.TryGetValue(key, out uint index)) representatives.Add(key, index = (uint)i);
            remap[i] = index;
        }
        var indices = new List<uint>();
        for (int i = 0; i + 2 < vertices.Length; i += 3)
        {
            uint a = remap[i], b = remap[i + 1], c = remap[i + 2];
            if (a == b || b == c || c == a) continue;
            var before = Vector3.Cross(vertices[i + 1].Position - vertices[i].Position, vertices[i + 2].Position - vertices[i].Position);
            var after = Vector3.Cross(vertices[b].Position - vertices[a].Position, vertices[c].Position - vertices[a].Position);
            // Preserve triangles that would flip rather than opening an inverted patch across a narrow feature.
            if (Vector3.Dot(before, after) <= 0) { a = (uint)i; b = (uint)(i + 1); c = (uint)(i + 2); }
            indices.Add(a); indices.Add(b); indices.Add(c);
        }
        return indices.Count > 0 ? indices.ToArray() : Enumerable.Range(0, vertices.Length).Select(i => (uint)i).ToArray();
    }
}