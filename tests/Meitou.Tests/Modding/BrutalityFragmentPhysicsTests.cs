using System.Numerics;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using static Meitou.Tests.Bodies.BodyFixtures;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuUtilities.Memory;
using Meitou.Brutality;
using Meitou.Brutality.Physics;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;
using Meitou.Data.Physics;
using IRagdollEnvironment = Meitou.Simulation.Ragdolls.IRagdollEnvironment;

namespace Meitou.Tests.Modding;

public sealed class BrutalityFragmentPhysicsTests
{
    sealed class Flat : IRagdollEnvironment
    {
        public float GroundHeight(float x, float z, float nearY) => 0;
        public bool Buildings(Vector3 min, Vector3 max, List<Vector3> triangles) => true;
    }
    static RagdollBody BoxBody(string name, int bone, Vector3 at, Vector3 size) => new()
    {
        Name = name, Bone = bone, Type = PhysicsShapeType.Box, Size = size, Mass = 10,
        Bind = new(at, Quaternion.Identity), BoneInBody = RigidTransform.Identity,
    };
    static RagdollDefinition Definition(RagdollBody[] bodies, (int A, int B, Vector3 Anchor)[]? anchors = null, int bones = 0)
    {
        var joints = (anchors ?? []).Select(j => new RagdollJoint
        {
            A = j.A, B = j.B, Type = PhysicsJointType.Spherical,
            OffsetA = bodies[j.A].Bind.Inverse().Apply(j.Anchor), OffsetB = bodies[j.B].Bind.Inverse().Apply(j.Anchor),
            BasisA = Quaternion.Identity, BasisB = Quaternion.Identity, Limits = true, Low = -1, High = 1, Swing = 1,
        }).ToArray();
        var joined = new ulong[bodies.Length];
        foreach (var joint in joints) { joined[joint.A] |= 1UL << joint.B; joined[joint.B] |= 1UL << joint.A; }
        var map = Enumerable.Repeat(-1, Math.Max(bones, bodies.Max(body => body.Bone) + 1)).ToArray();
        for (int body = 0; body < bodies.Length; body++) map[bodies[body].Bone] = body;
        return new() { Bodies = bodies, Joints = joints, BodyOfBone = map, Joined = joined, Root = 0 };
    }
    static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
    static float Distance(Vector4 plane, Vector3 p) => Vector4.Dot(plane, new(p, 1));
    static Vector3[] Corners(Vector3 size) => [.. from x in new[] { -1, 1 } from y in new[] { -1, 1 } from z in new[] { -1, 1 }
        select new Vector3(x * size.X / 2, y * size.Y / 2, z * size.Z / 2)];
    static void FinitePose(AdvancedFragmentScene.Piece piece)
    {
        foreach (var pose in piece.Pose)
        {
            Assert.True(Finite(pose.Position));
            Assert.InRange(pose.Position.LengthSquared(), 0, 1_000_000);
            Assert.InRange(pose.Rotation.LengthSquared(), .99f, 1.01f);
        }
    }

