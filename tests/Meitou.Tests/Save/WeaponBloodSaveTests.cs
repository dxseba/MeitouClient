using System.Numerics;
using Meitou.Data.Fcs;
using Meitou.Data.Save;
using Meitou.Data.World;
using Meitou.Simulation;
using Meitou.Simulation.Items;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Saving;
using Meitou.Simulation.Trading;
using Meitou.Tests.Simulation;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Save;

public class WeaponBloodSaveTests
{
    const string Blade = "72-blood-test", Bag = "73-blood-test", ShopTemplate = "74-blood-test", RootId = "1-blood-inventory";
    static readonly Vector4 Red = new(.71f, .08f, .025f, .81f);
    static readonly Vector4 Green = new(.06f, .47f, .12f, .48f);

    [Fact]
    public void Native_inventory_bytes_keep_each_equipped_and_nested_items_blood()
    {
        var data = Data();
        var hip = data.Items.Create(Blade)!;
        hip.Section = "hip";
        hip.WeaponBlood = Red;
        var back = data.Items.Create(Blade)!;
        back.Section = "back";
        back.WeaponBlood = Green;
        var bag = NestedBag(data, Red);
        bag.X = 2;
        var inventory = new Inventory();
        inventory.Items.AddRange([hip.WithQuantity(1), back, bag]);

        var file = Native(SavedKit.FromInventory(inventory));
        var loaded = SavedKit.ToInventory(Read(file), data.Items);

        Assert.Equal(Red, loaded.Items.Single(i => i.Section == "hip").WeaponBlood);
        Assert.Equal(Green, loaded.Items.Single(i => i.Section == "back").WeaponBlood);
        Assert.Equal(Red, Assert.Single(loaded.Items.Single(i => i.Record == Bag).Contents!.Items).WeaponBlood);
        Assert.Equal(3, file.Records.Count(r => r.Vector4s.ContainsKey(SaveInventory.WeaponBloodKey)));
    }

    [Fact]
    public void Cleaning_a_loaded_item_removes_the_old_source_stain_from_native_bytes()
    {
        var data = Data();
        var file = Native([new SaveItem { BaseId = Blade, WeaponBlood = Red }]);
        var loaded = SavedKit.ToInventory(Read(file), data.Items);
        var item = Assert.Single(loaded.Items);
        string sourceId = item.Saved!.StringId;
        Assert.Equal(Red, item.Saved.Vector4s[SaveInventory.WeaponBloodKey]);
        item.Saved.Ints["unmodelled test value"] = 37;
        item.WeaponBlood = default;

        SaveInventory.Replace(file, Root(file), SavedKit.FromInventory(loaded), IdOf);
        file = RoundTrip(file);
        var saved = Assert.Single(Read(file));
        var clean = Assert.Single(SavedKit.ToInventory([saved], data.Items).Items);

        Assert.Equal(sourceId, saved.Source!.StringId);
        Assert.Equal(37, saved.Source.Ints["unmodelled test value"]);
        Assert.False(saved.Source.Vector4s.ContainsKey(SaveInventory.WeaponBloodKey));
        Assert.Equal(Vector4.Zero, clean.WeaponBlood);
    }

    [Fact]
    public void Old_native_items_without_the_extension_load_clean()
    {
        var file = Native([new SaveItem { BaseId = Blade }]);
        Assert.DoesNotContain(file.Records, r => r.Vector4s.ContainsKey(SaveInventory.WeaponBloodKey));

        var loaded = SavedKit.ToInventory(Read(file), Data().Items);

        Assert.Equal(Vector4.Zero, Assert.Single(loaded.Items).WeaponBlood);
    }

