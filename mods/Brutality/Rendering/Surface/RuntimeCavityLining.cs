using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Rendering;

namespace Meitou.Brutality.Rendering.Surface;

/// <summary>Reconstruct the reference's U-shaped inner shell from the user's installed body, without shipping its baked coordinate tables.</summary>
internal static class RuntimeCavityLining
{
    internal const int Rows = 17, Columns = 27;
    const float FirstHeight = .72f, LastHeight = 6.30f, Clearance = .12f;
    readonly record struct Segment(Vector2 A, Vector2 B);

    internal static Vector3[,]? Build(IReadOnlyList<Vertex> body, SkeletonRig rig, Vector3 hip, float scale)
    {
        if (body.Count == 0 || !float.IsFinite(scale) || scale <= 0) return null;
        var groups = MeshCut.BoneGroups(rig);
        var slices = new List<Segment>[Rows];
        var heights = new float[Rows];
        for (int row = 0; row < Rows; row++)
        {
            heights[row] = hip.Y + (FirstHeight + (LastHeight - FirstHeight) * row / (Rows - 1)) * scale;
            slices[row] = Slice(body, groups, heights[row]);
        }
        if (slices.All(section => section.Count == 0)) return null;
        var wall = new Vector3[Rows, Columns];
        for (int row = 0; row < Rows; row++)
        {
            var section = slices[row];
            if (section.Count == 0)
                section = slices[Enumerable.Range(0, Rows).Where(index => slices[index].Count > 0).MinBy(index => Math.Abs(index - row))];
            float minimum = section.Min(edge => MathF.Min(edge.A.X, edge.B.X));
            float maximum = section.Max(edge => MathF.Max(edge.A.X, edge.B.X));
            float inset = MathF.Min(Clearance * scale, (maximum - minimum) * .15f);
            float left = minimum + inset, right = maximum - inset;
            var leftRange = Range(section, left); var rightRange = Range(section, right);
            float backLeft = Back(leftRange), backRight = Back(rightRange);
            float frontLeft = Front(leftRange), frontRight = Front(rightRange);
            for (int column = 0; column < Columns; column++)
            {
                float x, z;
                if (column < 6)
                { x = left; z = frontLeft + (backLeft - frontLeft) * column / 5; }
                else if (column >= 21)
                { x = right; z = backRight + (frontRight - backRight) * (column - 21) / 5; }
                else
                { x = left + (right - left) * (column - 5) / 16; z = Back(Range(section, x)); }
                wall[row, column] = new(x, heights[row], z);
            }

            float Back((float Back, float Front) range) => range.Back + MathF.Min(inset, (range.Front - range.Back) * .25f);
            // The anterior body remains open, as in the reference shell. Flanks turn forward only partway around the torso.
            float Front((float Back, float Front) range) => Math.Clamp((range.Back + range.Front) * .5f + (range.Front - range.Back) * .10f,
                Back(range), range.Front - MathF.Min(inset, (range.Front - range.Back) * .25f));
        }
        return wall;
    }

    static (float Back, float Front) Range(List<Segment> section, float x)
    {
        float back = float.PositiveInfinity, front = float.NegativeInfinity;
        foreach (var edge in section)
        {
            float span = edge.B.X - edge.A.X;
            if (MathF.Abs(span) <= 1e-7f)
            {
                if (MathF.Abs(x - edge.A.X) > .0001f) continue;
                back = MathF.Min(back, MathF.Min(edge.A.Y, edge.B.Y)); front = MathF.Max(front, MathF.Max(edge.A.Y, edge.B.Y));
                continue;
            }
            float at = (x - edge.A.X) / span;
            if (at < -.0001f || at > 1.0001f) continue;
            float z = edge.A.Y + (edge.B.Y - edge.A.Y) * Math.Clamp(at, 0, 1);
            back = MathF.Min(back, z); front = MathF.Max(front, z);
        }
        if (float.IsFinite(back) && float.IsFinite(front)) return (back, front);
        // Open source shells can lack one side. A nearby measured cross-section is preferable to a free-floating planar guess.
        var nearest = section.SelectMany(edge => new[] { edge.A, edge.B }).OrderBy(point => MathF.Abs(point.X - x)).Take(4).ToArray();
        return (nearest.Min(point => point.Y), nearest.Max(point => point.Y));
    }

    static List<Segment> Slice(IReadOnlyList<Vertex> body, int[] groups, float height)
    {
        var result = new List<Segment>();
        Span<Vector2> points = stackalloc Vector2[6];
        for (int triangle = 0; triangle + 2 < body.Count; triangle += 3)
        {
            int axial = 0;
            for (int k = 0; k < 3; k++) if (MeshCut.Group(body[triangle + k], groups) == 0) axial++;
            if (axial < 2) continue; // Arms beside the hips and head charts never widen the torso shell.
            int count = 0;
            for (int k = 0; k < 3; k++)
            {
                var a = body[triangle + k].Position; var b = body[triangle + (k + 1) % 3].Position;
                float first = a.Y - height, second = b.Y - height;
                if (MathF.Abs(first) < .00001f) Add(points, ref count, new(a.X, a.Z));
                if ((first < 0) != (second < 0))
                {
                    var hit = Vector3.Lerp(a, b, first / (first - second)); Add(points, ref count, new(hit.X, hit.Z));
                }
            }
            if (count < 2) continue;
            int left = 0, right = 1; float farthest = Vector2.DistanceSquared(points[0], points[1]);
            for (int a = 0; a < count; a++) for (int b = a + 1; b < count; b++)
                if (Vector2.DistanceSquared(points[a], points[b]) > farthest)
                { left = a; right = b; farthest = Vector2.DistanceSquared(points[a], points[b]); }
            if (farthest > 1e-10f) result.Add(new(points[left], points[right]));

        }
        return result;
    }

    static void Add(Span<Vector2> points, ref int count, Vector2 point)
    {
        for (int i = 0; i < count; i++) if (Vector2.DistanceSquared(points[i], point) < 1e-10f) return;
        points[count++] = point;
    }
}