    [Fact]
    public void Twenty_simultaneous_varied_full_body_cuts_stay_finite_and_settle_in_one_shared_scene()
    {
        var source = Definition([
            BoxBody("Bip01 Spine1", 0, new(0, 12, 0), new(4, 6, 2)),
            BoxBody("Bip01 Head", 1, new(0, 17, 0), new(2, 3, 2)),
            BoxBody("Bip01 L UpperArm", 2, new(-3, 12, 0), new(1, 6, 1)),
            BoxBody("Bip01 R UpperArm", 3, new(3, 12, 0), new(1, 6, 1)),
            BoxBody("Bip01 L Thigh", 4, new(-1, 5, 0), new(1.4f, 8, 1.4f)),
            BoxBody("Bip01 R Thigh", 5, new(1, 5, 0), new(1.4f, 8, 1.4f)),
        ], [(0, 1, new(0, 15, 0)), (0, 2, new(-2, 14, 0)), (0, 3, new(2, 14, 0)), (0, 4, new(-1, 9, 0)), (0, 5, new(1, 9, 0))]);
        using var scene = new AdvancedFragmentScene();
        var pieces = new List<AdvancedFragmentScene.Piece>();
        for (int character = 0; character < 20; character++)
        {
            float turn = character * MathF.Tau / 20;
            var normal = Vector3.Normalize(character % 3 == 0 ? new(MathF.Cos(turn), .12f, MathF.Sin(turn))
                : new Vector3(MathF.Cos(turn) * .4f, 1, MathF.Sin(turn) * .25f));
            var plane = new Vector4(normal, -Vector3.Dot(normal, new(0, 12.3f, 0)));
            var offset = new Vector3((character % 5 - 2) * 3, 10, (character / 5 - 1.5f) * 3);
            foreach (int side in new[] { -1, 1 })
            {
                var half = Assert.IsType<RagdollDefinition>(RagdollCut.Build(source, plane, side, fullBody: true));
                pieces.Add(scene.Create(half, bone => source.Bodies[source.BodyOfBone[bone]].Bind with
                { Position = source.Bodies[source.BodyOfBone[bone]].Bind.Position + offset }, new(10 * side, 4, 0), new(10 * side, 4, 0), (ulong)character));
            }
        }
        Assert.Equal(40, scene.Count);
        Assert.InRange(scene.AwakeBodies, 1, 384);
        for (int tick = 0; tick < 240; tick++)
        {
            scene.Step(1f / 30, new Flat(), tick);
            foreach (var piece in pieces)
            {
                FinitePose(piece);
                // Pinned RagdollSystem.Surface tolerates a body centre down to floor - SinkDepth (1 unit),
                // rather than requiring every clipped collider's centre to remain above the visible ground.
                // Check every step so a runaway fall cannot be hidden by the eventual freeze.
                Assert.All(piece.Pose, pose => Assert.True(pose.Position.Y >= -1,
                    $"tick {tick}, piece {piece.Number}: body centre escaped the surface tolerance: {pose.Position.Y}"));
            }
        }
        Assert.All(pieces, piece => Assert.True(piece.Frozen));
        Assert.Equal(0, scene.AwakeBodies);
        Assert.All(pieces, piece => Assert.All(piece.Pose, pose => Assert.InRange(pose.Position.Y, -1, 40)));
    }

    [Theory]
    [InlineData(.00001f)]
    [InlineData(.01f)]
    [InlineData(1f)]
    [InlineData(1000f)]
    public void Checked_hulls_accept_duplicate_near_coplanar_points_at_very_different_scales(float scale)
    {
        var raw = Corners(new(2, 5, .4f));
        var points = raw.Concat([raw[0], raw[3], Vector3.Zero, raw[1] + new Vector3(0, .0000001f, 0)])
            .Select(p => (p + new Vector3(17, -11, 5)) * scale).ToArray();
        Assert.True(RagdollHull.TryTriangles(points, out var triangles));
        Assert.InRange(triangles.Length, 12, (2 * points.Length - 4) * 3);
        Assert.All(triangles, index => Assert.InRange(index, 0, points.Length - 1));
        var pool = new BufferPool();
        try
        {
            Assert.True(RagdollHull.TryCreate(points, pool, out var hull, out var centre));
            try
            {
                Assert.True(Finite(centre));
                var inertia = hull.ComputeInertia(10);
                Assert.True(float.IsFinite(inertia.InverseMass) && inertia.InverseMass > 0);
                Assert.True(float.IsFinite(inertia.InverseInertiaTensor.XX) && inertia.InverseInertiaTensor.XX > 0);
            }
            finally { hull.Dispose(pool); }
        }
        finally { pool.Clear(); }
    }

    [Fact]
    public void Fog_prince_dump_plane_keeps_valid_bone_frames_and_half_spaces_for_thin_rotated_hulls()
    {
        var body = new RagdollBody
        {
            Name = "Bip01 Spine1", Bone = 0, Type = PhysicsShapeType.Mesh, Mass = 3,
            Hull = Corners(new(3, 6, .35f)).Concat([new Vector3(0, 3, 0), new(-1.5f, 0, .175f)]).ToArray(),
            Bind = new(new(.2f, 13, -.1f), Quaternion.CreateFromYawPitchRoll(.2f, -.15f, .1f)),
            BoneInBody = new(new(.1f, -.2f, .05f), Quaternion.CreateFromAxisAngle(Vector3.UnitX, .1f)),
        };
        var source = Definition([body]);
        var plane = new Vector4(-.2391731888f, .9625774026f, .1274404079f, -12.64869976f);
        var oldBone = body.BoneInBody.Then(body.Bind);
        using var scene = new AdvancedFragmentScene();
        foreach (int side in new[] { -1, 1 })
        {
            var cut = Assert.IsType<RagdollDefinition>(RagdollCut.Build(source, plane, side, fullBody: true));
            var retained = Assert.Single(cut.Bodies);
            var newBone = retained.BoneInBody.Then(retained.Bind);
            Assert.InRange(Vector3.Distance(oldBone.Position, newBone.Position), 0, .0001f);
            Assert.InRange(1 - MathF.Abs(Quaternion.Dot(oldBone.Rotation, newBone.Rotation)), 0, .0001f);
            Assert.All(retained.Hull!, point => Assert.True(side * Distance(plane, retained.Bind.Apply(point)) >= -.001f));
            var piece = scene.Create(cut, _ => oldBone.Then(new(new(8 * side, 10, 0), Quaternion.Identity)), Vector3.Zero, Vector3.Zero, (ulong)(side + 1));
            scene.Step(1f / 30, new Flat(), side + 1);
            FinitePose(piece);
        }
    }

