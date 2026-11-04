using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Simulation.Ragdolls;
using static Meitou.Tests.Bodies.BodyFixtures;

namespace Meitou.Tests.Physics;

public partial class RagdollHitReactionTests
{
    [Fact]
    public void Faithful_robot_actors_keep_file_mass_while_advanced_robots_are_heavier()
    {
        static float ActorMass(Scene scene)
        {
            scene.World.RunTick();
            var field = typeof(RagdollSystem).GetField("physics", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var physics = Assert.IsType<RagdollPhysics>(field.GetValue(scene.Physics));
            var active = physics.Simulation.Bodies.ActiveSet;
            Assert.Equal(4, active.Count);
            float total = 0;
            for (int i = 0; i < active.Count; i++)
                total += 1 / physics.Simulation.Bodies[active.IndexToHandle[i]].LocalInertia.InverseMass;
            return total;
        }
        using var organic = new Scene(advanced: false);
        using var faithfulRobot = new Scene(advanced: false, robot: true);
        using var advancedRobot = new Scene(robot: true);
        float sourceMass = ActorMass(organic);
        Assert.Equal(40, sourceMass, 3);
        Assert.Equal(sourceMass, ActorMass(faithfulRobot), 3);
        Assert.Equal(sourceMass * 2.5f, ActorMass(advancedRobot), 3);
    }

    [Fact]
    public void Robot_knockdown_reduces_the_added_blow_but_preserves_existing_movement()
    {
        using var human = new Scene();
        using var robot = new Scene(robot: true);
        foreach (var scene in new[] { human, robot })
        {
            scene.Cold.Blow = (new(43, 8, 0), new(40, 8, 0));
            scene.World.RunTick();
        }
        Assert.InRange(human.State.Pose[0].Position.X - 1000, 1.3f, 1.5f);
        Assert.InRange(robot.State.Pose[0].Position.X - 1000, 0.5f, 0.63f);
    }

    [Fact]
    public void A_robot_falling_without_a_blow_keeps_the_same_speed_and_gravity_as_an_organic_body()
    {
        using var human = new Scene();
        using var robot = new Scene(robot: true);
        foreach (var scene in new[] { human, robot })
        {
            scene.Cold.Blow = (new(18, -7, 4), Vector3.Zero);
            scene.World.RunTick();
        }
        Assert.InRange(Vector3.Distance(human.State.Pose[0].Position, robot.State.Pose[0].Position), 0, 0.005f);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Severed_robot_heads_and_limbs_launch_and_spin_less(bool arm)
    {
        using var human = new Scene();
        using var robot = new Scene(robot: true);
        var motion = new List<(float Distance, float Angle)>();
        foreach (var scene in new[] { human, robot })
        {
            scene.Cold.Blow = (new(30, 0, 0), new(30, 0, 0));
            if (arm) scene.Cold.Medical!.Sever(LArm, Context(scene.Cold.Race), false);
            else scene.Cold.Medical!.Decapitate();
            scene.World.RunTick();
            int bone = arm ? 2 : 1;
            var piece = Assert.Single(scene.Physics.PublishDetached(scene.Id), p => p.Part == (arm ? 0 : -1));
            var shown = Assert.Single(scene.World.Snapshot.Characters, c => c.Id == scene.Id);
            var placement = CharacterRigs.Placement(scene.Setup, shown.Position, shown.Yaw, 1);
            var position = Vector3.Transform(piece.Bones.Position[bone], placement);
            var rotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(placement) * piece.Bones.Rotation[bone]);
            float angle = 2 * MathF.Acos(Math.Clamp(MathF.Abs(rotation.W), 0, 1));
            motion.Add((position.X - (arm ? 1003 : 1000), angle));
        }
        Assert.True(motion[0].Distance > 0.7f, $"Organic part: {motion[0]}");
        Assert.InRange(motion[1].Distance / motion[0].Distance, 0.2f, 0.55f);
        Assert.True(motion[0].Angle > 0.005f, $"Organic part should spin: {motion[0]}");
        Assert.True(motion[1].Angle < motion[0].Angle * 0.7f, $"Robot {motion[1]}, organic {motion[0]}");
    }

    [Fact]
    public void Later_impacts_on_unconscious_robot_bodies_have_less_throw()
    {
        var distances = new List<float>();
        foreach (bool robot in new[] { false, true })
        {
            using var scene = new Scene(robot: robot);
            scene.Settle();
            float before = scene.State.Pose.Average(p => p.Position.X);
            Assert.True(scene.Hit(push: new(40, 0, 0)));
            scene.World.RunTicks(3);
            distances.Add(scene.State.Pose.Average(p => p.Position.X) - before);
        }
        Assert.True(distances[0] > 0.2f);
        Assert.True(distances[1] < distances[0] * 0.65f, $"Robot {distances[1]}, organic {distances[0]}");
    }

    [Fact]
    public void Both_sides_of_a_bisected_robot_receive_less_throw()
    {
        using var human = new RagdollTorsoPhysicsTests.Scene();
        using var robot = new RagdollTorsoPhysicsTests.Scene(robot: true);
        foreach (var scene in new[] { human, robot })
        {
            scene.Cold.Blow = (new(40, 8, 0), new(40, 8, 0));
            scene.World.RunTick();
        }
        var organic = Assert.IsType<Meitou.Simulation.Saving.SavedRagdoll>(human.Physics.SaveState(human.Id)).Torso!;
        var metal = Assert.IsType<Meitou.Simulation.Saving.SavedRagdoll>(robot.Physics.SaveState(robot.Id)).Torso!;
        foreach (var (a, b) in new[] { (organic.Positive, metal.Positive), (organic.Negative, metal.Negative) })
        {
            float baseline = a.Pose[0].Position.X - 20;
            Assert.True(baseline > 1);
            Assert.InRange((b.Pose[0].Position.X - 20) / baseline, 0.2f, 0.6f);
        }
    }
}
