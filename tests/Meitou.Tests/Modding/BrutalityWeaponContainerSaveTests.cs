using System.Numerics;
using System.Text.Json.Nodes;
using HarmonyLib;
using Meitou.Brutality;
using Meitou.Data.Fcs;
using Meitou.Data.Save;
using Meitou.Simulation;
using Meitou.Simulation.Items;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Saving;
using Meitou.Simulation.Trading;
using Meitou.Tests.Combat;
using Meitou.Tests.Simulation;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Modding;

[Collection("Brutality native combat")]
public sealed class BrutalityWeaponContainerSaveTests
{
    sealed class Patches : IDisposable
    {
        readonly Harmony harmony = new("brutality.container-state." + Guid.NewGuid().ToString("N"));
        public Patches() => CutEquipmentAdapter.Install(harmony);
        public void Dispose() => harmony.UnpatchAll(harmony.Id);
    }
    static PopulationData Data()
    {
        var weapon = SyntheticTown.Rec("brutality-blade", FcsRecordType.WEAPON, "identical blade");
        var bag = SyntheticTown.Rec("brutality-bag", FcsRecordType.CONTAINER, "bag");
        bag.Ints["storage size width"] = 4; bag.Ints["storage size height"] = 4;
        return SyntheticTown.Data(SyntheticTown.Database(anatomy: true, extra: [weapon, bag], changePatrol: patrol =>
        { patrol.Bools["is trader"] = true; patrol.References["vendors"] = [new("unused-vendor", 1, 0, 0)]; }));
    }
    static SimWorld World(WeaponBloodState state) => new(new WorldSettings { Seed = 19, PublishSnapshots = false }, new OpenGroundWalkability((_, _) => 0), [state]);
    static byte[] Write(WeaponBloodState state) { using var stream = new MemoryStream(); state.Write(stream); return stream.ToArray(); }
    static void Stain(WeaponBloodState state, ItemInstance item, uint colour, float cut) => Assert.True(state.Deposit(item, new() { BloodColour = colour }, new(cut, 0, 0, 1), true, true));
    static string Token(JsonNode row) => row[CutEquipmentAdapter.ContainerTokenProperty]!.GetValue<string>();
    static void Blood(WeaponBloodState state, ItemInstance item, Vector4 expected) { Assert.True(state.TryGet(item, out var stain)); Assert.Equal(expected, stain.Blood); }
    static JsonArray Reversed(JsonArray entries) => new(entries.Reverse().Select(entry => entry!.DeepClone()).ToArray());

    [Fact]
    public void Identical_weapons_follow_their_embedded_identity_after_native_container_reordering_and_auto_placement()
    {
        using var patches = new Patches(); var data = Data(); var state = new WeaponBloodState();
        var first = data.Items.Create("brutality-blade")!; var second = data.Items.Create("brutality-blade")!;
        var chest = new Inventory(5, 5); chest.Items.AddRange([first, second]);
        Stain(state, first, 0xFF0000, 10); Stain(state, second, 0x00FF00, 30);
        var entries = JsonNode.Parse(ContainerSave.Write(chest)!)!.AsArray(); string redKey = Token(entries[0]!), greenKey = Token(entries[1]!);
        Assert.NotEqual(redKey, greenKey); Assert.True(WeaponBloodState.IsContainerToken(redKey));
        var reversed = Reversed(entries);
        foreach (var row in reversed) { row!["X"] = 0; row["Y"] = 0; }
        var loaded = new Inventory(5, 5); ContainerSave.Read(reversed.ToJsonString(), loaded, data.Items);
        var restored = new WeaponBloodState(); restored.Read(new MemoryStream(Write(state)));
        Assert.Equal(2, loaded.Items.Count);
        Blood(restored, loaded.Items.Single(item => CutEquipmentAdapter.ItemKey(item) == redKey), new(1, 0, 0, .1f));
        Blood(restored, loaded.Items.Single(item => CutEquipmentAdapter.ItemKey(item) == greenKey), new(0, 1, 0, .3f));
        Assert.Equal(2, loaded.Items.Select(item => (item.X, item.Y)).Distinct().Count());
        Assert.False(CutEquipmentAdapter.HasContainerReadScope);
        Assert.Equal(Write(state), Write(restored));
    }

