using System.Numerics;
using BepuPhysics;
using Meitou.Data.Characters;
using Meitou.Simulation.Carrying;

namespace Meitou.Brutality.Physics;

internal sealed partial class AdvancedFragmentScene
{
    internal sealed record StockCarryGrip(RigidTransform[] Pose, Vector3 LengthAxis, Vector3[][] Sections,
        Matrix4x4[]? CarrySkin, bool Lower)
    {
        internal Quaternion Drape(Quaternion orientation, Vector3 carrierForward)
        {
            // The stock clip folds the whole body at the pelvis. A single half must be laid across the arm at its own midpoint.
            if (CarrySkin is not null)
                orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.Atan2(carrierForward.X, carrierForward.Z));

            var axis = Vector3.Transform(LengthAxis, orientation);
            float facing = Vector3.Dot(axis, carrierForward);
            var along = facing < -.08f ? -carrierForward : carrierForward;
            float pitch = Lower ? .16f : -.12f;
            var draped = along * MathF.Sqrt(1 - pitch * pitch) + Vector3.UnitY * pitch;
            var tilt = Quaternion.Normalize(new Quaternion(Vector3.Cross(axis, draped), 1 + Vector3.Dot(axis, draped)));
            // Keep the knees and feet hanging down in the native carry pose.
            if (Lower && CarrySkin is not null)
            {
                float angle = 2 * MathF.Acos(Math.Clamp(tilt.W, 0, 1));
                const float maxLegTilt = MathF.PI / 3;
                if (angle > maxLegTilt)
                    tilt = Quaternion.Slerp(Quaternion.Identity, tilt, maxLegTilt / angle);
            }
            return Quaternion.Normalize(tilt * orientation);
        }

