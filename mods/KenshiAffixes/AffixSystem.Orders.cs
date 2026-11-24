using System.Globalization;
using Meitou.Simulation;
using Meitou.Simulation.Items;
using Meitou.Simulation.Locks;

namespace Meitou.KenshiAffixes;

/// <summary>
/// The interface's orders, applied in the serial slow step: the original's test button (a character's equipped gear rolled once at no less than
/// the test level, fixed profiles aside, the player's gear too) and a container item rolled as loot on its first tooltip (the original's
/// <c>ensureContainerItem</c> on the tooltip: a non-player container's unrolled gear, at the loot level).
/// </summary>
sealed partial class AffixSystem
{
    readonly List<SimCommand> _orders = [];

    void Orders(World world)
    {
        foreach (var order in _orders)
        {
            if (!settings.Enabled || combat is null) continue;

            if (order is TestRollOrder test && settings.TestButton) TestRoll(world, test.Character);
            else if (order is InspectLootOrder loot && settings.AutoContainers) InspectLoot(world, loot);
        }

        _orders.Clear();
    }

    void TestRoll(World world, CharacterId id)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(id, out int slot) || table.Cold(slot) is not { Inventory: { } inventory, Stats: { } stats } cold) return;

        float level = Math.Max(Rolling.NpcLevel(stats), settings.TestMinimumLevel);
        int rolled = 0;
        for (int i = 0; i < inventory.Items.Count; i++)
        {
            var item = inventory.Items[i];
            if (!Rollable(item) || !Wearing.Equipped(item) || AffixTag.Rolled(item.ModTag) || Meitou(world, item)) continue;
            if (Rolling.BaselineOf(item, combat!) is not { } baseline) continue;

            Roll(world, item, baseline, level, Rng.Key(id) ^ (ulong)i << 40 ^ StableHash(item.Record) << 8);
            AffixTag.MarkTest(item);
            rolled++;
        }

        string text = string.Create(CultureInfo.InvariantCulture, $"Rolled {rolled} items (once)");
        world.Post($"{cold.Name}: {text}");
        log("TEST " + text);
    }

    void InspectLoot(World world, InspectLootOrder order)
    {
        if (world.Fixtures.Find(order.Site) is not { Kind: FixtureKind.Container, Contents: { } contents } fixture) return;
        if (fixture.Owner == world.Player.Faction && world.Player.Faction >= 0) return;
        if (order.Index < 0 || order.Index >= contents.Items.Count) return;

        var item = contents.Items[order.Index];
        if (item.Record != order.Record || !Rollable(item) || AffixTag.Rolled(item.ModTag) || AffixTag.Plain(item.ModTag)) return;
        if (Meitou(world, item) || Rolling.BaselineOf(item, combat!) is not { } baseline) return;

        int quality = Rolling.ShopLevel(Rolling.Band(item), settings.ShopBaseLevel, settings.ShopLevelPerBand);
        Roll(world, item, baseline, LootLevel(quality), StableHash(order.Site) ^ (ulong)order.Index << 40 ^ StableHash(item.Record) << 8);
    }
}
