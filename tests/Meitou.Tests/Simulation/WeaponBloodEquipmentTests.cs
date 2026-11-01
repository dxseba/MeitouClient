using System.Numerics;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;
using Meitou.Simulation;
using Meitou.Simulation.Items;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Simulation;

public class WeaponBloodEquipmentTests
{
    const string BladeId = "blood-test-blade";
    static readonly Vector4 Stain = new(0.5f, 0.02f, 0.01f, 0.65f);

    static CharacterAssembly Assembly()
    {
        var file = new FcsFile();
        var blade = new FcsRecord { StringId = BladeId, RecordType = FcsRecordType.WEAPON, Name = "Blade", Flags = 0x10 };
        blade.Ints["skill category"] = 0;
        blade.Floats["cut damage multiplier"] = 1;
        file.Records.Add(blade);
        var db = new GameDatabase();
        db.Apply(file, "weapon-blood-test.mod");
        return new(new PopulationData { Db = db, Sites = [], Factions = [] });
    }

    static ItemInstance Blade(string section, Vector4 blood = default) => new()
    {
        Record = BladeId, Type = FcsRecordType.WEAPON, Section = section, WeaponBlood = blood,
    };

    static CharacterCold Owner(params ItemInstance[] blades)
    {
        var inventory = new Inventory(8, 3);
        inventory.Items.AddRange(blades);
        return new CharacterCold { Name = "Owner", Inventory = inventory };
    }

    static InventoryItemToken Token(Inventory owner, ItemInstance item) =>
        new(Array.IndexOf(owner.All().ToArray(), item), item.Record, item.Section, item.X, item.Y, item.Quantity, item.Quality);

    static CharacterId Spawn(SimWorld world, CharacterCold owner, float x = 0) =>
        world.Characters.Spawn(new CharacterHot { Position = new(x, 0, 0), Health = 100 }, owner, 0);

    static CharacterSnapshot Snapshot(SimWorld world, CharacterId id) => Assert.Single(world.Snapshot.Characters, c => c.Id == id);

    [Fact]
    public void Swapping_identical_blades_between_slots_moves_only_their_own_stain_and_keeps_the_shown_snapshot_pair_unchanged()
    {
        var assembly = Assembly();
        var stained = Blade("back", Stain);
        var clean = Blade("hip");
        var owner = Owner(stained, clean);
        assembly.RefreshEquipment(owner);
        Assert.Same(stained, owner.Fighter!.BackWeapon!.SourceItem);
        Assert.Same(clean, owner.Fighter.HipWeapon!.SourceItem);
        using var world = new SimWorld(new WorldSettings(), new OpenGroundWalkability((_, _) => 0));
        var id = Spawn(world, owner);
        world.RunTick();
        var before = Snapshot(world, id);
        Assert.Equal(Stain, before.BackWeaponBlood);
        Assert.Equal(Vector4.Zero, before.HipWeaponBlood);

        var inventory = owner.Inventory!;
        Assert.True(InventoryCommands.Move(inventory, Token(inventory, stained), "main", 0, 0));
        Assert.True(InventoryCommands.Move(inventory, Token(inventory, clean), "back", 0, 0));
        Assert.True(InventoryCommands.Move(inventory, Token(inventory, stained), "hip", 0, 0));
        assembly.RefreshEquipment(owner);
        Assert.Same(clean, owner.Fighter!.Weapon!.SourceItem);
        Assert.Same(stained, owner.Fighter.HipWeapon!.SourceItem);
        owner.DrawnWeapon = WeaponKinds.Katana;
        world.RunTick();
        var drawn = Snapshot(world, id);
        Assert.True(drawn.WeaponDrawn);
        Assert.True(drawn.BackDrawn);
        Assert.Equal(Vector4.Zero, drawn.BackWeaponBlood);
        Assert.Equal(Stain, drawn.HipWeaponBlood);
        Assert.Same(before, Assert.Single(world.PreviousSnapshot.Characters, c => c.Id == id));
        Assert.Equal(Stain, before.BackWeaponBlood);
        Assert.Equal(Vector4.Zero, before.HipWeaponBlood);

        owner.Fighter.SetArms(right: true, left: false);
        world.RunTick();
        Assert.False(Snapshot(world, id).BackDrawn);
        Assert.Same(stained, owner.Fighter.Weapon!.SourceItem);
        owner.DrawnWeapon = WeaponKinds.None;
        world.RunTick();
        var sheathed = Snapshot(world, id);
        Assert.False(sheathed.WeaponDrawn);
        Assert.Equal(Stain, sheathed.HipWeaponBlood);
        Assert.Equal(Vector4.Zero, sheathed.BackWeaponBlood);
        Assert.Equal(Vector4.Zero, clean.WeaponBlood);
    }

    [Fact]
    public void Looting_a_stained_blade_moves_its_blood_to_the_new_owner_without_staining_an_identical_spare()
    {
        var assembly = Assembly();
        var stained = Blade("back", Stain);
        var spare = Blade("hip");
        var donor = Owner(stained);
        var recipient = Owner(spare);
        assembly.RefreshEquipment(donor);
        assembly.RefreshEquipment(recipient);
        using var world = new SimWorld(new WorldSettings(), new OpenGroundWalkability((_, _) => 0));
        var donorId = Spawn(world, donor);
        var recipientId = Spawn(world, recipient, 20);
        world.RunTick();
        var before = Snapshot(world, donorId);
        Assert.Equal(Stain, before.BackWeaponBlood);
        Assert.Equal(Vector4.Zero, Snapshot(world, recipientId).HipWeaponBlood);

        Assert.True(InventoryCommands.Transfer(donor.Inventory!, Token(donor.Inventory!, stained), recipient.Inventory!, "back", 0, 0));
        assembly.RefreshEquipment(donor);
        assembly.RefreshEquipment(recipient);
        world.RunTick();
        Assert.Empty(donor.Inventory!.Items);
        Assert.Equal(Vector4.Zero, Snapshot(world, donorId).BackWeaponBlood);
        Assert.Equal(Vector4.Zero, Snapshot(world, donorId).HipWeaponBlood);
        Assert.Same(stained, recipient.Fighter!.Weapon!.SourceItem);
        Assert.Equal(Stain, Snapshot(world, recipientId).BackWeaponBlood);
        Assert.Equal(Vector4.Zero, Snapshot(world, recipientId).HipWeaponBlood);
        Assert.Equal(Vector4.Zero, spare.WeaponBlood);
        Assert.Equal(Stain, before.BackWeaponBlood);
    }
}
