using System.Numerics;
using HarmonyLib;
using Meitou.Brutality;
using Meitou.Brutality.Physics;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Save;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Items;
using Meitou.Simulation.Saving;
using Meitou.Tests.Combat;
using Meitou.Tests.Simulation;
using static Meitou.Tests.Bodies.BodyFixtures;
using static Meitou.Tests.Combat.CombatFixtures;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Modding;

[Collection("Brutality native combat")]
public sealed class BrutalityWeaponStateTests
{
    sealed class Patches : IDisposable
    {
        readonly Harmony harmony = new("brutality.weapon-state." + Guid.NewGuid().ToString("N"));
        public Patches() { CombatAdapter.Install(harmony); CutEquipmentAdapter.Install(harmony); }
        public void Dispose() => harmony.UnpatchAll(harmony.Id);
    }
    static ItemInstance Weapon(string section = "hip") => new() { Record = "brutality-katana", Type = FcsRecordType.WEAPON, Section = section, Quality = 81, Level = 63, MaterialId = "model", CompanyId = "maker" };
    static BrutalityGameplaySystem Gameplay(CombatSystem? combat = null) => new(combat, BaseGame, BodyOptions.Default, (RngPurpose)0xB17A1171);
    static SimWorld World(params ITickSystem[] systems) => new(new WorldSettings { Seed = 13, PublishSnapshots = false }, new OpenGroundWalkability((_, _) => 0), systems);
    static byte[] Write(WeaponBloodState state) { using var stream = new MemoryStream(); state.Write(stream); return stream.ToArray(); }
    static RaceData Red => new() { BloodColour = 0xFF0000 };
    static RaceData Green => new() { BloodColour = 0x00FF00 };

    [Fact]
    public void Blood_uses_actual_opening_scale_and_race_colour_then_mixes_by_deposited_amount()
    {
        var state = new WeaponBloodState(); var item = Weapon();
        Assert.True(state.Deposit(item, Red, new(20, 50, 0, Scale: .5f), true, true, CombatWoundMechanism.Slash));
        Assert.True(state.TryGet(item, out var first)); Assert.Equal(new(1, 0, 0, .1f), first.Blood);
        Assert.True(state.Deposit(item, Green, new(30, 0, 0, 1), true, true, CombatWoundMechanism.Slash));
        Assert.True(state.TryGet(item, out var mixed));
        Assert.InRange(mixed.Blood.X, .24999f, .25001f); Assert.InRange(mixed.Blood.Y, .74999f, .75001f);
        Assert.Equal(0, mixed.Blood.Z); Assert.Equal(.4f, mixed.Blood.W); Assert.True(mixed.Revision > first.Revision);
        Assert.Equal(new(1, 0, 0, .1f), first.Blood); // Published values remain stable after subsequent contacts.
    }

    [Fact]
    public void Crushing_opens_tissue_but_incidental_blunt_in_a_slash_does_not()
    {
        var state = new WeaponBloodState(); var item = Weapon();
        Assert.False(state.Deposit(item, Red, new(0, 100, 0, 1), true, true, CombatWoundMechanism.Slash));
        Assert.True(state.Deposit(item, Red, new(0, 100, 0, 1), true, true, CombatWoundMechanism.Crush));
        Assert.True(state.TryGet(item, out var view)); Assert.Equal(.35f, view.Blood.W);
    }

    [Theory]
    [InlineData("dead")]
    [InlineData("prosthetic")]
    [InlineData("robot")]
    [InlineData("bloodless")]
    [InlineData("negative cut")]
    [InlineData("nan scale")]
    public void Non_bleeding_or_invalid_contacts_leave_the_actual_weapon_clean(string reason)
    {
        var state = new WeaponBloodState(); var item = Weapon();
        var race = reason switch { "robot" => new RaceData { IsRobot = true }, "bloodless" => new RaceData { BleedRate = 0 }, _ => Red };
        var damage = new HitDamage(reason == "negative cut" ? -1 : 20, 0, 0, Scale: reason == "nan scale" ? float.NaN : 1);
        Assert.False(state.Deposit(item, race, damage, reason != "dead", reason != "prosthetic", CombatWoundMechanism.Slash));
        Assert.False(state.TryGet(item, out _)); Assert.Empty(state.Views);
    }

