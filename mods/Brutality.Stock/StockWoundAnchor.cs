using Meitou.Game.Rendering.Frame;
using System.Numerics;

namespace Meitou.Brutality.Effects;

internal static class StockWoundAnchor
{
    const int SampleLimit = 3;
    public static CutFluidContact? Pose(ReadOnlySpan<Vertex> rim, ReadOnlySpan<Matrix4x4> bones, Matrix4x4 world)
    {
        var centre = Vector3.Zero; var normal = Vector3.Zero;
        int count = Math.Min(rim.Length, SampleLimit), valid = 0;
        for (int i = 0; i < count; i++)
        {
            var vertex = rim[i]; var point = Vector3.Zero; var direction = Vector3.Zero; float sum = 0;
            for (int k = 0; k < 4; k++)
            {
                int bone = (int)(vertex.Bones >> (k * 8) & 255); float weight = vertex.Weights[k];
                if ((uint)bone >= (uint)bones.Length || weight <= 0 || !float.IsFinite(weight)) continue;
                point += Vector3.Transform(vertex.Position, bones[bone]) * weight;
                direction += Vector3.TransformNormal(vertex.Normal, bones[bone]) * weight; sum += weight;
            }
            if (sum <= 0) continue;
            centre += Vector3.Transform(point / sum, world); normal += Vector3.TransformNormal(direction / sum, world); valid++;
        }
        if (valid == 0 || normal.LengthSquared() < 1e-10f) return null;
        centre /= valid; normal = Vector3.Normalize(normal);
        return float.IsFinite(centre.LengthSquared()) && float.IsFinite(normal.LengthSquared()) ? new(centre, normal) : null;
    }
}