    [Fact]
    public void Broad_thin_slabs_remain_physical_but_compact_microscopic_corners_are_rejected_without_source_mutation()
    {
        var original = BoxBody("Bip01 Spine1", 0, Vector3.Zero, new(4, 4, 4));
        var source = Definition([original]);
        var slab = Assert.IsType<RagdollDefinition>(RagdollCut.Build(source, new(0, 1, 0, -1.99f), 1, true));
        Assert.Single(slab.Bodies);
        var n = Vector3.Normalize(Vector3.One);
        var corner = new Vector4(n, -Vector3.Dot(n, new(2, 2, 2)) + .01f);
        Assert.Null(RagdollCut.Build(source, corner, 1, true));
        Assert.Single(Assert.IsType<RagdollDefinition>(RagdollCut.Build(source, corner, -1, true)).Bodies);
        Assert.Same(original, Assert.Single(source.Bodies));
        Assert.Null(original.Hull);
        Assert.Equal(new Vector3(4), original.Size);
    }

    [Fact]
    public void A_disconnected_arm_falls_and_its_non_physical_hand_follows_the_published_forearm()
    {
        var skeleton = new OgreSkeleton();
        skeleton.Bones.Add(new() { Handle = 0, Name = "Bip01", Position = new(0, 11, 0) });
        skeleton.Bones.Add(new() { Handle = 1, Parent = 0, Name = "Bip01 L UpperArm", Position = new(3, -1.5f, 0) });
        skeleton.Bones.Add(new() { Handle = 2, Parent = 1, Name = "Bip01 L Forearm", Position = new(0, -3, 0) });
        skeleton.Bones.Add(new() { Handle = 3, Parent = 2, Name = "Bip01 L Hand", Position = new(0, -2, 0) });
        var rig = new SkeletonRig(skeleton);
        var source = Definition([
            BoxBody("Bip01", 0, new(0, 11, 0), new(3, 5, 2)),
            BoxBody("Bip01 L UpperArm", 1, new(3, 9.5f, 0), new(1, 3, 1)),
            BoxBody("Bip01 L Forearm", 2, new(3, 6.5f, 0), new(1, 3, 1)),
        ], [(0, 1, new(2, 11, 0)), (1, 2, new(3, 8, 0))], bones: 4);
        var cut = Assert.IsType<RagdollDefinition>(RagdollCut.Build(source, new(0, 1, 0, -10), -1, true));
        var elbow = Assert.Single(cut.Joints);
        Assert.Equal("Bip01 L UpperArm", cut.Bodies[elbow.A].Name);
        Assert.Equal("Bip01 L Forearm", cut.Bodies[elbow.B].Name);
        Assert.Equal(0UL, cut.Joined[cut.BodyOfBone[0]]);
        var placement = Matrix4x4.CreateTranslation(20, 15, 20);
        using var scene = new AdvancedFragmentScene();
        var piece = scene.Create(cut, bone => new(rig.BindPosition[bone] + new Vector3(20, 15, 20), Quaternion.Identity), Vector3.Zero, Vector3.Zero, 9);
        float before = piece.Pose[cut.BodyOfBone[2]].Position.Y;
        for (int tick = 0; tick < 240; tick++) scene.Step(1f / 30, new Flat(), tick);
        var pose = scene.Publish(piece, rig, placement);
        Assert.True(pose.AbsolutePositions);
        var components = Assert.IsType<int[]>(pose.Components);
        Assert.Equal(rig.BoneCount, components.Length);
        Assert.NotEqual(components[0], components[2]);
        Assert.Equal(components[1], components[2]);
        Assert.Equal(components[2], components[3]);
        var topology = components.ToArray();
        var saved = scene.Capture(piece, 1, "detached-chain", rig);
        var replacement = scene.Create(cut, bone => new(rig.BindPosition[bone] + new Vector3(20, 15, 20), Quaternion.Identity), Vector3.Zero, Vector3.Zero, 10);
        Assert.True(scene.Restore(replacement, saved));
        var restored = scene.Publish(replacement, rig, placement);
        Assert.Equal(topology, restored.Components);
        Assert.NotSame(components, restored.Components);
        Assert.Equal(topology, components); // An older published snapshot stays immutable after a save roundtrip.
        var carried = AdvancedFragmentScene.CarryPose(replacement, rig, replacement.Pose);
        Assert.Equal(topology, carried.Components);
        // Re-cut an existing settled piece: the nonphysical hand must keep following its actual forearm,
        // not the upright animation whose physical actor transforms have merely been overwritten afterwards.
        var posed = FragmentPhysicsSystem.PoseWorld(new() { Rig = rig }, [], pose, placement);
        var second = scene.Create(cut, bone => posed[bone], Vector3.Zero, Vector3.Zero, 11, posed);
        var secondPose = scene.Publish(second, rig, placement);
        Assert.InRange(Vector3.Distance(Vector3.Transform(secondPose.Position[2], placement), Vector3.Transform(secondPose.Position[3], placement)), 1.99f, 2.01f);
        Assert.InRange(Vector3.Distance(secondPose.Position[3], pose.Position[3]), 0, .0001f);
        Assert.Equal(topology, secondPose.Components);
        var scratch = rig.Rent();
        try
        {
            scratch.PoseDerived(null, [], pose);
            var forearm = Vector3.Transform(scratch.DerivedPosition[2], placement);
            var hand = Vector3.Transform(scratch.DerivedPosition[3], placement);
            Assert.True(before > 15);
            Assert.InRange(forearm.Y, .1f, 3.5f);
            Assert.InRange(hand.Y, -.5f, 4.5f);
            Assert.InRange(Vector3.Distance(forearm, hand), 1.99f, 2.01f);
        }
        finally { rig.Return(scratch); }
        Assert.Equal(2, source.Joints.Length);
    }

