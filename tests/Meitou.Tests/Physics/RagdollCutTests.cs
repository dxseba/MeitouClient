using System.Numerics;
using Meitou.Data.Physics;
using Meitou.Simulation.Ragdolls;

namespace Meitou.Tests.Physics;

public class RagdollCutTests
{
    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(0, 0, 1)]
    [InlineData(0.6f, 0.8f, 0)]
    [InlineData(-0.6f, 0.8f, 0.25f)]
    public void Cut_colliders_stay_inside_each_half_and_keep_the_bones_bind_frame(float x, float y, float z)
    {
        var sourceBody = Box("Bip01 Spine1", 0, new(0.5f, 10, -0.3f), new(4, 4, 4));
        var source = Definition([sourceBody]);
        var normal = Vector3.Normalize(new(x, y, z));
        var plane = new Vector4(normal, -Vector3.Dot(normal, sourceBody.Bind.Position) - 0.35f);
        var originalBone = sourceBody.BoneInBody.Then(sourceBody.Bind);
        foreach (int side in new[] { -1, 1 })
        {
            var cut = Assert.IsType<RagdollDefinition>(RagdollCut.Build(source, plane, side, true));
            var body = Assert.Single(cut.Bodies);
            Assert.Equal(PhysicsShapeType.Mesh, body.Type);
            Assert.NotNull(body.Hull);
            Assert.True(body.Hull.Length >= 4);
            foreach (var vertex in body.Hull)
                Assert.True(side * Distance(plane, body.Bind.Apply(vertex)) >= -0.0001f, $"side {side}: {body.Bind.Apply(vertex)} is outside {plane}");
            Assert.True(body.Mass > 0 && body.Mass < sourceBody.Mass);
            var bone = body.BoneInBody.Then(body.Bind);
            Assert.InRange(Vector3.Distance(originalBone.Position, bone.Position), 0, 0.0001f);
            Assert.InRange(1 - MathF.Abs(Quaternion.Dot(originalBone.Rotation, bone.Rotation)), 0, 0.0001f);
            Assert.Equal(0, cut.BodyOfBone[0]);
            Assert.Equal(0, cut.Root);
        }
        Assert.Same(sourceBody, Assert.Single(source.Bodies));
        Assert.Equal(new Vector3(0.5f, 10, -0.3f), sourceBody.Bind.Position);
        Assert.Null(sourceBody.Hull);
    }

    [Fact]
    public void A_cut_above_the_elbow_releases_the_arm_without_an_invisible_shoulder_tether()
    {
        var source = Definition(
        [
            Box("Bip01 Spine1", 0, new(0, 11, 0), new(3, 5, 2)),
            Box("Bip01 L UpperArm", 1, new(3, 9.5f, 0), new(1, 3, 1)),
            Box("Bip01 L Forearm", 2, new(3, 6.5f, 0), new(1, 3, 1)),
        ], [(0, 1, new Vector3(2, 11, 0)), (1, 2, new Vector3(3, 8, 0))]);
        var cut = Assert.IsType<RagdollDefinition>(RagdollCut.Build(source, new(0, 1, 0, -10), -1, true));
        Assert.Equal(3, cut.Bodies.Length);
        var elbow = Assert.Single(cut.Joints);
        Assert.Equal("Bip01 L UpperArm", cut.Bodies[elbow.A].Name);
        Assert.Equal("Bip01 L Forearm", cut.Bodies[elbow.B].Name);
        var torso = Array.FindIndex(cut.Bodies, b => b.Name == "Bip01 Spine1");
        Assert.Equal(0UL, cut.Joined[torso]);
        Assert.DoesNotContain(cut.Joints, j => j.A == torso || j.B == torso);
        Assert.InRange(Vector3.Distance(cut.Bodies[elbow.A].Bind.Apply(elbow.OffsetA), new(3, 8, 0)), 0, 0.0001f);
        Assert.InRange(Vector3.Distance(cut.Bodies[elbow.B].Bind.Apply(elbow.OffsetB), new(3, 8, 0)), 0, 0.0001f);
        Assert.Equal(2, source.Joints.Length);
    }

    [Fact]
    public void Transverse_ownership_keeps_whole_arms_and_head_above_and_whole_legs_below()
    {
        string[] upper = ["Bip01 L UpperArm", "Bip01 R Forearm", "Bip01 Neck", "Bip01 Head"];
        string[] lower = ["Bip01 L Thigh", "Bip01 R Calf"];
        // Deliberately cross the numeric plane with a limb: legacy transverse rendering owns it by body group.
        var bodies = upper.Select((name, i) => Box(name, i, new(i, 3, 0), Vector3.One))
            .Concat(lower.Select((name, i) => Box(name, upper.Length + i, new(i, 16, 0), Vector3.One))).ToArray();
        var source = Definition(bodies);
        var positive = Assert.IsType<RagdollDefinition>(RagdollCut.Build(source, new(0, 1, 0, -10), 1, false));
        var negative = Assert.IsType<RagdollDefinition>(RagdollCut.Build(source, new(0, 1, 0, -10), -1, false));
        Assert.Equal(upper.Order(), positive.Bodies.Select(b => b.Name).Order());
        Assert.Equal(lower.Order(), negative.Bodies.Select(b => b.Name).Order());
        Assert.All(positive.Bodies.Concat(negative.Bodies), body => Assert.Equal(PhysicsShapeType.Box, body.Type));
        Assert.Equal(-1, positive.BodyOfBone[upper.Length]);
        Assert.Equal(-1, negative.BodyOfBone[0]);
    }

    [Fact]
    public void Empty_tangent_and_invalid_cuts_do_not_create_degenerate_physics_bodies()
    {
        var source = Definition([Box("Bip01 Spine1", 0, Vector3.Zero, new(2, 2, 2))]);
        Assert.Null(RagdollCut.Build(source, new(1, 0, 0, -4), 1, true));
        Assert.Null(RagdollCut.Build(source, new(1, 0, 0, -1), 1, true));
        Assert.Single(Assert.IsType<RagdollDefinition>(RagdollCut.Build(source, new(1, 0, 0, -4), -1, true)).Bodies);
        Assert.Null(RagdollCut.Build(source, Vector4.Zero, 1, true));
        Assert.Null(RagdollCut.Build(source, new(float.NaN, 1, 0, 0), 1, true));
        Assert.Null(RagdollCut.Build(source, new(1, 0, 0, float.PositiveInfinity), 1, true));
        Assert.Null(RagdollCut.Build(source, new(1, 0, 0, 0), 0, true));
    }

    [Theory]
    [InlineData(PhysicsShapeType.Sphere)]
    [InlineData(PhysicsShapeType.Capsule)]
    [InlineData(PhysicsShapeType.Mesh)]
    public void Rounded_and_convex_colliders_are_clipped_without_leaking_across_an_oblique_plane(PhysicsShapeType type)
    {
        var body = new RagdollBody
        {
            Name = "Bip01 Spine1", Bone = 0, Type = type, Mass = 5, Size = new(1, 4, 0),
            Bind = new(new(0, 10, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.25f)),
            BoneInBody = RigidTransform.Identity,
            Hull = type == PhysicsShapeType.Mesh ? Corners(new(2, 4, 2)) : null,
        };
        var normal = Vector3.Normalize(new Vector3(0.6f, 0.8f, 0.25f));
        var plane = new Vector4(normal, -Vector3.Dot(normal, body.Bind.Position));
        foreach (int side in new[] { -1, 1 })
        {
            var cut = Assert.Single(Assert.IsType<RagdollDefinition>(RagdollCut.Build(Definition([body]), plane, side, true)).Bodies);
            Assert.Equal(PhysicsShapeType.Mesh, cut.Type);
            Assert.NotNull(cut.Hull);
            Assert.All(cut.Hull, vertex => Assert.True(side * Distance(plane, cut.Bind.Apply(vertex)) >= -0.0001f));
            Assert.True(cut.Mass > 0);
        }
    }

    [Fact]
    public void Retained_spine_sections_stay_connected_when_the_central_anchor_is_beyond_the_cut_but_a_severed_shoulder_is_free()
    {
        var source = Definition(
        [
            Box("Bip01 Spine1", 0, Vector3.Zero, new(2, 2, 2)),
            Box("Bip01 Spine2", 1, new(0, 1.5f, 0), new(2, 2, 2)),
            Box("Bip01 L UpperArm", 2, new(2, 1, 0), new(1, 3, 1)),
        ], [(0, 1, new Vector3(0, 2, 0)), (1, 2, new Vector3(1.5f, 2, 0))]);
        var cut = Assert.IsType<RagdollDefinition>(RagdollCut.Build(source, new(0, 1, 0, -1.8f), -1, true));
        var joint = Assert.Single(cut.Joints);
        Assert.Equal("Bip01 Spine1", cut.Bodies[joint.A].Name);
        Assert.Equal("Bip01 Spine2", cut.Bodies[joint.B].Name);
        var anchorA = cut.Bodies[joint.A].Bind.Apply(joint.OffsetA);
        var anchorB = cut.Bodies[joint.B].Bind.Apply(joint.OffsetB);
        Assert.True(anchorA.Y <= 1.8001f && anchorB.Y <= 1.8001f, "The retained axial joint must move into the retained volume");
        Assert.InRange(Vector3.Distance(anchorA, anchorB), 0, 0.0001f);
        int arm = Array.FindIndex(cut.Bodies, body => body.Name == "Bip01 L UpperArm");
        Assert.True(arm >= 0);
        Assert.Equal(0UL, cut.Joined[arm]);
    }

    internal static RagdollBody Box(string name, int bone, Vector3 at, Vector3 size) => new()
    {
        Name = name, Bone = bone, Type = PhysicsShapeType.Box, Size = size, Mass = 10,
        Bind = new(at, Quaternion.Identity), BoneInBody = RigidTransform.Identity,
    };

    internal static RagdollDefinition Definition(RagdollBody[] bodies, (int A, int B, Vector3 Anchor)[]? anchors = null)
    {
        var joints = (anchors ?? []).Select(j => new RagdollJoint
        {
            A = j.A, B = j.B, Type = PhysicsJointType.Spherical,
            OffsetA = bodies[j.A].Bind.Inverse().Apply(j.Anchor), OffsetB = bodies[j.B].Bind.Inverse().Apply(j.Anchor),
            BasisA = Quaternion.Identity, BasisB = Quaternion.Identity, Limits = true, Low = -1, High = 1, Swing = 1,
        }).ToArray();
        var joined = new ulong[bodies.Length];
        foreach (var j in joints) { joined[j.A] |= 1UL << j.B; joined[j.B] |= 1UL << j.A; }
        var bodyOfBone = Enumerable.Repeat(-1, bodies.Max(b => b.Bone) + 1).ToArray();
        for (int b = 0; b < bodies.Length; b++) bodyOfBone[bodies[b].Bone] = b;
        return new() { Bodies = bodies, Joints = joints, BodyOfBone = bodyOfBone, Joined = joined, Root = 0 };
    }

    static Vector3[] Corners(Vector3 size) => [.. from x in new[] { -1, 1 } from y in new[] { -1, 1 } from z in new[] { -1, 1 } select new Vector3(x * size.X / 2, y * size.Y / 2, z * size.Z / 2)];
    static float Distance(Vector4 plane, Vector3 point) => Vector3.Dot(new(plane.X, plane.Y, plane.Z), point) + plane.W;
}