    [Fact]
    public void Native_extension_values_are_finite_clamped_and_clean_when_coverage_is_zero()
    {
        var file = Native([
            new SaveItem { BaseId = Blade },
            new SaveItem { BaseId = Blade, X = 1 },
            new SaveItem { BaseId = Blade, X = 2 },
        ]);
        var records = Read(file).Select(i => i.Source!).ToArray();
        records[0].Vector4s[SaveInventory.WeaponBloodKey] = new(float.NaN, .1f, .2f, .5f);
        records[1].Vector4s[SaveInventory.WeaponBloodKey] = new(-1, .25f, 2, 3);
        records[2].Vector4s[SaveInventory.WeaponBloodKey] = new(.8f, .1f, .2f, 0);

        var loaded = SavedKit.ToInventory(Read(RoundTrip(file)), Data().Items);

        Assert.Equal([Vector4.Zero, new Vector4(0, .25f, 1, 1), Vector4.Zero], loaded.Items.Select(i => i.WeaponBlood));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Picking_up_changing_blood_and_redropping_in_the_same_zone_updates_native_bytes(bool clean)
    {
        var data = Data();
        Vector4 before = clean ? Red : Vector4.Zero, after = clean ? Vector4.Zero : Red;
        var position = SyntheticTown.Centre;
        var coordinate = WorldLayout.ZoneOf(position.X, position.Z);
        var zone = new SaveZone(coordinate, new SaveFile());
        SaveGroundItems.Write(zone, [(position, Quaternion.Identity, new SaveItem { BaseId = Blade, WeaponBlood = before })], []);
        zone = new SaveZone(coordinate, RoundTrip(zone.File));
        var game = new SaveBuilder(83).Game;
        game.Zones[coordinate] = zone;
        string originalId = Assert.Single(SaveGroundItems.Read(zone)).Record.StringId;
        using var world = World();
        Assert.Equal(0, GroundItemSave.Load(game, world, data.Items));
        var onGround = Assert.Single(world.GroundItems.All);
        var pickedUp = world.GroundItems.Take(onGround.Id)!;
        Assert.Equal(before, pickedUp.Item.WeaponBlood);
        pickedUp.Item.WeaponBlood = after;
        world.GroundItems.Drop(pickedUp.Item, position, 0);

        GroundItemSave.Write(game, world, data.Items);
        var written = new SaveZone(coordinate, RoundTrip(game.Zones[coordinate].File));
        var saved = Assert.Single(SaveGroundItems.Read(written));
        using var restored = World();
        game.Zones[coordinate] = written;
        Assert.Equal(0, GroundItemSave.Load(game, restored, data.Items));

        Assert.Equal(originalId, saved.Record.StringId);
        Assert.Equal(after, saved.Item.WeaponBlood);
        Assert.Equal(after, Assert.Single(restored.GroundItems.All).Item.WeaponBlood);
        Assert.Equal(!clean, saved.Record.Vector4s.ContainsKey(SaveInventory.WeaponBloodKey));
    }

    [Fact]
    public void Chest_json_keeps_blood_on_weapons_inside_bags()
    {
        var data = Data();
        var chest = new Inventory(4, 4);
        Assert.True(chest.AddTo("main", NestedBag(data, Red)));

        string text = ContainerSave.Write(chest)!;
        var loaded = new Inventory(4, 4);
        ContainerSave.Read(text, loaded, data.Items);

        Assert.Equal(Red, Assert.Single(Assert.Single(loaded.Items).Contents!.Items).WeaponBlood);
        Assert.Equal(Vector4.Zero, Assert.Single(loaded.Items).WeaponBlood);
    }

    [Fact]
    public void Legacy_chest_json_without_blood_loads_clean_and_clean_writes_omit_it()
    {
        const string legacy = """[{"R":"72-blood-test","Q":1,"X":0,"Y":0,"Ql":0,"Lv":0,"M":"","C":"","Ch":1,"S":"main","I":null}]""";
        var loaded = new Inventory(4, 4);
        ContainerSave.Read(legacy, loaded, Data().Items);

        Assert.Equal(Vector4.Zero, Assert.Single(loaded.Items).WeaponBlood);
        Assert.DoesNotContain("\"B\":", ContainerSave.Write(loaded)!);
    }

    [Fact]
    public void Shop_stock_keeps_nested_weapon_blood_through_platoon_bytes()
    {
        var data = Data();
        using var world = World();
        var shop = new Shop { Terms = new Trader(0, 0), Fills = 3, NextRestockTick = 500 };
        Assert.True(shop.Stock.AddTo("main", NestedBag(data, Green)));
        var platoon = SyntheticTown.Rec("1-blood-platoon", FcsRecordType.PLATOON, "Trader");
        ShopSave.Write(platoon, shop, world);
        var file = new SaveFile();
        file.Records.Add(platoon);
        var saved = Assert.Single(RoundTrip(file).Records);

        var loaded = ShopSave.Read(world, new Squad { Id = 1, TemplateId = ShopTemplate, Town = 0, Faction = 0 }, saved, data);

        Assert.NotNull(loaded);
        Assert.Equal(3, loaded.Fills);
        Assert.Equal(Green, Assert.Single(Assert.Single(loaded.Stock.Items).Contents!.Items).WeaponBlood);
    }

    static ItemInstance NestedBag(PopulationData data, Vector4 blood)
    {
        var bag = data.Items.Create(Bag)!;
        var blade = data.Items.Create(Blade)!;
        blade.WeaponBlood = blood;
        Assert.True(bag.Contents!.AddTo("backpack_content", blade));
        return bag;
    }

    static PopulationData Data()
    {
        var blade = SyntheticTown.Rec(Blade, FcsRecordType.WEAPON, "Test blade");
        var bag = SyntheticTown.Rec(Bag, FcsRecordType.CONTAINER, "Test bag");
        bag.Ints["storage size width"] = bag.Ints["storage size height"] = 4;
        var vendor = SyntheticTown.Rec("75-blood-test", FcsRecordType.VENDOR_LIST, "Test stock");
        var shop = SyntheticTown.Rec(ShopTemplate, FcsRecordType.SQUAD_TEMPLATE, "Test shop");
        shop.Bools["is trader"] = true;
        shop.References["vendors"] = [SyntheticTown.Ref(vendor.StringId, 100)];
        return SyntheticTown.Data(SyntheticTown.Database(extra: [blade, bag, vendor, shop]));
    }

    static SimWorld World() => new(new WorldSettings { Seed = 83, PublishSnapshots = false }, new OpenGroundWalkability(SyntheticTown.Ground), []);
    static SaveFile RoundTrip(SaveFile file) => SaveFile.Read(file.ToBytes());
    static string IdOf(int number) => $"{number}-blood-item";
    static FcsRecord Root(SaveFile file) => file.Records.Single(r => r.StringId == RootId);
    static List<SaveItem> Read(SaveFile file) => SaveInventory.Read(file, Root(file));

    static SaveFile Native(IReadOnlyList<SaveItem> items)
    {
        var file = new SaveFile();
        var inventory = SyntheticTown.Rec(RootId, FcsRecordType.INVENTORY_STATE, "Inventory");
        inventory.Id = 1;
        file.Records.Add(inventory);
        SaveInventory.Replace(file, inventory, items, IdOf);
        return RoundTrip(file);
    }
}
