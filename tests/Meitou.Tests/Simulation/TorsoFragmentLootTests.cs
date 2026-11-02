using System.Numerics;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Ai;
using Meitou.Data.Save;
using Meitou.Simulation;
using Meitou.Simulation.Carrying;
using Meitou.Simulation.Items;
using Meitou.Simulation.Saving;
using Meitou.Tests.Save;

namespace Meitou.Tests.Simulation;

public sealed class TorsoFragmentLootTests
{
    static void AddRation(TorsoCarryFixture rig, CharacterId body) =>
        Assert.True(rig.Cold(body).Inventory!.Add(new ItemInstance { Record = "70-t", Name = "Ration", Type = FcsRecordType.ITEM,
            Quantity = 1, StackLimit = 1, Width = 1, Height = 1, UnitWeight = 1 }));

    [Fact]
    public void Positive_take_all_chooses_the_looter_near_that_half_and_moves_the_shared_item_once()
    {
        using var rig = new TorsoCarryFixture();
        var nearNegative = rig.Add("Near negative", 1000);
        var nearPositive = rig.Add("Near positive", 100);
        var body = rig.Cut(negativeX: 1000, positiveX: 100);
        AddRation(rig, body);
        rig.World.Commands.Enqueue(new LootOrder([nearNegative, nearPositive], body) { Tick = rig.World.Tick, TorsoSide = 1 });
        rig.World.RunTicks(5);
        Assert.Equal("70-t", Assert.Single(rig.Cold(nearPositive).Inventory!.All()).Record);
        Assert.Empty(rig.Cold(nearNegative).Inventory!.All());
        Assert.Empty(rig.Cold(body).Inventory!.All());
        Assert.InRange(rig.Hot(nearPositive).Position.X, 99, 101);
        Assert.InRange(rig.Hot(body).Position.X, 999, 1001);
    }

    [Fact]
    public void Positive_inventory_window_uses_that_half_for_opening_and_closes_when_its_holder_moves_away()
    {
        using var rig = new TorsoCarryFixture();
        var looter = rig.Add("Looter", 100);
        var holder = rig.Add("Holder", 102);
        var body = rig.Cut(negativeX: 1000, positiveX: 100);
        AddRation(rig, body);
        rig.World.Commands.Enqueue(new OpenLootOrder([looter], body) { Tick = rig.World.Tick, TorsoSide = 1 });
        rig.World.RunTicks(3);
        Assert.Equal(body, rig.World.Player.OpenLoot);
        Assert.Equal(1, rig.World.Player.LootTorsoSide);
        float reach = rig.World.SystemOf<CarrySystem>()!.Settings.Reach;
        var looterAt = rig.Hot(looter).Position;
        var positiveAt = rig.Cold(body).TorsoPositivePosition!.Value;
        Assert.True(Vector2.Distance(new(looterAt.X, looterAt.Z), new(positiveAt.X, positiveAt.Z)) <= reach);
        Assert.True(Vector2.Distance(new(looterAt.X, looterAt.Z), new(rig.Hot(body).Position.X, rig.Hot(body).Position.Z)) > 10 * reach);
        rig.PickUp(holder, body, 1);
        Assert.Equal(body, rig.World.Player.OpenLoot);
        rig.World.Commands.Enqueue(new MoveOrder([holder], new(500, 300, 0)) { Tick = rig.World.Tick });
        for (int tick = 0; tick < 200 && !rig.World.Player.OpenLoot.IsNone; tick++) rig.World.RunTick();
        Assert.True(rig.World.Player.OpenLoot.IsNone);
        Assert.InRange(rig.Hot(body).Position.X, 999, 1001);
        Assert.Equal("70-t", Assert.Single(rig.Cold(body).Inventory!.All()).Record);
        Assert.Empty(rig.Cold(looter).Inventory!.All());
    }

    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 4)]
    public void Queued_positive_loot_and_open_window_preserve_their_target_side_through_native_save_load(bool open, int kind)
    {
        using var rig = new TorsoCarryFixture();
        var looter = rig.Add("Looter", 100);
        var body = rig.Cut(negativeX: 0, positiveX: 100);
        // A player corpse is always restored immediately, independently of population streaming.
        rig.Cold(body).IsPlayer = true;
        rig.Cold(body).Faction = rig.Data.PlayerFaction;
        rig.Cold(body).SquadId = 0;
        rig.World.Squads.Find(0)!.Members.Add(body);
        rig.World.Commands.Enqueue(new MoveOrder([looter], new(5000, 300, 0)) { Tick = rig.World.Tick });
        OrderCommand loot = open ? new OpenLootOrder([looter], body) { Tick = rig.World.Tick, Queued = true, TorsoSide = 1 }
            : new LootOrder([looter], body) { Tick = rig.World.Tick, Queued = true, TorsoSide = 1 };
        rig.World.Commands.Enqueue(loot);
        rig.World.RunTicks(2);
        var captured = SaveCapture.Capture(rig.World, rig.Data, new(1, 13, 0));
        var savedLooter = captured.Platoons.SelectMany(p => p.Characters).Single(c => c.Name == "Looter");
        Assert.Equal(kind, savedLooter.Ai!.Ints["meitou tjobK1"]);
        string root = SaveSamples.Temp("torso-positive-loot-order");
        string folder = Path.Combine(root, "slot");
        try
        {
            captured.Write(folder);
            using var restored = new TorsoCarryFixture();
            SaveLoader.Load(restored.World, SaveGame.Load(folder), restored.Data);
            restored.World.RunTicks(25);
            var restoredLooter = restored.Named("Looter");
            var restoredBody = restored.Named("Body");
            var queued = Assert.Single(restored.Cold(restoredLooter).OrderQueue, e => e.Row == TaskType.LOOT_TARGET);
            Assert.Equal(restoredBody, queued.Target);
            Assert.Equal(kind, queued.Owner.SaveKind(queued));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