    [Fact]
    public void Nested_bags_and_skipped_unknown_entries_do_not_shift_the_following_items_identity()
    {
        using var patches = new Patches(); var data = Data(); var state = new WeaponBloodState();
        var bag = data.Items.Create("brutality-bag")!; var inner = data.Items.Create("brutality-blade")!; var outside = data.Items.Create("brutality-blade")!;
        bag.Contents!.Items.Add(inner); outside.X = 1; var chest = new Inventory(5, 5); chest.Items.AddRange([bag, outside]);
        Stain(state, inner, 0xFF0000, 25); Stain(state, outside, 0x00FF00, 50);
        var entries = JsonNode.Parse(ContainerSave.Write(chest)!)!.AsArray();
        var missing = entries[0]!.DeepClone(); missing["R"] = "missing-bag";
        // Native ItemOf skips the entire unknown bag, including its children. Its token must never leak to the next native item.
        entries.Insert(0, missing);
        var loaded = new Inventory(5, 5); ContainerSave.Read(entries.ToJsonString(), loaded, data.Items);
        var restored = new WeaponBloodState(); restored.Read(new MemoryStream(Write(state)));
        Assert.Equal(2, loaded.Items.Count);
        Blood(restored, loaded.Items.Single(item => item.Record == "brutality-bag").Contents!.Items.Single(), new(1, 0, 0, .25f));
        Blood(restored, loaded.Items.Single(item => item.Record == "brutality-blade"), new(0, 1, 0, .5f));
        Assert.False(CutEquipmentAdapter.HasContainerReadScope);
    }

    [Fact]
    public void Real_native_save_capture_writes_shop_tokens_and_the_native_shop_reader_restores_both_actual_weapons()
    {
        using var patches = new Patches(); var data = Data(); var blood = new WeaponBloodState(); using var world = World(blood);
        var shop = new Shop { Terms = new(0, data.FactionIndex("20-t")) };
        var first = data.Items.Create("brutality-blade")!; var second = data.Items.Create("brutality-blade")!;
        second.X = 1; shop.Stock.Items.AddRange([first, second]); Stain(blood, first, 0xFF0000, 10); Stain(blood, second, 0x00FF00, 30);
        var squad = new Squad { Id = world.Squads.NextId(), TemplateId = "10-t", Town = 0, Faction = data.FactionIndex("20-t"), Shop = shop };
        world.Squads.Add(squad);
        var cold = new Combatant().MakeCold(); cold.RecordId = "2-t"; cold.Faction = squad.Faction; cold.SquadId = squad.Id;
        var id = world.Characters.Spawn(new() { Position = new(1000, 0, 2000), Health = 100 }, cold, 0); squad.Members.Add(id); squad.Leader = id;
        var save = SaveCapture.Capture(world, data, new(1, 12, 0)); var bytes = Write(blood);
        var saved = Assert.Single(save.Platoons, platoon => platoon.Record.Strings.ContainsKey(ShopSave.StockKey));
        var rows = JsonNode.Parse(saved.Record.Strings[ShopSave.StockKey])!.AsArray(); Assert.NotEqual(Token(rows[0]!), Token(rows[1]!));
        saved.Record.Strings[ShopSave.StockKey] = Reversed(rows).ToJsonString();
        var restored = new WeaponBloodState(); using var loadedWorld = World(restored); restored.Read(new MemoryStream(bytes));
        var deferred = Write(restored); Assert.Equal(bytes, deferred); // No shop instances have been materialized yet.
        var loaded = ShopSave.Read(loadedWorld, new Squad { Id = 1, TemplateId = "10-t", Town = 0, Faction = squad.Faction }, saved.Record, data);
        Assert.NotNull(loaded); Assert.Equal(2, loaded.Stock.Items.Count);
        Blood(restored, loaded.Stock.Items.Single(item => CutEquipmentAdapter.ItemKey(item) == Token(rows[0]!)), new(1, 0, 0, .1f));
        Blood(restored, loaded.Stock.Items.Single(item => CutEquipmentAdapter.ItemKey(item) == Token(rows[1]!)), new(0, 1, 0, .3f));
        Assert.Equal(bytes, Write(restored)); Assert.False(CutEquipmentAdapter.HasContainerReadScope);
    }

