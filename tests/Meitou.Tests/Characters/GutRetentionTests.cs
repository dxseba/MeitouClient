using Meitou.Game;
using Meitou.Simulation;

namespace Meitou.Tests.Characters;

public class GutRetentionTests
{
    [Fact]
    public void Most_cuts_keep_their_tissue_with_a_stable_visual_choice()
    {
        int retained = 0;
        for (int slot = 0; slot < 500; slot++)
        {
            var id = new CharacterId(slot, 1);
            bool keep = GutEmergence.KeepAttached(id);
            Assert.Equal(keep, GutEmergence.KeepAttached(id));
            if (keep) retained++;
        }
        Assert.InRange(retained, 375, 425);
    }

    [Fact]
    public void Retained_tissue_does_not_expire_and_loose_spills_release_only_after_emerging()
    {
        Assert.True(GutEmergence.AttachedAt(true, 600));
        Assert.True(GutEmergence.AttachedAt(false, GutEmergence.FeedAge));
        Assert.True(GutEmergence.AttachedAt(false, 2.9f));
        Assert.False(GutEmergence.AttachedAt(false, GutEmergence.ReleaseAge));
        Assert.False(GutEmergence.AttachedAt(false, 600));
    }
}