    [Fact]
    public void Native_execution_deposits_only_on_the_actual_source_item_and_survives_putting_it_away()
    {
        using var patches = new Patches();
        var combat = new CombatSystem([Cut("cut left") with { Limb1 = 4 }], Constants, BaseGame, BodyOptions.Default, DuelFixture.Lengths, new() { TickMedical = false });
        var gameplay = Gameplay(combat); var state = new WeaponBloodState();
        using var world = World(combat, gameplay, state);
        var actual = Weapon(); var identical = Weapon("back");
        var source = new Combatant().MakeCold("attacker"); source.Inventory = new(); source.Inventory.Items.AddRange([actual, identical]);
        source.Fighter = new(new WeaponInstance(Katana, KatanaAt(.5f).Stats) { SourceItem = actual }, []);
        var target = new Combatant().MakeCold("target");
        var a = world.Characters.Spawn(new() { Position = new(0, 0, 0), Health = 100 }, source, 0);
        var b = world.Characters.Spawn(new() { Position = new(14, 0, 0), Health = 100 }, target, 0);
        world.RunTick(); combat.Execute(world, a.Slot, b.Slot, 1);
        Assert.True(state.TryGet(actual, out var blood)); Assert.True(blood.Blood.W > 0); Assert.Same(actual, blood.Item);
        Assert.False(state.TryGet(identical, out _));
        source.Fighter = new(new WeaponInstance(Katana, KatanaAt(.5f).Stats) { SourceItem = identical }, []);
        actual.Section = "main";
        Assert.True(state.TryGet(actual, out var kept)); Assert.Equal(blood.Blood, kept.Blood);
        var copy = actual.WithQuantity(1); Assert.True(state.TryGet(copy, out var copied)); Assert.Equal(blood.Blood, copied.Blood);
    }

    [Fact]
    public void A_destroyed_real_hand_drops_the_original_item_once_and_keeps_its_blood_grade_and_model()
    {
        var gameplay = Gameplay(); var blood = new WeaponBloodState(); int refreshes = 0;
        var equipment = new CutEquipmentAdapter(null, _ => refreshes++);
        using var world = World(gameplay, blood, equipment);
        var original = Weapon(); var spare = Weapon("back"); var cold = new Combatant().MakeCold();
        cold.Inventory = new(); cold.Inventory.Items.AddRange([original, spare]);
        cold.Fighter = new(new WeaponInstance(Katana, KatanaAt(.5f).Stats) { SourceItem = original }, []);
        cold.DrawnWeapon = WeaponKinds.Katana;
        var id = world.Characters.Spawn(new() { Position = new(5, 0, 6), Health = 100 }, cold, 0);
        Assert.True(blood.Deposit(original, Red, new(15, 0, 0, 1), true, true));
        equipment.Schedule(world);
        cold.Medical!.Sever(RArm, Context()); cold.Fighter.SetArms(false, true);
        equipment.SlowWorld(world); equipment.SlowWorld(world);
        var ground = Assert.Single(world.GroundItems.All); Assert.Same(original, ground.Item);
        Assert.Equal(id, ground.Fall!.Value.Character); Assert.Equal("Bip01 R Hand", ground.Fall.Value.Bone);
        Assert.Same(spare, Assert.Single(cold.Inventory.Items)); Assert.Equal(1, refreshes);
        Assert.Equal((81f, 63, "model", "maker"), (original.Quality, original.Level, original.MaterialId, original.CompanyId));
        Assert.True(blood.TryGet(ground.Item, out var kept)); Assert.Equal(.15f, kept.Blood.W);
    }

