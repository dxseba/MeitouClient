using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;
using Meitou.Rendering;

namespace Meitou.Brutality.Rendering.Surface;

/// <summary>
/// The punched tunnel's wall: a tube around the hole's axis from the front skin to the back skin. Each ring meets the (morphed) skin where
/// a line along the axis crosses it, and takes that skin's bone weights, so the wall bends with the body as the skin does.
/// </summary>
internal static class ChestHoleGeometry
{
    const int Around = 28, Along = 6;
    /// <summary>How far each end stays inside the skin (bind units).</summary>
    const float Inset = .03f;

    readonly record struct Face(Vector3 A, Vector3 B, Vector3 C, Vertex VA, Vertex VB, Vertex VC);
    readonly record struct Crossing(Vector3 Position, uint Bones, Vector4 Weights);

    public static OgreMesh? Lining(Vertex[] body, Vector4[]? morphs, SkeletonRig rig, in ChestHoleFrame frame, uint blood, string? skeleton)
    {
        var faces = TorsoFaces(body, morphs, rig);
        var ends = new (Crossing Entry, Crossing Exit)?[Around];
        for (int k = 0; k < Around; k++) ends[k] = Ends(faces, frame, k * MathF.Tau / Around);
        if (!FillGaps(ends)) return null;

        // The colour's alpha tells the wall's shader the region (0 chest, 1 stomach).
        var colour = NativeFluidPalette.Recolour(new Vector4(.30f, .02f, .025f, 0), blood) with { W = frame.Region };
        var vertices = new List<Vertex>(Around * (Along - 1) * 6);
        for (int k = 0; k < Around; k++)
            for (int j = 0; j + 1 < Along; j++)
                Quad(vertices, ends, frame, k, j, colour);
        return MeshCut.FromTriangles(vertices, skeleton, preserveRawVertexChannels: true);
    }

    /// <summary>
    /// A first line at the entry's radius finds the skin's depth there; the exit then sits on the lining radius of its own depth, so a
    /// flared exit stays outside its wider edge.
    /// </summary>
    static (Crossing, Crossing)? Ends(List<Face> faces, in ChestHoleFrame frame, float turn)
    {
        float entryRadius = frame.LiningRadius(-2 * frame.Radius);
        if (Cross(faces, frame.Centre + frame.Across(turn, entryRadius), frame.Axis) is not { } first) return null;
        float exitRadius = frame.LiningRadius(Vector3.Dot(first.Item2.Position - frame.Centre, frame.Axis));
        if (exitRadius - entryRadius < 1e-3f) return first;
        return Cross(faces, frame.Centre + frame.Across(turn, exitRadius), frame.Axis) is { } wide ? (first.Item1, wide.Item2) : first;
    }

    /// <summary>
    /// The body's torso triangles with the look's morphs applied; limbs hang beside the chest in the bind pose and must not stop a ray.
    /// </summary>
    static List<Face> TorsoFaces(Vertex[] body, Vector4[]? morphs, SkeletonRig rig)
    {
        var groups = MeshCut.BoneGroups(rig);
        var faces = new List<Face>(body.Length / 6);
        for (int i = 0; i + 2 < body.Length; i += 3)
        {
            if (!Torso(body[i], groups) || !Torso(body[i + 1], groups) || !Torso(body[i + 2], groups)) continue;
            faces.Add(new(Morphed(body[i], morphs), Morphed(body[i + 1], morphs), Morphed(body[i + 2], morphs), body[i], body[i + 1], body[i + 2]));
        }
        return faces;
    }

    static bool Torso(in Vertex vertex, int[] groups)
    {
        int at = 0;
        for (int k = 1; k < 4; k++) if (vertex.Weights[k] > vertex.Weights[at]) at = k;
        int bone = (int)(vertex.Bones >> (at * 8) & 255);
        return bone < groups.Length && groups[bone] == 0;
    }

    static Vector3 Morphed(in Vertex vertex, Vector4[]? morphs)
    {
        uint slot = BitConverter.SingleToUInt32Bits(vertex.Colour.X);
        return morphs is not null && slot > 0 && slot <= morphs.Length
            ? vertex.Position + new Vector3(morphs[slot - 1].X, morphs[slot - 1].Y, morphs[slot - 1].Z) : vertex.Position;
    }

    /// <summary>The first and last skin crossings of the line through <paramref name="origin"/> along the axis: entry and exit.</summary>
    static (Crossing, Crossing)? Cross(List<Face> faces, Vector3 origin, Vector3 axis)
    {
        float first = float.MaxValue, last = float.MinValue;
        Crossing entry = default, exit = default;
        foreach (var face in faces)
        {
            if (!Intersect(origin, axis, face, out float distance, out var bary)) continue;
            if (distance < first) { first = distance; entry = At(face, bary, origin + axis * (distance + Inset)); }
            if (distance > last) { last = distance; exit = At(face, bary, origin + axis * (distance - Inset)); }
        }
        return last - first > 4 * Inset ? (entry, exit) : null;
    }

