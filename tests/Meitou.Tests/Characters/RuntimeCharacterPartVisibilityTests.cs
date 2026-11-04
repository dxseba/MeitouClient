using Meitou.Data.Characters;
using Meitou.Rendering.Characters;

namespace Meitou.Tests.Characters;

public class RuntimeCharacterPartVisibilityTests
{
    [Theory]
    [InlineData(179.999f, false)]
    [InlineData(180, true)]
    [InlineData(180.001f, true)]
    public void Projected_size_boundary_applies_to_world_parts_and_portraits_keep_their_own_choice(float pixels, bool shown)
    {
        var part = new AssetPart
        {
            Label = "generated", Mesh = null!, Shared = true, Material = new(), BoundsCentre = default, BoundsRadius = 1,
            MinimumScreenPixels = 180, ShowInPortrait = false,
        };
        Assert.Equal(shown, part.ShownAt(pixels));
        Assert.False(part.ShownAt(pixels, portrait: true));
        part.ShowInPortrait = true;
        Assert.True(part.ShownAt(0, portrait: true));
        part.MinimumScreenPixels = 0;
        Assert.True(part.ShownAt(0));
        Assert.True(part.ShownAt(float.NaN));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void A_runtime_part_rejects_invalid_screen_thresholds(float invalid)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeCharacterPart(null!) { MinimumScreenPixels = invalid });
        Assert.Equal(0, new RuntimeCharacterPart(null!).MinimumScreenPixels);
    }
}
