using System.Numerics;
using Meitou.Brutality.Rendering.Surface;
using Meitou.Rendering;

namespace Meitou.Tests.Modding;

public sealed partial class BrutalityGeometryTests
{
    [Fact]
    public void Runtime_cavity_uses_the_reference_height_band_and_actual_back_and_flank_returns()
    {
        var wall = Assert.IsType<Vector3[,]>(RuntimeCavityLining.Build(TorsoShell(), Rig, new(0, 10, 0), 1));
        Assert.Equal(17, wall.GetLength(0)); Assert.Equal(27, wall.GetLength(1));
        Assert.Equal(10.72f, wall[0, 0].Y, .0001f); Assert.Equal(16.30f, wall[16, 0].Y, .0001f);
        for (int row = 0; row < 17; row++)
        {
            for (int column = 0; column < 6; column++) Assert.Equal(wall[row, 0].X, wall[row, column].X);
            for (int column = 21; column < 27; column++) Assert.Equal(wall[row, 26].X, wall[row, column].X);
            Assert.True(wall[row, 0].Z - wall[row, 5].Z > .2f);
            Assert.True(wall[row, 26].Z - wall[row, 21].Z > .2f);
            Assert.True(wall[row, 13].Z < wall[row, 5].Z - .2f);
            for (int column = 0; column < 27; column++)
            {
                var p = wall[row, column];
                Assert.True(p.X * p.X / 4 + p.Z * p.Z / 1.44f < 1, "The lining must stay inside the installed torso cross-section.");
                Assert.Equal(wall[row, 0].Y, p.Y);
            }
        }
    }

    [Fact]
    public void Runtime_cavity_follows_body_scale_translation_and_ignores_the_arm_beside_the_hips()
    {
        var source = TorsoShell(); var hip = new Vector3(0, 10, 0);
        var original = Assert.IsType<Vector3[,]>(RuntimeCavityLining.Build(source, Rig, hip, 1));
        var delta = new Vector3(3, -5, 4);
        var changed = source.Select(vertex => vertex with { Position = (vertex.Position - hip) * 2 + hip + delta })
            .Concat(Box(new(-30, 10, -2), new(-28, 30, 2), 5)).ToArray();
        var scaled = Assert.IsType<Vector3[,]>(RuntimeCavityLining.Build(changed, Rig, hip + delta, 2));
        for (int row = 0; row < 17; row++) for (int column = 0; column < 27; column++)
            Assert.True(Vector3.Distance((original[row, column] - hip) * 2 + hip + delta, scaled[row, column]) < .0001f);
    }

    static Vertex[] TorsoShell()
    {
        var result = new List<Vertex>();
        Vector3 At(int segment, float y) => new(2 * MathF.Cos(segment * MathF.Tau / 64), y, 1.2f * MathF.Sin(segment * MathF.Tau / 64));
        for (int segment = 0; segment < 64; segment++)
            foreach (var p in new[] { At(segment, 10.4f), At(segment + 1, 10.4f), At(segment + 1, 16.6f),
                At(segment, 10.4f), At(segment + 1, 16.6f), At(segment, 16.6f) })
                result.Add(Point(p, 1) with { Normal = Vector3.Normalize(new(p.X / 4, 0, p.Z / 1.44f)) });
        return result.ToArray();
    }
}
