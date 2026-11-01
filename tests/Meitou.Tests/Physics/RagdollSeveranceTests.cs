using System.Numerics;
using Meitou.Data.Physics;
using Meitou.Simulation.Ragdolls;
using static Meitou.Tests.Physics.RagdollCutTests;

namespace Meitou.Tests.Physics;

public class RagdollSeveranceTests
{
    [Theory]
    [InlineData(-1, "Bip01 Head", "Bip01 Jaw")]
    [InlineData(0, "Bip01 L UpperArm", "Bip01 L Forearm", "Bip01 L Hand")]
    [InlineData(1, "Bip01 R UpperArm", "Bip01 R Forearm", "Bip01 R Hand")]
    [InlineData(2, "Bip01 L Thigh", "Bip01 L Calf", "Bip01 L Foot")]
    [InlineData(3, "Bip01 R Thigh", "Bip01 R Calf", "Bip01 R Foot")]
    public void A_whole_severed_part_has_only_its_own_joint_chain_and_no_body_anchor(int part, params string[] removed)
    {
        var source = Human();
        var (remaining, detached) = RagdollSeverance.Split(source, part);
        Assert.NotNull(remaining);
        Assert.NotNull(detached);
        Assert.Equal(removed.Order(), detached.Bodies.Select(b => b.Name).Order());
        Assert.DoesNotContain(remaining.Bodies, b => removed.Contains(b.Name));
        Assert.Equal(source.Bodies.Length, remaining.Bodies.Length + detached.Bodies.Length);
        Assert.Equal(removed.Length - 1, detached.Joints.Length);
        Assert.All(detached.Joints, j =>
        {
            Assert.Contains(detached.Bodies[j.A].Name, removed);
            Assert.Contains(detached.Bodies[j.B].Name, removed);
        });
        Assert.Contains(remaining.Bodies, b => b.Name == "Bip01 Neck");
        foreach (var body in source.Bodies.Where(b => !removed.Contains(b.Name)))
            Assert.Same(body, Assert.Single(remaining.Bodies, b => b.Name == body.Name));
    }

    [Theory]
    [InlineData(0, 12.4f, "Bip01 L Forearm", "Bip01 L Hand", "Bip01 R Forearm")]
    [InlineData(2, 6f, "Bip01 L Calf", "Bip01 L Foot", "Bip01 R Calf")]
    public void A_lower_cut_splits_the_intersected_collider_and_preserves_other_limbs(int part, float height, string crossed, string distal, string other)
    {
        var source = Human();
        var (remaining, detached) = RagdollSeverance.Split(source, part, height);
        Assert.NotNull(remaining);
        Assert.NotNull(detached);
        var upper = Assert.Single(remaining.Bodies, b => b.Name == crossed);
        var lower = Assert.Single(detached.Bodies, b => b.Name == crossed);
        Assert.Equal(PhysicsShapeType.Mesh, upper.Type);
        Assert.Equal(PhysicsShapeType.Mesh, lower.Type);
        Assert.All(upper.Hull!, v => Assert.True(upper.Bind.Apply(v).Y >= height - 0.0001f));
        Assert.All(lower.Hull!, v => Assert.True(lower.Bind.Apply(v).Y <= height + 0.0001f));
        Assert.Contains(detached.Bodies, b => b.Name == distal);
        Assert.DoesNotContain(remaining.Bodies, b => b.Name == distal);
        Assert.Same(source.Bodies.Single(b => b.Name == other), remaining.Bodies.Single(b => b.Name == other));
        Assert.DoesNotContain(detached.Bodies, b => b.Name == other);
        Assert.Single(detached.Joints);
    }

    [Fact]
    public void Unsupported_parts_and_invalid_heights_leave_no_half_built_definition()
    {
        foreach (var (part, height) in new[] { (-2, 0f), (4, 0f), (0, -1f), (0, float.NaN), (0, float.PositiveInfinity) })
        {
            var cut = RagdollSeverance.Split(Human(), part, height);
            Assert.Null(cut.Remaining);
            Assert.Null(cut.Detached);
        }
    }

    static RagdollDefinition Human()
    {
        var bodies = new List<RagdollBody>
        {
            Box("Bip01", 0, new(0, 12, 0), new(3, 4, 2)),
            Box("Bip01 Neck", 1, new(0, 15, 0), Vector3.One),
            Box("Bip01 Head", 2, new(0, 17, 0), new(2, 2, 2)),
            Box("Bip01 Jaw", 3, new(0, 16, 1), Vector3.One),
        };
        var joints = new List<(int, int, Vector3)> { (0, 1, new(0, 14, 0)), (1, 2, new(0, 16, 0)), (2, 3, new(0, 16, 0.5f)) };
        for (int side = -1; side <= 1; side += 2)
        {
            string name = side < 0 ? "R" : "L";
            int arm = bodies.Count;
            bodies.Add(Box($"Bip01 {name} UpperArm", arm, new(side * 3, 14, 0), new(1, 3, 1)));
            bodies.Add(Box($"Bip01 {name} Forearm", arm + 1, new(side * 3, 11.5f, 0), new(1, 3, 1)));
            bodies.Add(Box($"Bip01 {name} Hand", arm + 2, new(side * 3, 9.5f, 0), Vector3.One));
            joints.AddRange([(0, arm, new(side * 2, 15, 0)), (arm, arm + 1, new(side * 3, 13, 0)), (arm + 1, arm + 2, new(side * 3, 10, 0))]);
            int leg = bodies.Count;
            bodies.Add(Box($"Bip01 {name} Thigh", leg, new(side, 8, 0), new(1, 3, 1)));
            bodies.Add(Box($"Bip01 {name} Calf", leg + 1, new(side, 5, 0), new(1, 3, 1)));
            bodies.Add(Box($"Bip01 {name} Foot", leg + 2, new(side, 3, 0), Vector3.One));
            joints.AddRange([(0, leg, new(side, 10, 0)), (leg, leg + 1, new(side, 6.5f, 0)), (leg + 1, leg + 2, new(side, 3.5f, 0))]);
        }
        return Definition([.. bodies], [.. joints]);
    }
}