    [Fact]
    public void A_container_item_rekeys_to_its_actual_native_inventory_record_and_gets_a_new_token_when_stored_again()
    {
        using var patches = new Patches(); var data = Data(); var blood = new WeaponBloodState(); using var world = World(blood);
        var item = data.Items.Create("brutality-blade")!; var chest = new Inventory(); chest.Items.Add(item); Stain(blood, item, 0xFF0000, 20);
        string original = Token(JsonNode.Parse(ContainerSave.Write(chest)!)![0]!);
        world.Player.Faction = data.PlayerFaction;
        var squad = new Squad { Id = world.Squads.NextId(), TemplateId = "10-t", Faction = data.PlayerFaction }; world.Squads.Add(squad); world.Player.Squad = squad.Id;
        var cold = new Combatant().MakeCold(); cold.RecordId = "2-t"; cold.IsPlayer = true; cold.Faction = data.PlayerFaction; cold.SquadId = squad.Id;
        cold.Inventory = new(); chest.Items.Remove(item); item.Section = "hip"; cold.Inventory.Items.Add(item);
        var id = world.Characters.Spawn(new() { Position = new(10, 0, 10), Health = 100 }, cold, 0); squad.Members.Add(id); squad.Leader = id;
        SaveCapture.Capture(world, data, new(1, 12, 0));
        Assert.StartsWith("item-record:v1:", CutEquipmentAdapter.ItemKey(item));
        Assert.DoesNotContain(original, System.Text.Encoding.UTF8.GetString(Write(blood)));
        cold.Inventory.Items.Remove(item); item.Section = "main"; chest.Items.Add(item);
        string again = Token(JsonNode.Parse(ContainerSave.Write(chest)!)![0]!); Assert.NotEqual(original, again);
        Blood(blood, item, new(1, 0, 0, .2f));
    }

    [Fact]
    public void Restored_allocators_do_not_reuse_pending_tokens_and_a_missing_sidecar_cannot_alias_an_old_container()
    {
        using var patches = new Patches(); var data = Data(); var original = new WeaponBloodState();
        using var originalWorld = World(original); original.Schedule(originalWorld);
        var a = data.Items.Create("brutality-blade")!; var chest = new Inventory(); chest.Items.Add(a); Stain(original, a, 0xFF0000, 20);
        string oldText = ContainerSave.Write(chest)!; string oldKey = Token(JsonNode.Parse(oldText)![0]!); var bytes = Write(original);
        var restored = new WeaponBloodState(); restored.Read(new MemoryStream(bytes));
        var b = data.Items.Create("brutality-blade")!; var next = new Inventory(); next.Items.Add(b); Stain(restored, b, 0x00FF00, 40);
        string nextKey = Token(JsonNode.Parse(ContainerSave.Write(next)!)![0]!); Assert.NotEqual(oldKey, nextKey);
        Assert.Contains(oldKey, System.Text.Encoding.UTF8.GetString(Write(restored))); // The old chest has not materialized yet.
        var missingSidecar = new WeaponBloodState();
        missingSidecar.ReserveContainerNamespaces(oldText); // The native load scans tokens before creating a replacement namespace.
        using var missingWorld = World(missingSidecar); missingSidecar.Schedule(missingWorld);
        var c = data.Items.Create("brutality-blade")!; var fresh = new Inventory(); fresh.Items.Add(c);
        Stain(missingSidecar, c, 0x00FF00, 40); string freshKey = Token(JsonNode.Parse(ContainerSave.Write(fresh)!)![0]!);
        Assert.NotEqual(oldKey, freshKey);
        var loadedOld = new Inventory(); ContainerSave.Read(oldText, loadedOld, data.Items);
        Assert.False(missingSidecar.TryGet(Assert.Single(loadedOld.Items), out _));
        Blood(restored, Assert.Single(loadedOld.Items), new(1, 0, 0, .2f));
    }
    [Fact]
    public void The_native_reader_ignores_tokens_without_the_mod_and_reader_scopes_are_cleared_after_an_exception()
    {
        var data = Data(); var state = new WeaponBloodState(); var chest = new Inventory(); var item = data.Items.Create("brutality-blade")!;
        chest.Items.Add(item); Stain(state, item, 0xFF0000, 20); string text;
        using (new Patches())
        {
            text = ContainerSave.Write(chest)!;
            Assert.Throws<NullReferenceException>(() => ContainerSave.Read(text, new Inventory(), null!));
            Assert.False(CutEquipmentAdapter.HasContainerReadScope);
        }
        var native = new Inventory(); ContainerSave.Read(text, native, data.Items);
        Assert.Equal("brutality-blade", Assert.Single(native.Items).Record);
        Assert.DoesNotContain(CutEquipmentAdapter.ContainerTokenProperty, ContainerSave.Write(native)!);
    }
}
