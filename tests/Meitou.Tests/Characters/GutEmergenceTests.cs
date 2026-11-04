using System.Numerics;
using Meitou.Game;
using Meitou.Rendering.Characters;

namespace Meitou.Tests.Characters;

public class GutEmergenceTests
{
    [Fact]
    public void Tissue_starts_inside_and_feeds_to_the_current_cut_face_before_release()
    {
        var normal = Vector3.Normalize(new Vector3(1, 2, -1));
        var contact = new CharacterCutContact(new Vector3(5, 12, -4), normal);
        var start = GutEmergence.Anchor(contact, 2, 0);
        Assert.InRange(Vector3.Dot(start - contact.Position, normal), -2.001f, -1.999f);
        var previous = start;
        for (int i = 1; i <= 15; i++)
        {
            var at = GutEmergence.Anchor(contact, 2, i / 30f);
            Assert.True(Vector3.Dot(at - previous, normal) >= 0);
            Assert.True(Vector3.Distance(at, previous) < 0.3f);
            previous = at;
        }
        Assert.InRange(Vector3.Dot(previous - contact.Position, normal), 0.0599f, 0.0601f);
        var moved = contact with { Position = contact.Position + new Vector3(3, -1, 2) };
        Assert.InRange(Vector3.Distance(previous + new Vector3(3, -1, 2),
            GutEmergence.Anchor(moved, 2, GutEmergence.FeedAge)), 0, 1e-5f);
        Assert.Equal(previous, GutEmergence.Anchor(contact, 2, GutEmergence.ReleaseAge));
    }
}