    [Fact]
    public void Oblique_support_cut_drops_a_mount_crossing_the_cut_but_retains_an_untouched_support_patch()
    {
        var cut = new TorsoBisectionState(new(Vector3.Normalize(new(1, 1, 0)), 0), true);
        var crossing = new CutWeaponMount(0, new(-1, -1, 0), new(1, 1, 0), new(-1, -1, 0), new(1, 1, 0), new(1, 1, 1));
        var untouched = new CutWeaponMount(0, new(2, 2, 0), new(3, 3, 0), new(2, 2, 0), new(3, 2, 0), new(2, 3, 0));
        Assert.True(CutWeaponDrops.Unsupported(crossing, cut, false, null));
        Assert.False(CutWeaponDrops.Unsupported(untouched, cut, false, null));
    }

    [Fact]
    public void Native_item_writer_allocates_distinct_durable_ids_and_restores_identical_weapons_after_reordering()
    {
        using var patches = new Patches();
        var db = SyntheticTown.Database(anatomy: true); var extra = new FcsFile();
        extra.Records.Add(SyntheticTown.Rec("brutality-katana", FcsRecordType.WEAPON, "same weapon")); db.Apply(extra, "weapon.mod");
        var data = SyntheticTown.Data(db); var blood = new WeaponBloodState();
        using var world = World(blood);
        world.Player.Faction = data.PlayerFaction;
        var squad = new Squad { Id = world.Squads.NextId(), TemplateId = "10-t", Name = "test", Faction = data.PlayerFaction };
        world.Squads.Add(squad); world.Player.Squad = squad.Id;
        var cold = new Combatant().MakeCold(); cold.RecordId = "2-t"; cold.IsPlayer = true; cold.Faction = data.PlayerFaction; cold.SquadId = squad.Id;
        cold.Inventory = new(); var first = data.Items.Create("brutality-katana")!; var second = data.Items.Create("brutality-katana")!;
        first.Section = "hip"; second.Section = "back"; cold.Inventory.Items.AddRange([first, second]);
        var id = world.Characters.Spawn(new() { Position = new(10, 0, 10) }, cold, 0); squad.Members.Add(id); squad.Leader = id;
        Assert.True(blood.Deposit(first, Red, new(10, 0, 0, 1), true, true));
        Assert.True(blood.Deposit(second, Green, new(30, 0, 0, 1), true, true));
        var save = SaveCapture.Capture(world, data, new(1, 12, 0)); var bytes = Write(blood);
        Assert.NotNull(CutEquipmentAdapter.ItemKey(first)); Assert.NotEqual(CutEquipmentAdapter.ItemKey(first), CutEquipmentAdapter.ItemKey(second));
        var restored = new WeaponBloodState(); using var loaded = World(restored);
        SaveLoader.Load(loaded, save, data); restored.Read(new MemoryStream(bytes));
        var owner = loaded.Characters.Cold(loaded.Squads.Find(loaded.Player.Squad)!.Leader.Slot)!;
        var restoredFirst = owner.Inventory!.Items.Single(item => item.Section == "hip");
        var restoredSecond = owner.Inventory.Items.Single(item => item.Section == "back");
        owner.Inventory.Items.Reverse(); (restoredFirst.Section, restoredSecond.Section) = ("main", "hip");
        restored.Schedule(loaded);
        Assert.True(restored.TryGet(restoredFirst, out var red)); Assert.Equal(new(1, 0, 0, .1f), red.Blood);
        Assert.True(restored.TryGet(restoredSecond, out var green)); Assert.Equal(new(0, 1, 0, .3f), green.Blood);
        Assert.Contains(owner.Fighter!.HipWeapon!.SourceItem!, owner.Inventory.Items);
        Assert.Equal(bytes, Write(restored));
        // Offscreen/temporarily unresolved items remain in the sidecar until their real record is materialized.
        var deferred = new WeaponBloodState(); deferred.Read(new MemoryStream(bytes)); Assert.Equal(bytes, Write(deferred));
    }
}
