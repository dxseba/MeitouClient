using System.Numerics;
using Meitou.Brutality.Physics;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;
using Meitou.Data.Physics;
using RigidTransform = Meitou.Brutality.Physics.RigidTransform;

namespace Meitou.Brutality
{
    /// <summary>Adapts the original articulated fragment solver to the stock renderer's skin palette.</summary>
    internal static class StockTorsoPhysics
    {
        internal sealed record Prepared(StockTorsoBuild.Cut Cut, RagdollDefinition Positive, RagdollDefinition Negative);

        internal static Prepared? Build(string folder, CharacterRigs rigs, CharacterAppearance look, TorsoBisectionState cut,
            CancellationToken cancellation, StockTorsoBuild.Prior? prior = null, StockTorsoSave.Pose? saved = null)
        {
            if (StockTorsoBuild.Build(folder, rigs, look, cut, cancellation, prior) is not { } prepared) return null;
            string ragdoll = look.Race.GetPath(look.Female ? "female ragdoll" : "male ragdoll");
            if (string.IsNullOrWhiteSpace(ragdoll)) ragdoll = look.Race.GetPath("male ragdoll");
            if (rigs.Find(ragdoll) is not { } path) return null;
            var rig = prepared.Rig;
            var source = RagdollDefinition.Build(PhysicsFile.ReadFile(path), rig, 1, Hull);
            if (source is null) return null;
            if (look.Race.GetBool("is robot")) source = RobotRagdollWeight.Weight(source);
            cancellation.ThrowIfCancellationRequested();
            var retained = source;
            if (prior is { Limbs.Length: > 0 } && prepared.Pieces[0].HiddenLimbs is not null)
                foreach (var limb in prior.Limbs)
                    if (LimbCutGeometry.TryPlane(rig, limb.Shape, out var plane))
                        retained = RagdollSeverance.Split(retained, limb.Shape.LimbSlot, plane).Remaining ?? retained;
            if (look.Headless)
                retained = (prepared.AttachedHeadPlane is { } head
                    ? RagdollSeverance.SplitHead(retained, head, HeadCutMode.AttachedSplit).Remaining
                    : RagdollSeverance.Split(retained, -1).Remaining) ?? retained;
            if (!ReferenceEquals(retained, source) && Split(retained, prepared.RetainedSkin) is { } trimmed
                && (saved is null || AdvancedFragmentScene.MatchesStockDefinition(trimmed.Positive, saved.Positive)
                    && AdvancedFragmentScene.MatchesStockDefinition(trimmed.Negative, saved.Negative))) return trimmed;

            // Earlier saves captured the untrimmed definition. Keep their existing poses and joints loadable.
            return Split(source, prepared.SourceSkin);

            Prepared? Split(RagdollDefinition definition, OgreMesh mesh)
            {
                var skin = RagdollSkinSurface.Read(mesh);
                var positive = RagdollCut.Build(definition, prepared.Plane, 1, cut.FullBody, skin.Bridges(rig, definition, prepared.Plane, 1));
                cancellation.ThrowIfCancellationRequested();
                var negative = RagdollCut.Build(definition, prepared.Plane, -1, cut.FullBody, skin.Bridges(rig, definition, prepared.Plane, -1));
                return positive is not null && negative is not null ? new(prepared, positive, negative) : null;
            }

            Vector3[]? Hull(string name) => rigs.Find(name) is { } hull ? CookedMeshReader.ReadConvex(File.ReadAllBytes(hull)).Vertices : null;
        }

        internal static Matrix4x4 Bind(SkeletonRig rig, int bone) => Matrix4x4.CreateScale(rig.BindScale[bone])
            * Matrix4x4.CreateFromQuaternion(Quaternion.Inverse(rig.BindRotationInverse[bone]))
            * Matrix4x4.CreateTranslation(rig.BindPosition[bone]);

        internal static RigidTransform[]? BoneWorld(SkeletonRig rig, Matrix4x4[] palette, Matrix4x4 world)
        {
            if (palette.Length != rig.BoneCount) return null;
            var result = new RigidTransform[palette.Length];
            for (int bone = 0; bone < palette.Length; bone++)
            {
                var matrix = Bind(rig, bone) * palette[bone] * world;
                if (!Matrix4x4.Decompose(matrix, out _, out var rotation, out var position))
                {
                    // A strong body slider can shear the matrix slightly. Extract an orthonormal basis.
                    var x = new Vector3(matrix.M11, matrix.M12, matrix.M13);
                    var z = Vector3.Cross(x, new(matrix.M21, matrix.M22, matrix.M23));
                    if (x.LengthSquared() < 1e-8f || z.LengthSquared() < 1e-8f) return null;
                    x = Vector3.Normalize(x);
                    z = Vector3.Normalize(z);
                    var y = Vector3.Cross(z, x);
                    rotation = Quaternion.CreateFromRotationMatrix(new(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1));
                    position = matrix.Translation;
                }
                if (!float.IsFinite(position.LengthSquared()) || !float.IsFinite(rotation.LengthSquared())) return null;
                result[bone] = new(position, Quaternion.Normalize(rotation));
            }
            return result;
        }
    }
}

namespace Meitou.Brutality.Physics
{
    internal sealed partial class AdvancedFragmentScene
    {
        internal static bool MatchesStockDefinition(RagdollDefinition definition, SavedFragmentPiece saved) =>
            saved.Definition == DefinitionKeyOf(definition);

        internal static void FillStockPalette(Piece piece, SkeletonRig rig, Matrix4x4[] initialSkin,
            Matrix4x4 world, Matrix4x4 inverseWorld, Matrix4x4[] output, Matrix4x4[] motion)
        {
            EnsureAnchors(piece, rig);
            for (int body = 0; body < piece.Pose.Length; body++)
                motion[body] = world * Matrix(piece.InitialPose[body].Inverse()) * Matrix(piece.Pose[body]) * inverseWorld;
            for (int bone = 0; bone < rig.BoneCount; bone++)
                output[bone] = initialSkin[bone] * motion[piece.Anchors![bone]];

            static Matrix4x4 Matrix(RigidTransform at) => Matrix4x4.CreateFromQuaternion(at.Rotation) * Matrix4x4.CreateTranslation(at.Position);
        }
    }
}