    [Theory]
    [InlineData(HeadCutMode.AttachedSplit)]
    [InlineData(HeadCutMode.DetachedSplit)]
    public void Head_halves_keep_original_bone_handles_and_only_the_attached_mode_keeps_a_neck_joint(HeadCutMode mode)
    {
        var source = Definition([
            BoxBody("Bip01 Neck", 0, new(0, 14, 0), new(1, 2, 1)),
            BoxBody("Bip01 Head", 1, new(0, 17, 0), new(2, 3, 2)),
            BoxBody("Bip01 Jaw", 2, new(0, 16, .3f), new(1.5f, 1, 1.5f)),
        ], [(0, 1, new(0, 15, 0)), (1, 2, new(0, 16, .3f))]);
        var split = RagdollSeverance.SplitHead(source, new(1, 0, 0, 0), mode);
        var remaining = Assert.IsType<RagdollDefinition>(split.Remaining);
        var positive = Assert.IsType<RagdollDefinition>(split.Positive);
        Assert.DoesNotContain(positive.Bodies, body => body.Bone == 0);
        Assert.Equal(mode == HeadCutMode.AttachedSplit, remaining.Bodies.Any(body => body.Bone == 1));
        Assert.Equal(mode == HeadCutMode.DetachedSplit, split.Negative is not null);
        Assert.All(positive.Bodies, body => Assert.Equal(source.Bodies[body.Bone].Name, body.Name));
        if (mode == HeadCutMode.AttachedSplit) Assert.Contains(remaining.Joints, joint => remaining.Bodies[joint.A].Bone == 0 && remaining.Bodies[joint.B].Bone == 1);
        else Assert.Empty(remaining.Joints);
        Assert.Equal(2, source.Joints.Length);
    }

