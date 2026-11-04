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
using Meitou.Tests.Combat;
using Meitou.Tests.Simulation;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Modding;

[Collection("Brutality native combat")]
public sealed class BrutalityWeaponLegacyTests
{
    sealed class Patches : IDisposable
    {
        readonly Harmony harmony = new("brutality.weapon-legacy." + Guid.NewGuid().ToString("N"));
        public Patches() => CutEquipmentAdapter.Install(harmony);
        public void Dispose() => harmony.UnpatchAll(harmony.Id);
    }
    static SimWorld World(WeaponBloodState state) => new(new WorldSettings { PublishSnapshots = false }, new OpenGroundWalkability((_, _) => 0), [state]);
    static byte[] Write(WeaponBloodState state) { using var stream = new MemoryStream(); state.Write(stream); return stream.ToArray(); }
    static (SaveGame Save, PopulationData Data, FcsRecord Item) NativeSave()
    {
        var weapon = SyntheticTown.Rec("legacy-blade", FcsRecordType.WEAPON, "legacy blade");
        var data = SyntheticTown.Data(SyntheticTown.Database(anatomy: true, extra: [weapon]));
        using var world = World(new()); world.Player.Faction = data.PlayerFaction;
        var squad = new Squad { Id = world.Squads.NextId(), TemplateId = "10-t", Faction = data.PlayerFaction }; world.Squads.Add(squad); world.Player.Squad = squad.Id;
        var cold = new Combatant().MakeCold(); cold.IsPlayer = true; cold.RecordId = "2-t"; cold.Faction = data.PlayerFaction; cold.SquadId = squad.Id;
        cold.Inventory = new(); cold.Inventory.Items.Add(data.Items.Create("legacy-blade")!);
        var id = world.Characters.Spawn(new() { Position = new(10, 0, 10) }, cold, 0); squad.Members.Add(id); squad.Leader = id;
        var save = SaveCapture.Capture(world, data, new(1, 12, 0));
        var item = Assert.Single(save.AllFiles().SelectMany(file => file.File.Records), record => record.RecordType == FcsRecordType.INVENTORY_ITEM_STATE);
        return (save, data, item);
    }
    static ItemInstance LoadedItem(SimWorld world) => Assert.Single(world.Characters.Cold(world.Squads.Find(world.Player.Squad)!.Leader.Slot)!.Inventory!.Items);

    [Fact]
    public void Legacy_item_record_blood_is_imported_by_its_actual_record_and_clamped_like_the_pin_before_the_first_tick()
    {
        using var patches = new Patches(); var (save, data, item) = NativeSave(); item.Vector4s["meitou weapon blood"] = new(-2, 3, .5f, 2);
        var state = new WeaponBloodState(); using var world = World(state); SaveLoader.Load(world, save, data);
        Assert.Equal(0, world.Tick); Assert.True(state.TryGet(LoadedItem(world), out var blood)); Assert.Equal(new(0, 1, .5f, 1), blood.Blood);
        Assert.Contains(CutEquipmentAdapter.ItemKey(blood.Item)!, System.Text.Encoding.UTF8.GetString(Write(state)));
    }
    [Fact]
    public void A_new_empty_weapon_sidecar_is_authoritative_over_stale_legacy_item_fields()
    {
        using var patches = new Patches(); var (save, data, item) = NativeSave(); item.Vector4s["meitou weapon blood"] = new(1, 0, 0, .8f);
        var state = new WeaponBloodState(); using var world = World(state); SaveLoader.Load(world, save, data);
        state.Read(new MemoryStream(Write(new WeaponBloodState()))); state.Schedule(world);
        Assert.False(state.TryGet(LoadedItem(world), out _)); Assert.Empty(state.Views);
    }
    [Fact]
    public void Offscreen_legacy_container_entries_keep_their_B_colour_through_new_sidecar_capture_and_later_materialization()
    {
        using var patches = new Patches(); var (save, data, _) = NativeSave();
        var inventory = new Inventory(); inventory.Items.Add(data.Items.Create("legacy-blade")!);
        var rows = JsonNode.Parse(ContainerSave.Write(inventory)!)!.AsArray(); rows[0]!["B"] = new JsonArray(0, 1, 0, .6f);
        var building = new FcsRecord { StringId = "legacy-building", RecordType = FcsRecordType.GAMESTATE_BUILDING };
        building.Strings["meitou container 0"] = rows.ToJsonString(); save.Quick.Records.Add(building);
        var state = new WeaponBloodState(); using var world = World(state); SaveLoader.Load(world, save, data);
        var tagged = JsonNode.Parse(building.Strings["meitou container 0"])!.AsArray();
        string key = tagged[0]![CutEquipmentAdapter.ContainerTokenProperty]!.GetValue<string>(); Assert.True(WeaponBloodState.IsContainerToken(key));
        var bytes = Write(state); var restored = new WeaponBloodState(); restored.Read(new MemoryStream(bytes));
        Assert.Contains(key, System.Text.Encoding.UTF8.GetString(Write(restored)));
        var later = new Inventory(); ContainerSave.Read(tagged.ToJsonString(), later, data.Items);
        Assert.True(restored.TryGet(Assert.Single(later.Items), out var blood)); Assert.Equal(new(0, 1, 0, .6f), blood.Blood);
    }
}
