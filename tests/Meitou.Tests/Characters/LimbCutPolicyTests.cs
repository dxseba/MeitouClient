using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Wounds;

namespace Meitou.Tests.Characters;

public class LimbCutPolicyTests
{
    [Fact]
    public void Cuts_vary_continuously_inside_high_middle_and_low_strike_ranges()
    {
        foreach (int limb in Enumerable.Range(0, 4))
        {
            var high = Enumerable.Range(0, 200).Select(seed => LimbCutPolicy.FromHit(limb, 1, false, Vector2.UnitX, 0, (ulong)seed)).ToArray();
            var middle = Enumerable.Range(0, 200).Select(seed => LimbCutPolicy.FromHit(limb, 2, false, Vector2.UnitX, 0, (ulong)seed)).ToArray();
            var low = Enumerable.Range(0, 200).Select(seed => LimbCutPolicy.FromHit(limb, 9, true, Vector2.UnitX, 0, (ulong)seed)).ToArray();
            Assert.All(high.Concat(middle).Concat(low), cut => Assert.True(cut.IsValid));
            Assert.True(high.Max(c => c.Fraction) < middle.Min(c => c.Fraction));
            Assert.True(middle.Max(c => c.Fraction) < low.Min(c => c.Fraction));
            Assert.True(middle.Max(c => c.Fraction) - middle.Min(c => c.Fraction) > .2f);
            Assert.True(middle.Select(c => c.Slant).Distinct().Count() > 180);
            Assert.True(middle.Select(c => MathF.Round(c.Fraction, 3)).Distinct().Count() > 90);
        }
    }

    [Fact]
    public void A_pair_shares_its_strike_height_and_mirrored_slant_while_opposite_sweeps_reverse_tilt()
    {
        foreach (int first in new[] { 0, 2 })
        {
            var left = LimbCutPolicy.FromHit(first, 2, false, Vector2.UnitX, 0, 123);
            var right = LimbCutPolicy.FromHit(first + 1, 2, false, Vector2.UnitX, 0, 123);
            Assert.Equal(left.Fraction, right.Fraction);
            Assert.Equal(-left.Slant.X, right.Slant.X);
            Assert.Equal(left.Slant.Y, right.Slant.Y);
            var reverse = LimbCutPolicy.FromHit(first, 3, false, Vector2.UnitX, 0, 123);
            Assert.Equal(-left.Slant.X, reverse.Slant.X);
        }
    }

    [Fact]
    public void Travel_is_relative_to_the_victim_and_invalid_directions_cannot_create_nonfinite_cuts()
    {
        var front = LimbCutPolicy.FromHit(0, 1, false, Vector2.UnitX, 0, 45);
        var turned = LimbCutPolicy.FromHit(0, 1, false, Vector2.UnitX, MathF.PI, 45);
        Assert.InRange(MathF.Abs(front.Slant.X + turned.Slant.X), 0, 1e-6f);
        foreach (var travel in new[] { Vector2.Zero, new Vector2(float.NaN, 0), new Vector2(float.PositiveInfinity, 0) })
            Assert.True(LimbCutPolicy.FromHit(0, 1, false, travel, float.NaN, 12).IsValid);
    }

    [Fact]
    public void New_cuts_replace_only_their_limb_without_mutating_published_lists_and_affect_state_hash()
    {
        var owner = new CharacterCold();
        var empty = new StateHasher(); empty.Add(5);
        var same = empty; LimbCutPolicy.Hash(ref same, owner.LimbSeverances);
        Assert.Equal(empty.Value, same.Value);
        LimbCutPolicy.Note(owner, 0, 2, false, Vector2.UnitX, 0, 1);
        var published = owner.LimbSeverances;
        var original = Assert.Single(published);
        LimbCutPolicy.Note(owner, 1, 2, false, Vector2.UnitX, 0, 1);
        Assert.Equal(original, Assert.Single(published));
        LimbCutPolicy.Note(owner, 0, 3, false, Vector2.UnitX, 0, 2);
        Assert.Equal(2, owner.LimbSeverances.Count);
        Assert.Equal(original, Assert.Single(published));
        Assert.NotEqual(original, owner.LimbSeverances[0]);
        var changed = empty; LimbCutPolicy.Hash(ref changed, owner.LimbSeverances);
        Assert.NotEqual(empty.Value, changed.Value);
        Assert.Empty(new CharacterCold().LimbSeverances);
    }
}
