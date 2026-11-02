using System.Numerics;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Wounds;

namespace Meitou.Tests.Wounds;

public class BrutalityHeadPolicyTests
{
    static readonly HeadWoundPlacement Frame = new(new(0.25f, 18.7f, 0.8f), Vector3.UnitZ, Vector3.UnitX);

    static SevereHeadWoundState FourHits(float level, float force, SevereHeadWoundKind requested,
        CombatWoundMechanism mechanism = CombatWoundMechanism.Slash, bool fixture = false)
    {
        var state = new SevereHeadWoundState(0);
        for (int i = 0; i < 4; i++) state = BrutalityHeadPolicy.Apply(state, level, force, requested, mechanism, 1, Frame, fixture);
        return state;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(19.999f)]
    public void Untrained_attackers_do_not_author_or_accumulate_severe_head_openings(float level)
    {
        Assert.Equal(new SevereHeadWoundState(0), FourHits(level, 100, SevereHeadWoundKind.BrokenSkull));
    }

    [Theory]
    [InlineData(20, SevereHeadWoundKind.JawLost)]
    [InlineData(39.999f, SevereHeadWoundKind.BrokenSkull)]
    public void First_skill_tier_opens_only_scalp_even_when_the_hit_and_requested_form_are_severe(float level, SevereHeadWoundKind requested)
    {
        var state = FourHits(level, 60, requested, CombatWoundMechanism.Thrust);
        Assert.Equal(4, state.HeavyHits);
        Assert.Equal(SevereHeadWoundKind.Scalp, state.Kind);
        Assert.Equal(CombatWoundMechanism.Thrust, state.Mechanism);
        Assert.Equal(Frame.Point, state.Placement!.Value.Point);
        Assert.Equal(Frame.Tangent, state.Placement.Value.Tangent);
    }

    [Theory]
    [InlineData(34.999f, SevereHeadWoundKind.JawLost, SevereHeadWoundKind.Scalp)]
    [InlineData(35, SevereHeadWoundKind.JawLost, SevereHeadWoundKind.JawLost)]
    [InlineData(34.999f, SevereHeadWoundKind.BrokenSkull, SevereHeadWoundKind.Scalp)]
    [InlineData(35, SevereHeadWoundKind.BrokenSkull, SevereHeadWoundKind.BrokenSkull)]
    public void Advanced_forms_need_both_the_second_skill_tier_and_delivered_force(float force, SevereHeadWoundKind requested, SevereHeadWoundKind expected)
    {
        var state = FourHits(40, force, requested, CombatWoundMechanism.Crush);
        Assert.Equal(expected, state.Kind);
        Assert.Equal(CombatWoundMechanism.Crush, state.Mechanism);
        Assert.True(state.IsValid);
    }

    [Fact]
    public void Only_eligible_heavy_hits_advance_the_counter_and_the_fourth_opens()
    {
        var third = new SevereHeadWoundState(3);
        Assert.Equal(third, BrutalityHeadPolicy.Apply(third, 100, 14.999f, SevereHeadWoundKind.BrokenSkull, CombatWoundMechanism.Slash));
        Assert.Equal(third, BrutalityHeadPolicy.Apply(third, 19.999f, 100, SevereHeadWoundKind.BrokenSkull, CombatWoundMechanism.Slash));
        var fourth = BrutalityHeadPolicy.Apply(third, 20, 15, SevereHeadWoundKind.BrokenSkull, CombatWoundMechanism.Slash);
        Assert.Equal(4, fourth.HeavyHits);
        Assert.Equal(SevereHeadWoundKind.Scalp, fourth.Kind);
    }

    [Fact]
    public void A_saved_counter_past_four_is_not_permanently_blocked_from_opening()
    {
        var state = BrutalityHeadPolicy.Apply(new(8), 40, 35, SevereHeadWoundKind.BrokenSkull, CombatWoundMechanism.Thrust, 2, Frame);
        Assert.Equal(9, state.HeavyHits);
        Assert.Equal(SevereHeadWoundKind.BrokenSkull, state.Kind);
    }

