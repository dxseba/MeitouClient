using Meitou.Simulation.Wounds;
using static Meitou.Simulation.Wounds.WoundPolicy;

namespace Meitou.Tests.Wounds;

/// <summary>Kenshi Gore Lab's <c>MeleePolicyTest</c> checks, on the C# port.</summary>
public class WoundPolicyTests
{
    static readonly WoundInput Cut = new(1, WoundAttack.Front, false, true, 25, 0, 0, 100);

    [Fact]
    public void A_front_torso_cut_is_a_slash_with_a_dose()
    {
        var r = Evaluate(Cut);
        Assert.True(r.Accepted);
        Assert.Equal(WoundKind.Slash, r.Kind);
        Assert.Equal(0.25f, r.Dose, 5);
        Assert.Equal((0f, -1f, 0.20f), (r.Shape.Cosine, r.Shape.Sine, r.Shape.Aspect));
        Assert.Equal(1, r.Hits);
    }

    [Fact]
    public void Unsupported_hits_are_refused()
    {
        for (int d = 0; d <= 5; d++) Assert.True(Evaluate(Cut with { CutDirection = d }).Accepted);
        foreach (int d in new[] { -1, 6, 7, 10 }) Assert.False(Evaluate(Cut with { CutDirection = d }).Accepted);
        Assert.False(Evaluate(Cut with { Harpoon = true }).Accepted);
        Assert.False(Evaluate(Cut with { FrontTorso = false }).Accepted);
        foreach (var a in new[] { WoundAttack.None, WoundAttack.Back, WoundAttack.Top, WoundAttack.Front | WoundAttack.Left, WoundAttack.Front | WoundAttack.Bottom, WoundAttack.Front | (WoundAttack)64 })
            Assert.False(Evaluate(Cut with { Attack = a }).Accepted);
        Assert.True(Evaluate(Cut with { Attack = WoundAttack.Front | WoundAttack.Top }).Accepted);
        foreach (var bad in new[] { Cut with { Maximum = 0 }, Cut with { Cutting = -1 }, Cut with { Pierce = -1 }, Cut with { Blunt = float.NaN }, Cut with { Cutting = float.PositiveInfinity }, Cut with { Maximum = float.NaN }, Cut with { Cutting = 0 } })
            Assert.False(Evaluate(bad).Accepted);
    }

    [Fact]
    public void Blunt_crushes_thrusts_pierce_and_dose_is_capped()
    {
        Assert.Equal(WoundKind.Crush, Evaluate(Cut with { Cutting = 0, Blunt = 5 }).Kind);
        Assert.Equal(WoundKind.Thrust, Evaluate(Cut with { CutDirection = 4, Cutting = 0, Pierce = 8 }).Kind);
        Assert.False(Evaluate(Cut with { Cutting = 0, Pierce = 8 }).Accepted);
        var r = Evaluate(Cut with { LocalDose = 3.9f, LocalHits = 1000 });
        Assert.Equal(4f, r.Dose, 5);
        Assert.Equal(8, r.Hits);
        Assert.Equal(1f, r.Shape.Depth, 5);
        Assert.Equal(0.25f, Evaluate(Cut with { LocalDose = float.NaN }).Dose, 5);
        Assert.Equal(0.25f, Evaluate(Cut with { LocalDose = -5 }).Dose, 5);
    }

    [Fact]
    public void Sample_follows_the_cut_axis_and_rejects_bad_input()
    {
        WoundShape horizontal = ShapeForDirection(3, 0.5f), vertical = ShapeForDirection(1, 0.5f), deep = ShapeForDirection(3, 1), minor = ShapeForDirection(3, 0.01f);
        Assert.Equal(0, Sample(1.1f, 0, minor).Area);
        Assert.True(Sample(1.1f, 0, deep).Area > 0.9f);
        Assert.Equal(new WoundProfile(1, 1, 1), Sample(0, 0, horizontal));
        Assert.Equal(0, Sample(2.1f, 0, horizontal).Ring);
        Assert.True(Sample(0.8f, 0, horizontal).Area > 0.9f);
        Assert.Equal(0, Sample(0.8f, 0, vertical).Ring);
        Assert.True(Sample(0, 0.8f, vertical).Area > 0.9f);
        Assert.Equal(0, Sample(float.NaN, 0, horizontal).Ring);
        Assert.Equal(0, Sample(0, 1e11f, horizontal).Ring);
    }

    [Fact]
    public void Layers_open_in_order_and_bone_and_organs_never()
    {
        var shape = ShapeForDirection(3, 0.5f);
        var middle = Sample(0, 0, shape);
        Assert.False(RemovesLayer(WoundLayer.Bone, middle, 1, 8));
        Assert.False(RemovesLayer(WoundLayer.Organ, middle, 1, 8));
        Assert.True(RemovesLayer(WoundLayer.Skin, middle, 0, 1));
        Assert.False(RemovesLayer(WoundLayer.Muscle, middle, 0.39f, 1));
        Assert.True(RemovesLayer(WoundLayer.Muscle, middle, 0.4f, 1));
        Assert.False(RemovesLayer(WoundLayer.Tendon, middle, 1, 2));
        Assert.True(RemovesLayer(WoundLayer.Tendon, middle, 1, 3));
        Assert.False(RemovesLayer(WoundLayer.Muscle, new WoundProfile(1, 0, 1), 1, 8));
        for (int ix = -160; ix <= 160; ix += 4)
            for (int iy = -40; iy <= 40; iy += 2)
            {
                var p = Sample(ix * 0.01f, iy * 0.01f, shape);
                Assert.True(p.Core >= 0 && p.Core <= p.Area && p.Area <= p.Ring && p.Ring <= 1);
                Assert.True(!RemovesLayer(WoundLayer.Muscle, p, 1, 8) || RemovesLayer(WoundLayer.Skin, p, 1, 8));
                Assert.True(!RemovesLayer(WoundLayer.Tendon, p, 1, 8) || RemovesLayer(WoundLayer.Muscle, p, 1, 8));
            }
    }
}