    /// <summary>Möller–Trumbore on the whole line: the distance may be negative.</summary>
    static bool Intersect(Vector3 origin, Vector3 direction, in Face face, out float distance, out Vector3 bary)
    {
        distance = 0;
        bary = default;
        var e1 = face.B - face.A;
        var e2 = face.C - face.A;
        var p = Vector3.Cross(direction, e2);
        float det = Vector3.Dot(e1, p);
        if (MathF.Abs(det) < 1e-9f) return false;

        float inverse = 1 / det;
        var s = origin - face.A;
        float u = Vector3.Dot(s, p) * inverse;
        if (u < 0 || u > 1) return false;
        var q = Vector3.Cross(s, e1);
        float v = Vector3.Dot(direction, q) * inverse;
        if (v < 0 || u + v > 1) return false;

        distance = Vector3.Dot(e2, q) * inverse;
        bary = new(1 - u - v, u, v);
        return true;
    }

    static Crossing At(in Face face, Vector3 bary, Vector3 position)
    {
        var (bones, weights) = Blend([(face.VA, bary.X), (face.VB, bary.Y), (face.VC, bary.Z)]);
        return new(position, bones, weights);
    }

    /// <summary>The four heaviest bones of a weighted mix of skin vertices, renormalized.</summary>
    static (uint Bones, Vector4 Weights) Blend(ReadOnlySpan<(Vertex Vertex, float Share)> sources)
    {
        var mix = new Dictionary<int, float>();
        foreach (var (vertex, share) in sources)
            for (int k = 0; k < 4; k++)
            {
                float weight = vertex.Weights[k] * share;
                if (weight <= 0) continue;
                int bone = (int)(vertex.Bones >> (k * 8) & 255);
                mix[bone] = mix.GetValueOrDefault(bone) + weight;
            }
        var top = mix.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).Take(4).ToArray();
        float total = top.Sum(pair => pair.Value);
        if (top.Length == 0 || total <= 0) return (0, Vector4.UnitX);

        uint bones = 0;
        var weights = new float[4];
        for (int i = 0; i < top.Length; i++) { bones |= (uint)top[i].Key << (i * 8); weights[i] = top[i].Value / total; }
        return (bones, new(weights[0], weights[1], weights[2], weights[3]));
    }

    /// <summary>A ring that missed the skin takes its nearest found neighbours' ends. Too many misses: no lining.</summary>
    static bool FillGaps((Crossing Entry, Crossing Exit)?[] ends)
    {
        int missing = ends.Count(end => end is null);
        if (missing > Around / 4) return false;
        for (int k = 0; k < Around; k++)
        {
            if (ends[k] is not null) continue;
            for (int step = 1; step < Around && ends[k] is null; step++)
                ends[k] = ends[(k + step) % Around] ?? ends[(k - step + Around) % Around];
        }
        return true;
    }

    static void Quad(List<Vertex> output, (Crossing Entry, Crossing Exit)?[] ends, in ChestHoleFrame frame, int k, int j, Vector4 colour)
    {
        int next = (k + 1) % Around;
        var a = Point(ends, frame, k, j, colour);
        var b = Point(ends, frame, next, j, colour);
        var c = Point(ends, frame, k, j + 1, colour);
        var d = Point(ends, frame, next, j + 1, colour);
        // cross(b - a, c - a) points at the axis, as the vertex normals do: the wall's front faces the tunnel.
        output.Add(a); output.Add(b); output.Add(c);
        output.Add(b); output.Add(d); output.Add(c);
    }

    /// <summary>Ring <paramref name="j"/> of column <paramref name="k"/>, between its entry and exit; the wall faces the axis.</summary>
    static Vertex Point((Crossing Entry, Crossing Exit)?[] ends, in ChestHoleFrame frame, int k, int j, Vector4 colour)
    {
        var (entry, exit) = ends[k]!.Value;
        float along = j / (float)(Along - 1);
        var position = Vector3.Lerp(entry.Position, exit.Position, along);
        var across = position - frame.Centre;
        across -= frame.Axis * Vector3.Dot(across, frame.Axis);
        var (bones, weights) = Blend([(AsVertex(entry), 1 - along), (AsVertex(exit), along)]);
        float grain = .9f + .1f * MathF.Sin(position.X * 31 + position.Y * 17 + position.Z * 23);
        return new Vertex
        {
            Position = position, Normal = -Vector3.Normalize(across), Uv = new(along, k / (float)Around),
            Tangent = new(position, NativeSurfaceShaders.ChestHoleLayer), Colour = (colour * grain) with { W = colour.W },
            Bones = bones, Weights = weights,
        };
    }

    static Vertex AsVertex(in Crossing crossing) => new() { Bones = crossing.Bones, Weights = crossing.Weights };
}
