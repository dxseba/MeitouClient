using System.Numerics;
using System.Runtime.InteropServices;
using Meitou.Data;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Ogre;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Ragdolls;
using static Meitou.Tests.Bodies.BodyFixtures;
using static Meitou.Tests.Physics.RagdollCutTests;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Physics;

public class LimbSeveranceGeometryTests
{
    [Theory]
    [InlineData(0, .08f)]
    [InlineData(0, .92f)]
    [InlineData(1, .3f)]
    [InlineData(1, .7f)]
    [InlineData(2, .3f)]
    [InlineData(2, .7f)]
    [InlineData(3, .08f)]
    [InlineData(3, .92f)]
    public void A_saved_fraction_has_one_shared_oblique_plane_at_each_races_scaled_limb(int limb, float fraction)
    {
        var (rig, _) = HumanRig();
        var cut = new LimbCutState(limb, fraction, new(.6f, -.25f));
        Assert.True(LimbCutGeometry.TryPlane(rig, cut, out var plane));
        Assert.True(LimbCutGeometry.TryPlane(rig, cut, out var scaled, 2.5f));
        int first = 1 + limb * 3;
        var centre = Vector3.Lerp(rig.BindPosition[first], rig.BindPosition[first + 2], fraction);
        Assert.InRange(MathF.Abs(Distance(plane, centre)), 0, .00001f);
        Assert.InRange(MathF.Abs(Distance(scaled, centre * 2.5f)), 0, .00001f);
        Assert.InRange(new Vector3(plane.X, plane.Y, plane.Z).Length(), .99999f, 1.00001f);
        Assert.True(Distance(plane, rig.BindPosition[first]) > 0);
        Assert.True(Distance(plane, rig.BindPosition[first + 2]) < 0);
        Assert.True(MathF.Abs(plane.X) > .1f && MathF.Abs(plane.Z) > .1f);
        Assert.Equal(plane.X, scaled.X);
        Assert.Equal(plane.Y, scaled.Y);
        Assert.Equal(plane.Z, scaled.Z);
    }

    [Fact]
    public void A_bent_chain_and_a_Z_aligned_chain_use_the_local_segment_instead_of_world_height()
    {
        foreach (var lower in new[] { new Vector3(3, 0, 0), new Vector3(0, 0, 3) })
        {
            var skeleton = new OgreSkeleton();
            skeleton.Bones.Add(new() { Handle = 0, Name = "Bip01 L UpperArm", Position = new(4, 9, 2) });
            skeleton.Bones.Add(new() { Handle = 1, Parent = 0, Name = "Bip01 L Forearm", Position = new(0, -3, 0) });
            skeleton.Bones.Add(new() { Handle = 2, Parent = 1, Name = "Bip01 L Hand", Position = lower });
            var rig = new SkeletonRig(skeleton);
            var cut = new LimbCutState(0, .75f, new(.4f, .3f));
            Assert.True(LimbCutGeometry.TryPlane(rig, cut, out var plane));
            var centre = rig.BindPosition[1] + lower * .5f;
            Assert.InRange(MathF.Abs(Distance(plane, centre)), 0, .00001f);
            Assert.True(Distance(plane, rig.BindPosition[1]) > 0);
            Assert.True(Distance(plane, rig.BindPosition[2]) < 0);
            Assert.True(float.IsFinite(plane.LengthSquared()));
        }
    }