    [Fact]
    public void Robot_mass_reduces_received_collision_speed_and_only_the_added_launch_push_is_attenuated()
    {
        static float StruckSpeed(bool robot)
        {
            var source = Definition([BoxBody("Bip01", 0, Vector3.Zero, new(2))]);
            var mass = (robot ? RobotRagdollWeight.Weight(source) : source).Bodies[0].Mass;
            using var physics = new RagdollPhysics();
            var shape = new Box(2, 2, 2); var index = physics.Simulation.Shapes.Add(shape);
            physics.AddBody(BodyDescription.CreateDynamic(new RigidPose(new(-2.1f, 100, 0)), new BodyVelocity(new(20, 0, 0)),
                shape.ComputeInertia(10), RagdollPhysics.Collidable(index), new BodyActivityDescription(.01f)), new BodyTag { Ragdoll = 1 });
            var struck = physics.AddBody(BodyDescription.CreateDynamic(new RigidPose(new(0, 100, 0)), shape.ComputeInertia(mass),
                RagdollPhysics.Collidable(index), new BodyActivityDescription(.01f)), new BodyTag { Ragdoll = 2 });
            for (int step = 0; step < 10; step++) physics.Step(1f / 120);
            return physics.Simulation.Bodies[struck].Velocity.Linear.X;
        }
        float organic = StruckSpeed(false), metal = StruckSpeed(true);
        Assert.InRange(organic, 8, 12);
        Assert.InRange(metal / organic, .45f, .7f);
        var movement = new Vector3(7, 0, 3); var push = new Vector3(20, 5, 0);
        var start = (Velocity: movement + push, Push: push);
        Assert.Equal(start, RobotRagdollWeight.Launch(false, start));
        var robot = RobotRagdollWeight.Launch(true, start);
        Assert.InRange(Vector3.Distance(movement + push * .35f, robot.Velocity), 0, .00001f);
        Assert.Equal(push * .35f, robot.Push);
    }
    [Theory]
    [InlineData(5f, 50f)]
    [InlineData(100f, 300f)]
    public void A_held_half_uses_the_rendered_thin_or_muscular_skin_scale_without_reapplying_missing_limbs_or_world_ground_offset(float strength, float hunger)
    {
        var skeleton = new OgreSkeleton();
        skeleton.Bones.Add(new() { Handle = 0, Name = "Bip01", Position = Vector3.Zero });
        skeleton.Bones.Add(new() { Handle = 1, Name = "Bip01 L UpperArm", Parent = 0, Position = new(-3, 15, 0) });
        for (ushort bone = 2; bone < CharacterShape.HumanBoneCount; bone++)
            skeleton.Bones.Add(new() { Handle = bone, Name = "unused" + bone, Parent = 0, Position = Vector3.Zero });
        var rig = new SkeletonRig(skeleton); var input = new CharacterShapeInput();
        var neutral = CharacterShape.Compute(input, name => rig.Handle(name) is not null);
        var setup = new CharacterPoseSetup { Rig = rig, Shape = rig.Shape(neutral.BoneSize, neutral.PositionalSize, neutral.MovementScale), GroundOffset = 7 };
        var data = new GameDatabase(); var file = new FcsFile();
        file.Records.Add(new FcsRecord { StringId = "carry-race", Name = "human", RecordType = FcsRecordType.RACE, Flags = 0x10 }); data.Apply(file, "test.mod");
        var natural = new CharacterAppearance { Race = data.Find("carry-race")!, Shape = input };
        var collapsed = natural.WithParts([]); collapsed.Shape = input with { Missing = new HashSet<Limb> { Limb.LeftArm }, HideStump = HideStump.LeftArm };
        var rigs = new CharacterRigs(data, _ => null, _ => null);
        // The synthetic pose setup replaces file loading; the actual public Reshape and its native scale maths still run.
        var cache = (ConditionalWeakTable<CharacterAppearance, Lazy<CharacterPoseSetup?>>)AccessTools.Field(typeof(CharacterRigs), "setups").GetValue(rigs)!;
        cache.Add(natural, new(() => setup));
        var stats = new CharacterStats(); stats[Meitou.Data.Gameplay.Bodies.StatsEnumerated.Strength] = strength;
        var medical = Healthy(); medical.Hunger = hunger;
        var cold = new CharacterCold { Appearance = collapsed, Race = Human, Stats = stats, Medical = medical };
        var bones = new BonePose(rig.BoneCount) { AbsolutePositions = true };
        for (int bone = 0; bone < rig.BoneCount; bone++) { bones.Set[bone] = true; bones.Position[bone] = new(100, 200, 300); bones.Rotation[bone] = Quaternion.Identity; }
        using var physics = new FragmentPhysicsSystem(new(null, BaseGame, BodyOptions.Default, default), rigs);
        var skin = physics.CarryContactSkin(cold, natural, setup, [], bones);
        var build = CharacterShape.BuildFromStats(false, stats, static (state, stat) => state[stat], hunger);
        var rendered = CharacterShape.Compute(input with { Muscle = build.Muscle, Starvation = build.Starvation }, name => rig.Handle(name) is not null);
        var scale = rendered.BoneSize["Bip01 L UpperArm"];
        var sample = rig.BindPosition[1] + new Vector3(2, 0, .5f);
        var expected = new Vector3(100 + 2 * scale.X, 200, 300 + .5f * scale.Z);
        Assert.InRange(Vector3.Distance(expected, Vector3.Transform(sample, skin[1])), 0, .0001f);
        Assert.True(scale.X > 0); Assert.NotEqual(Vector3.One, scale);
        Assert.Equal(200, Vector3.Transform(rig.BindPosition[1], skin[1]).Y, .0001f);
    }

