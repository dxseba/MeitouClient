using System.Numerics;
using Meitou.Game.Rendering.Frame;

namespace Meitou.FlowingHair;

/// <summary>
/// Which vertices of a body mesh are its tail (the original's tail profiles, facts only): the Khajiit bodies' tails are two runs of triangles of
/// the one submesh with the profile's triangle count, the Cat Mod's are its pieces wholly inside the tail box. A tail shares no vertex with the
/// rest of the body, lies inside the profile's box (binding space), is at least 5 units tall and reaches 8 units up; anything else is not
/// taken (the body is drawn as the file has it).
/// </summary>
static class TailProfile
{
    /// <summary>A profile: the submesh's triangle count, the two runs (first triangles, length) and the box the tail lies in.</summary>
    readonly record struct Profile(int Triangles, int First, int Second, int Run, Vector3 Min, Vector3 Max);

    static readonly Profile KhajjitMale = new(139062, 68446, 137977, 1085, new(-2, -1, -7), new(2, 13, 0));
    static readonly Profile KhajjitFemale = new(33317, 16837, 32333, 984, new(-2, -1, -7), new(2, 13, 0));

    /// <summary>The Cat Mod's tail: 2 x 152 triangles of the 8376 (the runs are not contiguous, so the pieces are found by the box).</summary>
    static readonly Profile Cat = new(8376, 0, 0, 152, new(-3, 0, -22), new(3, 15, -6));

    /// <summary>Per part, which vertices are the tail; null when no part matches a profile.</summary>
    public static bool[][]? Select(HairKind kind, IReadOnlyList<ModelPart> parts)
    {
        var profiles = kind == HairKind.CatTail ? new[] { Cat } : new[] { KhajjitMale, KhajjitFemale };
        for (int p = 0; p < parts.Count; p++)
        {
            foreach (var profile in profiles)
            {
                if (parts[p].Indices.Length != profile.Triangles * 3) continue;

                bool[]? faces = kind == HairKind.CatTail ? BoxPieces(parts[p], profile) : Runs(profile);
                if (faces is not null && Vertices(parts[p], faces, profile) is { } tail) return Mask(parts, p, tail);
            }
        }

        return null;
    }

    static bool[] Runs(Profile profile)
    {
        var faces = new bool[profile.Triangles];
        for (int f = 0; f < profile.Run; f++)
        {
            if (profile.First + f < faces.Length) faces[profile.First + f] = true;
            if (profile.Second + f < faces.Length) faces[profile.Second + f] = true;
        }

        return faces;
    }

    /// <summary>The triangles of every connected piece of the part whose vertices all lie in the box; null unless they are 2 x the run.</summary>
    static bool[]? BoxPieces(ModelPart part, Profile profile)
    {
        var root = new int[part.Vertices.Length];
        for (int i = 0; i < root.Length; i++) root[i] = i;

        int Find(int i)
        {
            while (root[i] != i) i = root[i] = root[root[i]];

            return i;
        }

        var indices = part.Indices;
        for (int t = 0; t + 2 < indices.Length; t += 3)
        {
            root[Find((int)indices[t + 1])] = Find((int)indices[t]);
            root[Find((int)indices[t + 2])] = Find((int)indices[t]);
        }

        var outside = new HashSet<int>();
        for (int i = 0; i < root.Length; i++)
        {
            if (!Inside(part.Vertices[i].Position, profile)) outside.Add(Find(i));
        }

        var faces = new bool[indices.Length / 3];
        int count = 0;
        for (int f = 0; f < faces.Length; f++)
        {
            faces[f] = !outside.Contains(Find((int)indices[3 * f]));
            if (faces[f]) count++;
        }

        return count == 2 * profile.Run ? faces : null;
    }

    static bool Inside(Vector3 p, Profile profile) =>
        p.X > profile.Min.X && p.X < profile.Max.X && p.Y > profile.Min.Y && p.Y < profile.Max.Y && p.Z > profile.Min.Z && p.Z < profile.Max.Z;

    /// <summary>The tail triangles' vertices, checked as the original checks them; null when a check fails.</summary>
    static bool[]? Vertices(ModelPart part, bool[] faces, Profile profile)
    {
        var membership = new byte[part.Vertices.Length];
        var indices = part.Indices;
        for (int t = 0; t < indices.Length; t++)
        {
            if (indices[t] >= membership.Length) return null;

            membership[indices[t]] |= faces[t / 3] ? (byte)2 : (byte)1;
        }

        var tail = new bool[membership.Length];
        float bottom = float.MaxValue, top = float.MinValue;
        for (int i = 0; i < membership.Length; i++)
        {
            if (membership[i] == 3) return null;

            if (membership[i] != 2) continue;

            var p = part.Vertices[i].Position;
            if (!Inside(p, profile)) return null;

            (tail[i], bottom, top) = (true, MathF.Min(bottom, p.Y), MathF.Max(top, p.Y));
        }

        return top - bottom >= 5 && top >= 8 ? tail : null;
    }

    static bool[][] Mask(IReadOnlyList<ModelPart> parts, int tailPart, bool[] tail)
    {
        var mask = new bool[parts.Count][];
        for (int p = 0; p < parts.Count; p++) mask[p] = p == tailPart ? tail : new bool[parts[p].Vertices.Length];

        return mask;
    }
}