    [Fact]
    public void Invalid_or_missing_bind_chains_never_publish_a_partial_or_nonfinite_plane()
    {
        var (rig, _) = HumanRig();
        foreach (var cut in new[]
        {
            new LimbCutState(-1, .5f, default), new(4, .5f, default), new(0, .079f, default), new(0, .921f, default),
            new(0, float.NaN, default), new(0, .5f, new(float.PositiveInfinity, 0)), new(0, .5f, new(1, 1)),
        })
        {
            Assert.False(cut.IsValid);
            Assert.False(LimbCutGeometry.TryPlane(rig, cut, out var plane));
            Assert.Equal(Vector4.Zero, plane);
        }
        var valid = new LimbCutState(0, .5f, default);
        foreach (float scale in new[] { 0, -1, float.NaN, float.PositiveInfinity })
            Assert.False(LimbCutGeometry.TryPlane(rig, valid, out _, scale));
        Assert.False(LimbCutGeometry.TryPlane(new(new OgreSkeleton()), valid, out _));
        var skeleton = new OgreSkeleton();
        foreach (var (name, handle) in new[] { ("UpperArm", 0), ("Forearm", 1), ("Hand", 2) })
            skeleton.Bones.Add(new() { Name = "Bip01 L " + name, Handle = (ushort)handle });
        Assert.False(LimbCutGeometry.TryPlane(new(skeleton), valid, out _));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void Both_oblique_collision_halves_share_the_skin_plane_and_leave_other_chains_untouched(int limb, bool robot)
    {
        var (rig, original) = HumanRig();
        var source = robot ? RobotRagdollWeight.Weight(original) : original;
        var cut = new LimbCutState(limb, .5f, new(.55f, -.35f));
        Assert.True(LimbCutGeometry.TryPlane(rig, cut, out var plane));
        var (remaining, detached) = RagdollSeverance.Split(source, limb, plane);
        Assert.NotNull(remaining);
        Assert.NotNull(detached);
        int middle = 2 + limb * 3, distal = middle + 1;
        var upper = Assert.Single(remaining.Bodies, body => body.Bone == middle);
        var lower = Assert.Single(detached.Bodies, body => body.Bone == middle);
        Assert.NotNull(upper.Hull);
        Assert.NotNull(lower.Hull);
        Assert.All(upper.Hull, point => Assert.True(Distance(plane, upper.Bind.Apply(point)) >= -.0001f));
        Assert.All(lower.Hull, point => Assert.True(Distance(plane, lower.Bind.Apply(point)) <= .0001f));
        Assert.Contains(upper.Hull, point => MathF.Abs(Distance(plane, upper.Bind.Apply(point))) < .0001f);
        Assert.Contains(lower.Hull, point => MathF.Abs(Distance(plane, lower.Bind.Apply(point))) < .0001f);
        Assert.DoesNotContain(remaining.Bodies, body => body.Bone == distal);
        Assert.Contains(detached.Bodies, body => body.Bone == distal);
        Assert.All(detached.Bodies, body => Assert.InRange(body.Bone, middle, distal));
        foreach (var body in source.Bodies.Where(body => body.Bone < middle - 1 || body.Bone > distal))
            Assert.Same(body, Assert.Single(remaining.Bodies, kept => kept.Bone == body.Bone));
        Assert.Single(detached.Joints);
        RagdollInstalledRaceTests.AssertInertia(upper, "oblique stump");
        RagdollInstalledRaceTests.AssertInertia(lower, "oblique detached limb");
        Assert.All(original.Bodies, body => Assert.Null(body.Hull));
    }

    [Fact]
    public void A_carry_contact_stays_on_retained_oblique_skin_after_the_fragment_moves()
    {
        var (rig, definition) = HumanRig();
        var cut = new LimbCutState(0, .5f, new(.8f, 0));
        Assert.True(LimbCutGeometry.TryPlane(rig, cut, out var plane));
        Vector3[] positions = [new(2, 9, 0), new(4, 9, 0), new(4, 12, 0), new(2, 9, 0), new(4, 12, 0), new(2, 12, 0)];
        var data = new OgreVertexData { VertexCount = (uint)positions.Length };
        data.Elements.Add(new(0, OgreVertexElementType.Float3, OgreVertexSemantic.Position, 0, 0));
        data.Buffers[0] = new(12, MemoryMarshal.AsBytes(positions.AsSpan()).ToArray());
        var sub = new OgreSubMesh { VertexData = data, Indices = new(false, Enumerable.Range(0, positions.Length).Select(i => (uint)i).ToArray()) };
        for (uint vertex = 0; vertex < positions.Length; vertex++) sub.BoneAssignments.Add(new(vertex, 2, 1));
        var mesh = new OgreMesh();
        mesh.SubMeshes.Add(sub);
        var surface = RagdollSkinSurface.Read(mesh);
        var database = new GameDatabase();
        var file = new FcsFile();
        file.Records.Add(new() { StringId = "grip-race", RecordType = FcsRecordType.RACE, Flags = 0x10 });
        database.Apply(file, "limb-tests.mod");
        var look = new CharacterAppearance { Race = database.Find("grip-race")! };
        var probe = new Vector3(4, 10.2f, 0);
        foreach (var transform in new[] { Matrix4x4.Identity, Matrix4x4.CreateRotationY(.7f) * Matrix4x4.CreateTranslation(10, 4, -6) })
        {
            var skin = Enumerable.Repeat(transform, rig.BoneCount).ToArray();
            var grip = Assert.IsType<RagdollSkinSurface.Grip>(surface.FindGrip(look, rig, default, 1, true,
                new int[rig.BoneCount], 0, definition, skin, Vector3.Transform(probe, transform), [10, -1, -1, -1],
                query: new(NearestToPoint: true, Uncut: true), limbSeverances: [cut]));
            Assert.True(Matrix4x4.Invert(transform, out var inverse));
            var point = Vector3.Transform(grip.Posed(skin), inverse);
            Assert.True(Distance(plane, point) >= -.0001f);
            Assert.True(Vector3.Distance(point, probe) > .25f, "The old height-only contact would grab the removed side of the slanted cut");
        }
    }

    [Theory]
    [InlineData(.628f)]
    [InlineData(-.628f)]
    public void Overlapping_limb_actors_keep_their_original_joint_in_each_half_when_its_old_anchor_was_cut_away(float slant)
    {
        var (rig, source) = OverlappingArm();
        var cut = new LimbCutState(0, .506f, new(slant, -.293f));
        Assert.True(LimbCutGeometry.TryPlane(rig, cut, out var plane));
        var (remaining, detached) = RagdollSeverance.Split(source, 0, plane);
        Assert.NotNull(remaining);
        Assert.NotNull(detached);
        foreach (var (definition, side) in new[] { (remaining, 1), (detached, -1) })
        {
            var elbow = Assert.Single(definition.Joints, joint => definition.Bodies[joint.A].Name.Contains("UpperArm", StringComparison.Ordinal)
                && definition.Bodies[joint.B].Name.Contains("Forearm", StringComparison.Ordinal));
            var a = definition.Bodies[elbow.A].Bind.Apply(elbow.OffsetA);
            var b = definition.Bodies[elbow.B].Bind.Apply(elbow.OffsetB);
            Assert.InRange(Vector3.Distance(a, b), 0, .0001f);
            Assert.True(Distance(plane, a) * side >= -.0001f);
            Assert.True(Distance(plane, b) * side >= -.0001f);
            Assert.All(definition.Bodies, body => Assert.True(float.IsFinite(body.Bind.Position.LengthSquared())));
        }
        Assert.All(detached.Bodies, body => Assert.NotEqual("Bip01", body.Name));
        Assert.Same(source.Bodies[0], remaining.Bodies[0]);
        var oldElbow = source.Bodies[1].Bind.Apply(source.Joints[1].OffsetA);
        Assert.True(Distance(plane, oldElbow) > .5f, "The old elbow centre lies outside the negative half despite both clipped actors surviving");
    }

    [Theory]
    [InlineData(.628f)]
    [InlineData(-.628f)]
    public void A_cut_through_overlapping_elbow_actors_keeps_both_skin_edges_bounded_during_independent_motion(float slant)
    {
        var (rig, definition) = OverlappingArm();
        var setup = new CharacterPoseSetup { Rig = rig };
        var database = new GameDatabase();
        var file = new FcsFile();
        file.Records.Add(new() { StringId = "overlap-race", RecordType = FcsRecordType.RACE, Flags = 0x10 });
        database.Apply(file, "limb-tests.mod");
        var cold = new CharacterCold { Race = Human, Medical = Healthy(), Appearance = new() { Race = database.Find("overlap-race")! },
            LimbSeverances = [new(0, .506f, new(slant, -.293f))], Blow = (new(35, 7, 0), new(35, 7, 0)) };
        cold.Medical.Sever(LArm, Context());
        cold.Medical.KnockOut(10000);
        using var physics = new RagdollSystem(new(database, _ => null, _ => null), new Flat(), BodyOptions.Default with { AdvancedRagdolls = true },
            _ => setup, (_, _, _) => definition);
        using var world = new SimWorld(new WorldSettings(), new OpenGroundWalkability((_, _) => 0), [physics]);
        var id = world.Characters.Spawn(new() { Position = new(20, 25, 20) }, cold, 0);
        var skin = new Matrix4x4[rig.BoneCount];
        var scratch = rig.Rent();
        try
        {
            for (int frame = 0; frame < 120; frame++)
            {
                world.RunTick();
                var shown = Assert.Single(world.Snapshot.Characters, character => character.Id == id);
                var detached = Assert.Single(shown.DetachedRagdolls, piece => piece.Part == 0);
                foreach (var pose in new[] { Assert.IsType<BonePose>(shown.Ragdoll), detached.Bones })
                {
                    Assert.NotNull(pose.Components);
                    Assert.Equal(pose.Components[1], pose.Components[2]);
                    scratch.Pose(null, [], skin, pose);
                    var a = Vector3.Transform(new(3.4f, 9.952f, 0), skin[1]);
                    var b = Vector3.Transform(new(2.6f, 9.952f, 0), skin[2]);
                    Assert.True(float.IsFinite(a.LengthSquared()) && float.IsFinite(b.LengthSquared()));
                    Assert.InRange(Vector3.Distance(a, b), 0, 7);
                }
            }
        }
        finally { rig.Return(scratch); }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void A_pair_of_fresh_oblique_limb_losses_produces_two_independent_finite_articulated_pieces(int firstLimb, bool robot)
    {
        var (rig, definition) = HumanRig();
        var setup = new CharacterPoseSetup { Rig = rig };
        var database = new GameDatabase();
        var file = new FcsFile();
        file.Records.Add(new() { StringId = "limb-race", RecordType = FcsRecordType.RACE, Flags = 0x10 });
        file.Records[0].Bools["is robot"] = robot;
        database.Apply(file, "limb-tests.mod");
        var race = new RaceData { IsRobot = robot, Anatomy = Human.Anatomy };
        var cold = new CharacterCold
        {
            Race = race, Medical = Healthy(race), Appearance = new() { Race = database.Find("limb-race")! },
            LimbSeverances = [new(firstLimb, .4f, new(.4f, -.3f)), new(firstLimb + 1, .65f, new(-.5f, .25f))],
        };
        cold.Medical.KillFromBisection();
        foreach (int limb in new[] { firstLimb, firstLimb + 1 })
            cold.Medical.Sever(cold.Medical.Parts.ToList().FindIndex(part => part.Template.LimbSlot == limb), Context());
        using var physics = new RagdollSystem(new(database, _ => null, _ => null), new Flat(), BodyOptions.Default with { AdvancedRagdolls = true },
            _ => setup, (_, _, _) => definition);
        using var world = new SimWorld(new WorldSettings { Seed = 13 }, new OpenGroundWalkability((_, _) => 0), [physics]);
        var id = world.Characters.Spawn(new() { Position = new(20, 25, 20) }, cold, 0);
        world.RunTick();
        var before = Assert.Single(world.Snapshot.Characters, character => character.Id == id).DetachedRagdolls;
        Assert.Equal(new[] { firstLimb, firstLimb + 1 }, before.Select(piece => piece.Part).Order());
        var firstPose = (Vector3[])before[0].Bones.Position.Clone();
        world.RunTicks(240);
        var after = Assert.Single(world.Snapshot.Characters, character => character.Id == id).DetachedRagdolls;
        Assert.Equal(2, after.Count);
        Assert.All(after, piece =>
        {
            Assert.True(piece.Bones.AbsolutePositions);
            Assert.All(piece.Bones.Position, point => Assert.True(float.IsFinite(point.LengthSquared())));
            Assert.NotNull(piece.Bones.Components);
        });
        Assert.Equal(firstPose, before[0].Bones.Position);
        var saved = Assert.IsType<Meitou.Simulation.Saving.SavedRagdoll>(physics.SaveState(id));
        Assert.All(saved.Pose, pose => Assert.True(float.IsFinite(pose.Position.LengthSquared())));
    }

    internal static (SkeletonRig Rig, RagdollDefinition Definition) HumanRig()
    {
        var skeleton = new OgreSkeleton();
        skeleton.Bones.Add(new() { Handle = 0, Name = "Bip01", Position = new(0, 11, 0) });
        var bodies = new List<RagdollBody> { Box("Bip01", 0, new(0, 11, 0), new(3, 5, 2)) };
        var joints = new List<(int, int, Vector3)>();
        for (int limb = 0; limb < 4; limb++)
        {
            string side = limb % 2 == 0 ? "L" : "R";
            string[] chain = limb < 2 ? ["UpperArm", "Forearm", "Hand"] : ["Thigh", "Calf", "Foot"];
            int first = bodies.Count;
            var top = new Vector3((limb % 2 == 0 ? 1 : -1) * (limb < 2 ? 3 : 1), limb < 2 ? 14 : 8, 0);
            for (int segment = 0; segment < 3; segment++)
            {
                int bone = bodies.Count;
                var at = top - Vector3.UnitY * segment * 4;
                string name = $"Bip01 {side} {chain[segment]}";
                skeleton.Bones.Add(new()
                {
                    Handle = (ushort)bone, Parent = (ushort)(segment == 0 ? 0 : bone - 1), Name = name,
                    Position = segment == 0 ? at - new Vector3(0, 11, 0) : -Vector3.UnitY * 4,
                });
                bodies.Add(Box(name, bone, at, new(1, 4, 1)));
                joints.Add((segment == 0 ? 0 : bone - 1, bone, segment == 0 ? top : at + Vector3.UnitY * 2));
            }
            Assert.Equal(first, 1 + limb * 3);
        }
        return (new(skeleton), Definition([.. bodies], [.. joints]));
    }

    static (SkeletonRig Rig, RagdollDefinition Definition) OverlappingArm()
    {
        var skeleton = new OgreSkeleton();
        skeleton.Bones.Add(new() { Name = "Bip01", Handle = 0, Position = new(0, 11, 0) });
        skeleton.Bones.Add(new() { Name = "Bip01 L UpperArm", Handle = 1, Parent = 0, Position = new(3, 3, 0) });
        skeleton.Bones.Add(new() { Name = "Bip01 L Forearm", Handle = 2, Parent = 1, Position = new(0, -4, 0) });
        skeleton.Bones.Add(new() { Name = "Bip01 L Hand", Handle = 3, Parent = 2, Position = new(0, -4, 0) });
        return (new(skeleton), Definition([Box("Bip01", 0, new(0, 11, 0), new(3, 5, 2)),
            Box("Bip01 L UpperArm", 1, new(3, 14, 0), new(2, 10, 2)),
            Box("Bip01 L Forearm", 2, new(3, 10, 0), new(2, 8, 2)),
            Box("Bip01 L Hand", 3, new(3, 6, 0), new(2, 4, 2))],
            [(0, 1, new(3, 14, 0)), (1, 2, new(3, 12, 0)), (2, 3, new(3, 8, 0))]));
    }

    sealed class Flat : IRagdollEnvironment
    {
        public float GroundHeight(float x, float z, float nearY) => 0;
        public bool Buildings(Vector3 min, Vector3 max, List<Vector3> triangles) => true;
    }

    static float Distance(Vector4 plane, Vector3 point) => Vector4.Dot(plane, new(point, 1));
}
