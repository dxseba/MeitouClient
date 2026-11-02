using System.Numerics;
using Meitou.Data.Characters;

namespace Meitou.Simulation.Ragdolls;

internal sealed partial class RagdollSkinSurface
{
    /// <summary>The renderer's head box: actual Head/Jaw skin, excluding torso charts and articulated horns.</summary>
    internal bool HeadPlane(SkeletonRig rig, Vector4 normalized, out Vector4 plane)
    {
        plane = default;
        if (rig.Handle("Bip01 Head") is not int head) return false;
        int jaw = rig.Handle("Bip01 Jaw") ?? -1;
        var horns = morphs.Where(p => p.Name.Contains("horn", StringComparison.OrdinalIgnoreCase))
            .SelectMany(p => p.Vertices.Where(v => v.Offset.LengthSquared() > 0).Select(v => (p.Target, v.Index))).ToHashSet();
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        int count = 0;
        foreach (var point in triangles.SelectMany(t => new[] { t.A, t.B, t.C }))
        {
            if (point.UvY >= 0 || !Finite(point.Position) || horns.Contains((point.Target, point.Vertex))) continue;
            float weight = 0;
            for (int k = 0; k < 4; k++)
            {
                int bone = (int)(point.Bones >> (k * 8) & 255);
                if (bone == head || bone == jaw) weight += point.Weights[k];
            }
            if (weight < .5f || !float.IsFinite(weight)) continue;
            min = Vector3.Min(min, point.Position);
            max = Vector3.Max(max, point.Position);
            count++;
        }
        var half = (max - min) * .5f;
        var centre = (min + max) * .5f;
        if (count < 3 || !Finite(half) || half.X <= 0 || half.Y <= 0 || half.Z <= 0)
        {
            centre = rig.BindPosition[head];
            half = new(1, 1.5f, 1);
        }
        var normal = new Vector3(normalized.X, normalized.Y, normalized.Z) / half;
        float length = normal.Length();
        if (!float.IsFinite(length) || length < 1e-8f) return false;
        plane = new(normal / length, (normalized.W - Vector3.Dot(normal, centre)) / length);
        return true;
    }
}
