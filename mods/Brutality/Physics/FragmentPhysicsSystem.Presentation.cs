using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Data.Physics;

namespace Meitou.Brutality.Physics;

/// <summary>A complete unsplit body at topology birth, before physics changes its root. Immutable once published.</summary>
public sealed record FragmentBirthPresentation(CharacterAppearance Appearance, BonePose Bones, Matrix4x4 Placement);

public sealed partial class FragmentPhysicsSystem
{
    // Pending render work must not put a full body on the latest retained fragment's partial pose/root.
    // This complete, independently owned snapshot is a presentation fallback, never simulation input.
    internal static FragmentBirthPresentation? CaptureBirthPresentation(CharacterAppearance appearance,
        ReadOnlySpan<RigidTransform> worldBones, Matrix4x4 placement, bool restoring = false)
    {
        // Saved fragments are restored after topology creation. Their temporary animated pose was never shown.
        if (restoring) return null;
        var pose = new BonePose(worldBones.Length) { AbsolutePositions = true };
        Matrix4x4.Invert(placement, out var inverse);
        var unturn = Quaternion.Inverse(Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(
            placement with { M41 = 0, M42 = 0, M43 = 0 })));
        for (int bone = 0; bone < worldBones.Length; bone++)
        {
            pose.Set[bone] = true;
            pose.Position[bone] = Vector3.Transform(worldBones[bone].Position, inverse);
            pose.Rotation[bone] = Quaternion.Normalize(unturn * worldBones[bone].Rotation);
        }
        // No split component IDs: this pose belongs to the complete pre-separation mesh.
        return new(appearance, pose, placement);
    }
}
