using System.Numerics;
using Meitou.Data.Characters;

namespace Meitou.Brutality.Rendering;

/// <summary>Moves the displayed skeleton while its replacement geometry is being prepared.</summary>
internal static class PendingCutPose
{
    internal static bool TryPose(SkeletonRig rig, BonePose shown, BonePose reference, BonePose current, BonePose result)
    {
        int count = rig.BoneCount;
        if (count == 0 || !MatchesRig(shown, count) || !MatchesRig(reference, count) || !MatchesRig(current, count)
            || result.Set.Length != count || ReferenceEquals(result, shown) || ReferenceEquals(result, reference)
            || ReferenceEquals(result, current))
            return false;

        Array.Clear(result.Set);
        result.AbsolutePositions = true;
        result.Components = shown.Components;
        result.Weight = 1;
        bool any = false;

        foreach (int bone in rig.Order)
        {
            if (!shown.Set[bone]) continue;
            int parent = rig.Parent[bone];
            if (parent >= 0 && result.Set[parent] && Connected(shown, parent, bone))
            {
                var inverseShownParent = Quaternion.Inverse(shown.Rotation[parent]);
                var localPosition = Vector3.Transform(shown.Position[bone] - shown.Position[parent], inverseShownParent);
                var localRotation = inverseShownParent * shown.Rotation[bone];

                // An edge already severed in the driver cannot bend the still-connected displayed skeleton.
                if (Connected(reference, parent, bone) && Connected(current, parent, bone))
                {
                    var referenceLocal = Quaternion.Inverse(reference.Rotation[parent]) * reference.Rotation[bone];
                    var currentLocal = Quaternion.Inverse(current.Rotation[parent]) * current.Rotation[bone];
                    localRotation = currentLocal * Quaternion.Inverse(referenceLocal) * localRotation;
                }

                result.Position[bone] = result.Position[parent] + Vector3.Transform(localPosition, result.Rotation[parent]);
                result.Rotation[bone] = Quaternion.Normalize(result.Rotation[parent] * localRotation);
            }
            else if (reference.Set[bone] && current.Set[bone])
            {
                var turn = Quaternion.Normalize(current.Rotation[bone] * Quaternion.Inverse(reference.Rotation[bone]));
                result.Position[bone] = current.Position[bone] + Vector3.Transform(shown.Position[bone] - reference.Position[bone], turn);
                result.Rotation[bone] = Quaternion.Normalize(turn * shown.Rotation[bone]);
            }
            else
            {
                result.Position[bone] = shown.Position[bone];
                result.Rotation[bone] = shown.Rotation[bone];
            }

            if (!float.IsFinite(result.Position[bone].LengthSquared()) || !float.IsFinite(result.Rotation[bone].LengthSquared()))
                return false;
            result.Set[bone] = true;
            any = true;
        }

        return any;
    }

    static bool MatchesRig(BonePose pose, int count) =>
        pose.AbsolutePositions && pose.Set.Length == count && (pose.Components is null || pose.Components.Length == count);

    static bool Connected(BonePose pose, int parent, int bone) =>
        pose.Set[parent] && pose.Set[bone] && (pose.Components is null || pose.Components[parent] == pose.Components[bone]);
}
