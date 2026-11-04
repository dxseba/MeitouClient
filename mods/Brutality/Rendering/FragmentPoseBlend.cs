using System.Numerics;
using Meitou.Data.Characters;

namespace Meitou.Brutality.Rendering;

/// <summary>Like the native host blend, alternate published objects so the renderer sees each changed articulated pose.</summary>
internal sealed class FragmentPoseBlend
{
    BonePose[] scratch = [];
    int next;

    public BonePose Between(BonePose? before, BonePose after, float alpha)
    {
        if (before is null || ReferenceEquals(before, after) || before.Set.Length != after.Set.Length
            || before.AbsolutePositions != after.AbsolutePositions || !ReferenceEquals(before.Components, after.Components)) return after;
        if (scratch.Length == 0 || scratch[0].Set.Length != after.Set.Length)
            scratch = [new(after.Set.Length), new(after.Set.Length)];
        var result = scratch[next ^= 1];
        result.Weight = before.Weight + (after.Weight - before.Weight) * alpha;
        result.AbsolutePositions = after.AbsolutePositions; result.Components = after.Components;
        for (int h = 0; h < result.Set.Length; h++)
        {
            result.Set[h] = after.Set[h];
            result.Position[h] = before.Set[h] && after.Set[h] ? Vector3.Lerp(before.Position[h], after.Position[h], alpha) : after.Position[h];
            result.Rotation[h] = before.Set[h] && after.Set[h] ? Quaternion.Slerp(before.Rotation[h], after.Rotation[h], alpha) : after.Rotation[h];
        }
        return result;
    }
}
