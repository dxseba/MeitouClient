using System.Numerics;
using Meitou.Brutality;
using Meitou.Brutality.Rendering;
using Meitou.Data;
using Meitou.Data.Characters;

namespace Meitou.Tests.Modding;

public sealed partial class BrutalityGeometryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Natural_appearance_restores_only_the_explicit_cut_slot_and_preserves_unrelated_native_losses(int slot)
    {
        using var scene = new Scene();
        var original = scene.Appearance;
        original.Shape = new CharacterShapeInput
        {
            Missing = new HashSet<Limb>(Enum.GetValues<Limb>()), HideStump = HideStump.BothArms,
            Sliders = new Dictionary<string, float> { ["Height"] = 117 }, LegRatio = .23f, Starvation = .4f,
        };
        SetVisibility(original, 15, true, false);
        var state = State(torso: new(new(0, 1, 0, -13)), limbs: [new(slot, .5f, Vector2.Zero)]);
        var natural = NaturalAppearance.Make(new GameDatabase(), original, state: state);

        Assert.DoesNotContain((Limb)slot, natural.Shape!.Missing);
        Assert.Equal(3, natural.Shape.Missing.Count);
        foreach (var limb in Enum.GetValues<Limb>().Where(limb => (int)limb != slot)) Assert.Contains(limb, natural.Shape.Missing);
        Assert.Equal(HideStump.BothArms & ~(HideStump)((1 << slot) & 3), natural.Shape.HideStump);
        Assert.Equal(15 & ~(1 << slot), natural.HiddenLimbs);
        Assert.True(natural.Headless);
        Assert.Equal(117, natural.Shape.Sliders["Height"]);
        Assert.Equal(.23f, natural.Shape.LegRatio); Assert.Equal(.4f, natural.Shape.Starvation);
        Assert.Equal(4, original.Shape.Missing.Count);
        Assert.Equal(HideStump.BothArms, original.Shape.HideStump);
        Assert.Equal(15, original.HiddenLimbs); Assert.True(original.Headless);
    }

    [Fact]
    public void Torso_only_or_unspecified_presentation_does_not_regrow_native_missing_limbs_or_head()
    {
        using var scene = new Scene();
        var original = scene.Appearance;
        original.Shape = new CharacterShapeInput { Missing = new HashSet<Limb> { Limb.LeftArm }, HideStump = HideStump.LeftArm };
        SetVisibility(original, 1, true, false);
        foreach (var state in new BrutalityActorView?[] { null, State(torso: new(new(0, 1, 0, -13))) })
        {
            var natural = NaturalAppearance.Make(new GameDatabase(), original, state: state);
            Assert.Contains(Limb.LeftArm, natural.Shape!.Missing);
            Assert.Equal(HideStump.LeftArm, natural.Shape.HideStump);
            Assert.Equal(1, natural.HiddenLimbs); Assert.True(natural.Headless);
        }
    }

    [Theory]
    [InlineData(HeadCutMode.AttachedSplit)]
    [InlineData(HeadCutMode.DetachedSplit)]
    public void Explicit_custom_head_geometry_restores_head_skin_without_restoring_an_unrelated_missing_arm(HeadCutMode mode)
    {
        using var scene = new Scene();
        var original = scene.Appearance;
        original.Shape = new CharacterShapeInput { Missing = new HashSet<Limb> { Limb.RightArm }, HideStump = HideStump.RightArm };
        SetVisibility(original, 2, true, true);
        var state = State(head: new(new(1, 0, 0, 0), mode));
        var natural = NaturalAppearance.Make(new GameDatabase(), original, state: state);
        Assert.False(natural.Headless); Assert.False(natural.HeadOnly);
        Assert.Contains(Limb.RightArm, natural.Shape!.Missing);
        Assert.Equal(HideStump.RightArm, natural.Shape.HideStump); Assert.Equal(2, natural.HiddenLimbs);
        Assert.True(original.Headless); Assert.True(original.HeadOnly);
    }

    [Fact]
    public void A_saved_jaw_wound_cannot_regrow_a_head_later_lost_to_native_decapitation()
    {
        using var scene = new Scene();
        var original = scene.Appearance;
        original.Shape = new CharacterShapeInput();
        var state = State() with { SevereHead = new(4, SevereHeadWoundKind.JawLost) };
        SetVisibility(original, 0, true, false);
        Assert.True(NaturalAppearance.Make(new GameDatabase(), original, state: state).Headless);
        SetVisibility(original, 0, false, true);
        var attached = NaturalAppearance.Make(new GameDatabase(), original, state: state);
        Assert.False(attached.Headless); Assert.False(attached.HeadOnly);
    }

    static void SetVisibility(CharacterAppearance appearance, int limbs, bool headless, bool headOnly)
    {
        typeof(CharacterAppearance).GetProperty(nameof(CharacterAppearance.HiddenLimbs))!.SetValue(appearance, limbs);
        typeof(CharacterAppearance).GetProperty(nameof(CharacterAppearance.Headless))!.SetValue(appearance, headless);
        typeof(CharacterAppearance).GetProperty(nameof(CharacterAppearance.HeadOnly))!.SetValue(appearance, headOnly);
    }
}