    [Fact]
    public void A_later_expert_hit_upgrades_scalp_without_relocating_or_reorienting_the_original_wound()
    {
        var before = new SevereHeadWoundState(4, SevereHeadWoundKind.Scalp, 2, Frame, CombatWoundMechanism.Thrust);
        var opposite = new HeadWoundPlacement(new(-0.5f, 18.8f, -0.9f), -Vector3.UnitZ, Vector3.UnitY);
        var after = BrutalityHeadPolicy.Apply(before, 40, 60, SevereHeadWoundKind.BrokenSkull, CombatWoundMechanism.Crush, 0, opposite);
        Assert.Equal(SevereHeadWoundKind.BrokenSkull, after.Kind);
        Assert.Equal(before.Profile, after.Profile);
        Assert.Equal(before.Mechanism, after.Mechanism);
        Assert.Equal(Frame.Point, after.Placement!.Value.Point);
        Assert.Equal(Frame.Normal, after.Placement.Value.Normal);
        Assert.Equal(Frame.Tangent, after.Placement.Value.Tangent);
        Assert.True(after.Placement.Value.LengthScale >= Frame.LengthScale);
        Assert.True(after.Placement.Value.WidthScale >= Frame.WidthScale);
    }

    [Fact]
    public void Jaw_upgrade_clears_only_the_incompatible_surface_frame()
    {
        var before = new SevereHeadWoundState(5, SevereHeadWoundKind.Scalp, 2, Frame, CombatWoundMechanism.Crush);
        var after = BrutalityHeadPolicy.Apply(before, 40, 35, SevereHeadWoundKind.JawLost, CombatWoundMechanism.Slash);
        Assert.Equal(SevereHeadWoundKind.JawLost, after.Kind);
        Assert.Equal(0, after.Profile);
        Assert.Null(after.Placement);
        Assert.Equal(before.Mechanism, after.Mechanism);
        Assert.True(after.IsValid);
    }

    [Theory]
    [InlineData(SevereHeadWoundKind.JawLost)]
    [InlineData(SevereHeadWoundKind.BrokenSkull)]
    public void Later_low_tier_hits_do_not_erase_or_downgrade_saved_severe_forms(SevereHeadWoundKind kind)
    {
        var before = new SevereHeadWoundState(7, kind, kind == SevereHeadWoundKind.JawLost ? 0 : 4,
            kind == SevereHeadWoundKind.JawLost ? null : Frame, CombatWoundMechanism.Thrust);
        Assert.Equal(before, BrutalityHeadPolicy.Apply(before, 0, 100, SevereHeadWoundKind.Scalp, CombatWoundMechanism.Crush));
        var after = BrutalityHeadPolicy.Apply(before, 20, 60, SevereHeadWoundKind.Scalp, CombatWoundMechanism.Crush);
        Assert.Equal(before with { HeavyHits = 8 }, after);
    }

    [Fact]
    public void Explicit_visual_fixture_can_select_its_form_without_inflating_the_real_footprint()
    {
        var state = FourHits(100, 30, SevereHeadWoundKind.BrokenSkull, fixture: true);
        Assert.Equal(SevereHeadWoundKind.BrokenSkull, state.Kind);
        Assert.Equal(Frame, state.Placement);
        Assert.Equal(SevereHeadWoundKind.Scalp, FourHits(100, 30, SevereHeadWoundKind.BrokenSkull).Kind);
    }

    [Theory]
    [InlineData(float.NaN, 100)]
    [InlineData(float.PositiveInfinity, 100)]
    [InlineData(100, float.NaN)]
    [InlineData(100, float.PositiveInfinity)]
    [InlineData(100, -1)]
    public void Invalid_inputs_leave_saved_state_unchanged(float level, float force)
    {
        var before = new SevereHeadWoundState(3);
        Assert.Equal(before, BrutalityHeadPolicy.Apply(before, level, force, SevereHeadWoundKind.JawLost, CombatWoundMechanism.Slash));
    }
}
