using System.Numerics;

namespace Meitou.Rendering.Characters;

/// <summary>Whole, headless and detached-head views of one fitted interior, sharing vertices and indices.</summary>
internal sealed record AnatomyMeshRegions(uint[] Indices, AnatomyMeshRegion Whole, AnatomyMeshRegion Body, AnatomyMeshRegion Head)
{
    public static AnatomyMeshRegions Build(Vertex[] vertices, uint[][] levels, int headBone, int jawBone)
    {
        bool IsHead(uint index)
        {
            int bone = (int)(vertices[index].Bones & 255);
            return bone == headBone || bone == jawBone;
        }
        var indices = new uint[levels.Sum(level => level.Length)];
        var offsets = new int[levels.Length];
        var headOffsets = new int[levels.Length];
        var counts = levels.Select(level => level.Length).ToArray();
        var bodyCounts = new int[levels.Length];
        var headCounts = new int[levels.Length];
        int offset = 0;
        for (int level = 0; level < levels.Length; level++)
        {
            var source = levels[level];
            offsets[level] = offset;
            // A fitted anatomy triangle follows one bone. Group it without copying or fitting its vertices again.
            for (int i = 0; i + 2 < source.Length; i += 3)
                if (!IsHead(source[i]))
                {
                    source.AsSpan(i, 3).CopyTo(indices.AsSpan(offset));
                    offset += 3;
                    bodyCounts[level] += 3;
                }
            headOffsets[level] = offset;
            for (int i = 0; i + 2 < source.Length; i += 3)
                if (IsHead(source[i]))
                {
                    source.AsSpan(i, 3).CopyTo(indices.AsSpan(offset));
                    offset += 3;
                    headCounts[level] += 3;
                }
        }
        AnatomyMeshRegion Region(int[] starts, int[] sizes)
        {
            var min = new Vector3(float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity);
            if (starts.Length == 0 || sizes[0] == 0) return new(starts, sizes, Vector3.Zero, 0);
            foreach (uint index in indices.AsSpan(starts[0], sizes[0]))
            {
                var position = vertices[index].Position;
                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);
            }
            var centre = (min + max) * 0.5f;
            return new(starts, sizes, centre, Vector3.Distance(min, max) * 0.5f);
        }
        return new(indices, Region(offsets, counts), Region(offsets, bodyCounts), Region(headOffsets, headCounts));
    }
}

internal sealed record AnatomyMeshRegion(int[] Offsets, int[] Counts, Vector3 Centre, float Radius);
