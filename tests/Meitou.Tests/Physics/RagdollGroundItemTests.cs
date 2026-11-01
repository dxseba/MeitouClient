using System.Numerics;
using Meitou.Data.Fcs;
using Meitou.Simulation.Items;

namespace Meitou.Tests.Physics;

public class RagdollGroundItemTests
{
    [Fact]
    public void Following_a_physical_limb_moves_the_same_loot_item_and_preserves_its_owner()
    {
        var items = new GroundItems();
        var item = new ItemInstance { Record = "severed-limb-test", Type = FcsRecordType.ITEM, Quantity = 1 };
        int id = items.Place(item, new(1, 2, 3), 0.5f, "limb-test-slot", 17);
        var shown = Assert.Single(items.Published());
        long before = items.Revision;
        var position = new Vector3(5, 0.75f, 8);
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.7f);
        items.Move(id, position, rotation);
        var moved = Assert.IsType<GroundItem>(items.Find(id));
        Assert.Same(item, moved.Item);
        Assert.Equal(id, moved.Id);
        Assert.Equal(17, moved.OwnedBy);
        Assert.Equal("limb-test-slot", moved.Slot);
        Assert.False(moved.Dropped);
        Assert.Equal(position, moved.Position);
        Assert.Equal(rotation, moved.Rotation);
        Assert.Equal(before + 1, items.Revision);
        Assert.Equal(new Vector3(1, 2, 3), shown.Position);
        Assert.Equal(position, Assert.Single(items.Published()).Position);
        items.Move(id, position, rotation);
        items.Move(id + 100, Vector3.Zero, Quaternion.Identity);
        Assert.Equal(before + 1, items.Revision);
        Assert.Same(item, Assert.IsType<GroundItem>(items.Take(id)).Item);
        Assert.Null(items.Find(id));
    }
}