        internal Vector3 Contact(Quaternion orientation, Vector3 armNormal)
        {
            var towardArm = Vector3.Transform(-armNormal, Quaternion.Inverse(orientation));
            float reach = float.NegativeInfinity;
            var support = Sections[0];
            // Separate thigh sections can leave an empty space between them.
            foreach (var section in Sections)
                foreach (var point in section)
                {
                    float distance = Vector3.Dot(point, towardArm);
                    if (distance <= reach) continue;
                    reach = distance;
                    support = section;
                }
            var contact = Vector3.Zero;
            int count = 0;
            foreach (var point in support)
            {
                if (reach - Vector3.Dot(point, towardArm) > .005f) continue;
                contact += point;
                count++;
            }
            return contact / count;
        }
    }

    internal static StockCarryGrip BuildStockGrip(Piece piece, Matrix4x4 birthWorld, Vector3 spine, Vector3 upperLegs,
        CharacterRigs? rigs, CharacterAppearance appearance, BodyBuild? build)
    {
        var definition = piece.Definition;
        var connected = piece.StockGripBodies ??= ConnectedRoot(definition);
        var rotation = Matrix4x4.Decompose(birthWorld, out _, out var turn, out _) ? turn : Quaternion.Identity;
        var toModel = new RigidTransform(birthWorld.Translation, rotation).Inverse();
        // Move the pose the cut was made in as a unit so adjacent clothing and flesh stay coherent.
        var pose = piece.InitialPose.Select(body => body.Then(toModel)).ToArray();
        var bodies = Enumerable.Range(0, pose.Length).Where(index => connected[index]).ToArray();
        bool chest = bodies.Any(index => definition.Bodies[index].Name is "Bip01 Spine2" or "Bip01 Neck");
        bool thighs = bodies.Any(index => definition.Bodies[index].Name is "Bip01 L Thigh" or "Bip01 R Thigh");
        bool lower = !chest && thighs;
        var axis = lower ? upperLegs : spine;
        Matrix4x4[]? carrySkin = null;
        if (rigs is not null && rigs.For(appearance) is { } setup
            && setup.Rig.BoneCount == definition.BodyOfBone.Length
            && setup.Rig.Layer(rigs.Database, CarrySystem.CarriedClip, setup.Animal) is { } carried)
        {
            var rig = setup.Rig;
            var layers = new List<ActiveLayer> { new(carried, 0, 1) };
            foreach (var (layer, time) in setup.Fixed) layers.Add(new(layer, time, 1));
            var shape = build is { } current && rigs.Reshape(appearance, current) is { } shaped ? shaped.Shape : setup.Shape;
            carrySkin = new Matrix4x4[rig.BoneCount];
            var scratch = rig.Rent();
            try
            {
                scratch.Pose(shape, layers.ToArray(), carrySkin);
                foreach (int index in bodies)
                {
                    var shapeBody = definition.Bodies[index];
                    int bone = shapeBody.Bone;
                    var posedBone = new RigidTransform(scratch.DerivedPosition[bone], scratch.DerivedRotation[bone]);
                    pose[index] = shapeBody.BoneInBody.Inverse().Then(posedBone);
                }
                var pelvis = At("Bip01 Pelvis") ?? At("Bip01") ?? Vector3.Zero;
                var left = At("Bip01 L Calf");
                var right = At("Bip01 R Calf");
                var knee = left is { } l && right is { } r ? (l + r) * .5f : left ?? right;
                var chestBase = At("Bip01 Spine2") ?? At("Bip01 Spine1") ?? pelvis;
                var neck = At("Bip01 Neck") ?? At("Bip01 Head");
                if (lower && knee is { } bent)
                    axis = pelvis - bent;
                else if (neck is { } neckAt)
                    axis = neckAt - chestBase;

                Vector3? At(string name) => rig.Handle(name) is int bone ? scratch.DerivedPosition[bone] : null;
            }
            finally { rig.Return(scratch); }
        }
        axis = float.IsFinite(axis.LengthSquared()) && axis.LengthSquared() > 1e-4f ? Vector3.Normalize(axis) : Vector3.UnitY;
        var axial = bodies.Where(index => Core(definition.Bodies[index].Name, lower)).ToArray();
        if (axial.Length == 0) axial = [definition.Root];
        var hulls = axial.Select(index => RagdollCut.Points(definition.Bodies[index]).Select(pose[index].Apply).ToArray())
            .Where(points => points.Length > 0).ToArray();
        if (hulls.Length == 0) return new(pose, axis, [[pose[definition.Root].Position]], carrySkin, lower);

        // Intersect only the central body actors at the halfway station. Arms, horns and feet do not move the grip.
        var ranges = hulls.Select(hull => (Min: hull.Min(point => Vector3.Dot(point, axis)),
            Max: hull.Max(point => Vector3.Dot(point, axis)))).ToArray();
        float middle = (ranges.Min(range => range.Min) + ranges.Max(range => range.Max)) * .5f;
        // Two adjacent colliders can leave a small gap. Use the nearest actual material section in that case.
        float station = ranges.Select(range => Math.Clamp(middle, range.Min, range.Max)).MinBy(value => MathF.Abs(value - middle));
        var sections = new List<Vector3[]>();
        foreach (var hull in hulls)
        {
            var section = new HashSet<Vector3>();
            var distances = hull.Select(point => Vector3.Dot(point, axis) - station).ToArray();
            for (int a = 0; a < hull.Length; a++)
            {
                float first = distances[a];
                if (MathF.Abs(first) < 1e-5f) section.Add(hull[a]);
                for (int b = a + 1; b < hull.Length; b++)
                {
                    float second = distances[b];
                    if ((first < 0 && second > 0) || (first > 0 && second < 0))
                        section.Add(Vector3.Lerp(hull[a], hull[b], first / (first - second)));
                }
            }
            if (section.Count > 0) sections.Add([.. section]);
        }
        return new(pose, axis, sections.Count > 0 ? [.. sections] : [[pose[definition.Root].Position]], carrySkin, lower);

        static bool Core(string name, bool legs) => name is "Bip01" or "Bip01 Pelvis"
            || name.StartsWith("Bip01 Spine", StringComparison.Ordinal)
            || legs && name is "Bip01 L Thigh" or "Bip01 R Thigh";
    }

    internal void CarryStockGrip(Piece piece, StockCarryGrip grip, Vector3 support, Quaternion orientation, Vector3 armNormal, bool entering)
    {
        var connected = piece.StockGripBodies!;
        var target = piece.StockGripTargets ??= new RigidTransform[piece.Pose.Length];
        var contact = grip.Contact(orientation, armNormal);
        var placement = new RigidTransform(support - Vector3.Transform(contact, orientation), orientation);
        for (int body = 0; body < target.Length; body++)
            if (connected[body]) target[body] = grip.Pose[body].Then(placement);
        SetCarried(piece, target, connected, connected, entering);
    }

    internal static void ApplyStockCarryPalette(Piece piece, StockCarryGrip grip, Matrix4x4 inverseWorld,
        Matrix4x4[] palette, Matrix4x4[] motion)
    {
        if (grip.CarrySkin is not { } skin || piece.Anchors is not { } anchors) return;
        var connected = piece.StockGripBodies!;
        for (int body = 0; body < motion.Length; body++)
            if (connected[body])
                motion[body] = Matrix(grip.Pose[body].Inverse()) * Matrix(piece.Pose[body]) * inverseWorld;
        for (int bone = 0; bone < palette.Length; bone++)
            if (connected[anchors[bone]])
                palette[bone] = skin[bone] * motion[anchors[bone]];

        static Matrix4x4 Matrix(RigidTransform at) => Matrix4x4.CreateFromQuaternion(at.Rotation) * Matrix4x4.CreateTranslation(at.Position);
    }

    internal void MoveStockGrip(Piece piece, Vector3 from, Vector3 to, Quaternion turn)
    {
        for (int i = 0; i < piece.Pose.Length; i++)
        {
            var prior = piece.Pose[i];
            var next = new RigidTransform(to + Vector3.Transform(prior.Position - from, turn), Quaternion.Normalize(turn * prior.Rotation));
            piece.Pose[i] = next;
            if (piece.Bodies[i] is not { } handle) continue;
            var body = physics.Simulation.Bodies.GetBodyReference(handle);
            body.Pose = new RigidPose(next.Position, next.Rotation);
            body.UpdateBounds();
        }
    }
}
