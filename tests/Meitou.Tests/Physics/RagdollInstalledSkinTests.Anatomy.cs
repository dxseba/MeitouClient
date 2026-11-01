using System.Numerics;
using Meitou.Content;
using Meitou.Data.Characters;
using Meitou.Data.Physics;
using Meitou.Rendering;
using Meitou.Rendering.Characters;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Ragdolls;
using Meitou.Simulation.Wounds;
using static Meitou.Tests.Bodies.BodyFixtures;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Physics;

public partial class RagdollInstalledSkinTests
{
    [Theory]
    [InlineData(TorsoBisectionStyle.Oblique, 2)]
    [InlineData(TorsoBisectionStyle.Vertical, 1)]
    [Slow]
    public void Installed_internal_anatomy_keeps_triangles_with_one_free_physical_component(TorsoBisectionStyle style, int direction)
    {
        var install = GameInstall.Locate();
        Assert.SkipWhen(install is null, "Kenshi install not found");
        string anatomyPath = GameFiles.Resolve(Path.Combine(install.ModsDirectory, "Gauntlet of Crowns", "Ecorche.anatomy"));
        Assert.SkipWhen(!File.Exists(anatomyPath), "Locally installed Ecorche.anatomy not found");
        var (rig, definition, model, setup, rigs, look) = Installed();
        var skin = model.Parts.SelectMany(part => part.Vertices).Where((_, index) => index % 2 == 0)
            .Select(vertex => new WoundPoint(vertex.Position, vertex.Normal, vertex.Uv, (int)(vertex.Bones & 255), vertex.Bones, vertex.Weights)).ToArray();
        var anatomy = Assert.IsType<List<Vertex>>(CharacterAnatomy.Build(rig, skin, HeadWoundSurface.Source(model), false, null, false, false, anatomyPath));
        Assert.True(anatomy.Count(vertex => vertex.Tangent.W == 1) > 1000, "The real imported muscle geometry must be present");
        Assert.Contains(anatomy, vertex => vertex.Tangent.W == 7);
        using var physics = new RagdollSystem(rigs, new Flat(), BodyOptions.Default with { AdvancedRagdolls = true }, _ => setup, (_, _, _) => definition);
        using var world = new SimWorld(new WorldSettings { Seed = 41 }, new OpenGroundWalkability((_, _) => 0), [physics]);
        const float yaw = -MathF.PI / 2;
        var cut = TorsoBisectionPolicy.StateForDirection(direction, 1, Vector2.UnitX, yaw, style);
        var push = new Vector3(50, 10, 0);
        var cold = new CharacterCold
        {
            Name = "Anatomy test corpse", Race = Human, Medical = Healthy(), Appearance = look,
            TorsoBisection = cut, Blow = (push, push), Knockdown = push,
        };
        cold.Medical.KillFromBisection();
        var id = world.Characters.Spawn(new() { Position = new(20, 0, 20), Yaw = yaw }, cold, 0);
        var beforeSkin = new Matrix4x4[rig.BoneCount];
        var afterSkin = new Matrix4x4[rig.BoneCount];
        var scratch = rig.Rent();
        try
        {
            scratch.Pose(setup.Shape, setup.Fixed.Select(layer => new ActiveLayer(layer.Def, layer.Time, 1)).ToArray(), beforeSkin);
            var baseline = anatomy.Select(vertex => Skin(vertex, beforeSkin)).ToArray();
            foreach (int tick in new[] { 1, 90, 320 })
            {
                while (world.Tick < tick) world.RunTick();
                var shown = Assert.Single(world.Snapshot.Characters, character => character.Id == id);
                var halves = Assert.IsType<TorsoRagdollPose>(shown.TorsoRagdoll);
                foreach (int side in new[] { -1, 1 })
                {
                    var pose = side < 0 ? halves.Negative : halves.Positive;
                    Assert.True(pose.AbsolutePositions);
                    Assert.NotNull(pose.Components);
                    if (style == TorsoBisectionStyle.Oblique && side < 0)
                        Assert.NotEqual(ComponentOf(rig, pose.Components, "Bip01"), ComponentOf(rig, pose.Components, "Bip01 R Forearm"));
                    scratch.Pose(setup.Shape, [], afterSkin, pose);
                    CheckAnatomyTriangles(anatomy, baseline, afterSkin, pose.Components, cut.Plane, side, tick, style);
                }
            }
        }
        finally { rig.Return(scratch); }
    }

    void CheckAnatomyTriangles(List<Vertex> anatomy, Vector3[] baseline, Matrix4x4[] palette, int[] components, Vector4 plane, int side, int tick, TorsoBisectionStyle style)
    {
        Span<EdgePoint> clipped = stackalloc EdgePoint[4];
        int examined = 0;
        float worst = 0;
        string message = "";
        for (int triangle = 0; triangle + 2 < anatomy.Count; triangle += 3)
        {
            EdgePoint Point(int offset) => new(anatomy[triangle + offset].Position, baseline[triangle + offset], Skin(anatomy[triangle + offset], palette));
            int count = Clip(Point(0), Point(1), Point(2), plane, side, clipped);
            if (count == 0) continue;
            int component = -1;
            for (int index = triangle; index < triangle + 3; index++)
                for (int influence = 0; influence < 4; influence++)
                {
                    if (anatomy[index].Weights[influence] <= 0.00001f) continue;
                    int bone = (int)(anatomy[index].Bones >> (8 * influence) & 255);
                    Assert.InRange(bone, 0, components.Length - 1);
                    int next = components[bone];
                    Assert.True(next >= 0, $"Anatomy bone{bone} must name a physical component");
                    if (component < 0) component = next;
                    Assert.True(component == next, $"{style} side{side} tick{tick}: anatomy layer{anatomy[index].Tangent.W} triangle{triangle / 3} joins disconnected components{component}/{next}");
                }
            for (int edge = 0; edge < count; edge++)
            {
                var a = clipped[edge];
                var b = clipped[(edge + 1) % count];
                float before = Vector3.Distance(a.Before, b.Before);
                if (before < 0.0001f) continue;
                float after = Vector3.Distance(a.After, b.After);
                Assert.True(float.IsFinite(after));
                float limit = MathF.Max(8 * before, 6);
                if (after / limit <= worst) continue;
                worst = after / limit;
                message = $"{style} side{side} tick{tick}: anatomy layer{anatomy[triangle].Tangent.W} triangle{triangle / 3} edge grew from{before:F3} to{after:F3} (limit{limit:F3})";
            }
            examined++;
        }
        Assert.True(examined > 1000, "The retained installed anatomy must provide substantial real deformation coverage");
        output.WriteLine(message);
        Assert.True(worst <= 1, message);
    }
}
