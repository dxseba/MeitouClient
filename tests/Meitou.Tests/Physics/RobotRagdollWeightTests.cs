using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using Meitou.Simulation.Ragdolls;
using static Meitou.Tests.Physics.RagdollCutTests;

namespace Meitou.Tests.Physics;

public class RobotRagdollWeightTests
{
    [Fact]
    public void Metal_body_transfers_less_collision_speed_to_itself_than_an_organic_body()
    {
        static float Collide(bool metal)
        {
            var source = Definition([Box("Bip01", 0, Vector3.Zero, new(2, 2, 2))]);
            var mass = (metal ? RobotRagdollWeight.Weight(source) : source).Bodies[0].Mass;
            using var physics = new RagdollPhysics();
            var shape = new Box(2, 2, 2);
            var index = physics.Simulation.Shapes.Add(shape);
            var moving = physics.AddBody(BodyDescription.CreateDynamic(new RigidPose(new(-2.1f, 100, 0)),
                new BodyVelocity(new(20, 0, 0)), shape.ComputeInertia(10), RagdollPhysics.Collidable(index),
                new BodyActivityDescription(0.01f)), new BodyTag { Ragdoll = 1 });
            var struck = physics.AddBody(BodyDescription.CreateDynamic(new RigidPose(new(0, 100, 0)),
                shape.ComputeInertia(mass), RagdollPhysics.Collidable(index), new BodyActivityDescription(0.01f)), new BodyTag { Ragdoll = 2 });
            for (int step = 0; step < 10; step++) physics.Step(1f / 120);
            Assert.True(physics.Simulation.Bodies[moving].Velocity.Linear.X > 0);
            return physics.Simulation.Bodies[struck].Velocity.Linear.X;
        }
        float organic = Collide(false), metal = Collide(true);
        Assert.InRange(organic, 8, 12);
        Assert.InRange(metal / organic, 0.45f, 0.7f);
    }

    [Fact]
    public void Robot_mass_is_uniform_and_keeps_compact_cut_rejection_and_thin_slabs()
    {
        var source = Definition([Box("Bip01 Spine1", 0, Vector3.Zero, new(4, 4, 4))]);
        var metal = RobotRagdollWeight.Weight(source);
        Assert.Equal(10, source.Bodies[0].Mass);
        Assert.Equal(25, metal.Bodies[0].Mass);
        var plane = new Vector4(0, 1, 0, -1.99f);
        var organic = Assert.IsType<RagdollDefinition>(RagdollCut.Build(source, plane, 1, true));
        var weighted = Assert.IsType<RagdollDefinition>(RagdollCut.Build(metal, plane, 1, true));
        Assert.Equal(organic.Bodies.Length, weighted.Bodies.Length);
        Assert.InRange(weighted.Bodies[0].Mass / organic.Bodies[0].Mass, 2.499f, 2.501f);
        RagdollInstalledRaceTests.AssertInertia(weighted.Bodies[0], "weighted thin slab");
        var normal = Vector3.Normalize(Vector3.One);
        var corner = new Vector4(normal, -Vector3.Dot(normal, new(2, 2, 2)) + 0.01f);
        Assert.Null(RagdollCut.Build(metal, corner, 1, true));
        Assert.Null(source.Bodies[0].Hull);
    }
}