    [Fact]
    public void A_new_cut_preserves_actor_spin_and_centre_of_mass_motion_without_reapplying_the_launch()
    {
        using var scene = new AdvancedFragmentScene();
        var definition = Definition([BoxBody("Bip01", 0, new(0, 10, 0), new(2))]);
        var piece = scene.Create(definition, _ => new(new(2, 10, 0), Quaternion.Identity), new(100, 0, 0), Vector3.Zero, 0);
        scene.InheritMotion(piece, [new(0, new(new(0, 10, 0), Quaternion.Identity), new(3, 4, 5), new(0, 0, 2))], new(0, 1, 0));
        var actual = Assert.Single(scene.CaptureMotion(piece));
        Assert.Equal(new Vector3(3, 9, 5), actual.Linear);
        Assert.Equal(new Vector3(0, 0, 2), actual.Angular);
    }

    [Fact]
    public void A_hit_applies_the_pinned_local_impulse_and_torque_only_to_its_struck_component()
    {
        Assert.Equal(new Vector3(10, 2, 0), FragmentPhysicsSystem.HitPush(Vector2.UnitX, 1));
        Assert.Equal(new Vector3(20, 4, 0), FragmentPhysicsSystem.HitPush(Vector2.UnitX, 50));
        Assert.Equal(new Vector3(50, 10, 0), FragmentPhysicsSystem.HitPush(Vector2.UnitX, 1000));
        Assert.Equal(Vector3.Zero, FragmentPhysicsSystem.HitPush(Vector2.Zero, 50));
        using var scene = new AdvancedFragmentScene();
        var definition = Definition([BoxBody("Bip01", 0, new(0, 10, 0), new(2)), BoxBody("detached hand", 1, new(10, 10, 0), new(2))]);
        var piece = scene.Create(definition, bone => definition.Bodies[bone].Bind, Vector3.Zero, Vector3.Zero, 0);
        scene.Impact(piece, 0, Vector3.UnitY, new(10, 0, 0));
        var actual = scene.CaptureMotion(piece);
        Assert.Equal(new Vector3(10, 0, 0), actual[0].Linear);
        Assert.True(actual[0].Angular.Z < 0);
        Assert.Equal(Vector3.Zero, actual[1].Linear);
        Assert.Equal(Vector3.Zero, actual[1].Angular);
    }

    [Fact]
    public void Four_settled_components_cannot_freeze_a_fifth_component_that_is_still_falling()
    {
        var definition = Definition([
            BoxBody("Bip01", 0, new(0, 1, 0), new(2)), BoxBody("settled arm", 1, new(5, 1, 0), new(2)),
            BoxBody("settled leg", 2, new(10, 1, 0), new(2)), BoxBody("settled head", 3, new(15, 1, 0), new(2)),
            BoxBody("airborne hand", 4, new(100, 1000, 0), new(2)),
        ]);
        using var scene = new AdvancedFragmentScene();
        var piece = scene.Create(definition, bone => definition.Bodies[bone].Bind, Vector3.Zero, Vector3.Zero, 0);
        for (int tick = 0; tick < 75; tick++) scene.Step(1f / 30, new Flat(), tick);
        Assert.False(piece.Frozen);
        var motion = scene.CaptureMotion(piece);
        Assert.All(motion.Take(4), body => Assert.InRange(body.Linear.LengthSquared(), 0, 2.25f));
        Assert.True(motion[4].Linear.Y < -10);
        Assert.True(piece.Pose[4].Position.Y > 100);
        // The native seven-second upper bound still releases the simulation budget for pathological fragments.
        for (int tick = 75; tick < 215; tick++) scene.Step(1f / 30, new Flat(), tick);
        Assert.True(piece.Frozen);
    }

}
