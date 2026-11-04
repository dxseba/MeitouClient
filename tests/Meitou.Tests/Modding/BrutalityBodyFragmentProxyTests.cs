using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using HarmonyLib;
using Meitou.Brutality;
using Meitou.Data.Fcs;
using Meitou.Data.Save;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Carrying;
using Meitou.Simulation.Items;
using Meitou.Simulation.Saving;
using Meitou.Tests.Combat;
using Meitou.Tests.Simulation;
using static Meitou.Tests.Bodies.BodyFixtures;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Modding;

[Collection("Brutality native combat")]
public sealed class BrutalityBodyFragmentProxyTests
{
    static readonly Hand OwnerHand = new(25, 11, 12, 13, 14), CarrierHand = new(25, 21, 22, 23, 24);
    sealed class Scene : IDisposable
    {
        public BrutalityGameplaySystem Gameplay { get; } = new(null, BaseGame, BodyOptions.Default, (RngPurpose)0xB17A1171);
        public CarrySystem Carry { get; } = new(BaseGame, BodyOptions.Default, .1f, new() { AutoEveryTicks = 0 });
        public BodyFragmentProxySystem Proxies { get; }
        public SimWorld World { get; }
        public CharacterId Owner { get; }
        public CharacterCold Source { get; } = new Combatant().MakeCold("corpse");
        public Scene(int padding = 0, Action<CharacterCold>? refresh = null, bool body = false)
        {
            Proxies = new(refresh); World = new(new WorldSettings { Seed = 29, PublishSnapshots = false }, new OpenGroundWalkability((_, _) => 0), body ? [new BodySystem(BaseGame, BodyOptions.Default), Carry, Gameplay, Proxies] : [Carry, Gameplay, Proxies]);
            for (int i = 0; i < padding; i++) World.Characters.Spawn(new(), new(), 0);
            Source.Faction = 2; Source.Inventory = new();
            Source.Race = new() { StringId = Human.StringId, Name = Human.Name, StatMultipliers = Human.StatMultipliers, Anatomy = Human.Anatomy, Carriable = true };
            Source.Medical!.Butcher();
            Owner = World.Characters.Spawn(new() { Position = new(10, 0, 10) }, Source, 0);
            Gameplay.CaptureSaveIdentity(World, Source, Owner, OwnerHand);
        }
        public void Cut()
        {
            var saved = new BrutalityDurableState(SaveIdentityAdapter.KeyFor(OwnerHand)!, 0, new(0), null, new(new(0, 1, 0, -10)), []);
            Gameplay.ImportDurable(World, [saved], _ => Owner); Gameplay.SlowWorld(World);
        }
        public CharacterId Carrier(Hand? hand = null, Vector3? position = null)
        {
            var cold = new Combatant().MakeCold("carrier"); cold.IsPlayer = true; cold.Faction = 1;
            var id = World.Characters.Spawn(new() { Position = position ?? new(11, 0, 10), Health = 100 }, cold, World.Tick);
            if (hand is { } key) Gameplay.CaptureSaveIdentity(World, cold, id, key);
            return id;
        }
        public void Dispose() => World.Dispose();
    }
    static byte[] Write(BodyFragmentProxySystem system) { using var stream = new MemoryStream(); system.Write(stream); return stream.ToArray(); }

    [Fact]
    public void The_two_halves_can_be_lifted_independently_through_the_real_native_carry_orders()
    {
        using var scene = new Scene(); scene.Cut(); scene.World.RunTicks(2);
        Assert.True(scene.Proxies.TryProxy(scene.Owner, 1, out var lower, out var proxy));
        Assert.True(CarrySystem.CanBeLifted(scene.Source)); Assert.True(CarrySystem.CanBeLifted(proxy));
        var a = scene.Carrier(); var b = scene.Carrier();
        scene.World.Commands.Enqueue(new PickUpOrder([a], scene.Owner) { Tick = scene.World.Tick });
        scene.World.Commands.Enqueue(new PickUpOrder([b], lower) { Tick = scene.World.Tick }); scene.World.RunTicks(20);
        Assert.Equal(a, scene.Source.CarriedBy); Assert.Equal(b, proxy.CarriedBy);
        Assert.Equal(scene.Owner, scene.World.Characters.Cold(a.Slot)!.Carrying); Assert.Equal(lower, scene.World.Characters.Cold(b.Slot)!.Carrying);
        scene.World.Commands.Enqueue(new PutDownOrder([b]) { Tick = scene.World.Tick }); scene.World.RunTicks(20);
        Assert.True(proxy.CarriedBy.IsNone); Assert.Equal(a, scene.Source.CarriedBy);
        Assert.True(scene.World.Characters.Cold(b.Slot)!.Carrying.IsNone);
    }
    [Fact]
    public void The_proxy_shares_loot_but_has_independent_dead_medical_state_no_ai_and_no_persistent_squad()
    {
        CharacterCold? refreshed = null; using var scene = new Scene(refresh: cold => refreshed = cold); scene.Cut();
        var item = new ItemInstance { Type = FcsRecordType.WEAPON, Record = "blade", Quality = 81 }; scene.Source.Inventory!.Items.Add(item);
        scene.World.RunTicks(2); Assert.True(scene.Proxies.TryProxy(scene.Owner, 1, out var lower, out var proxy));
        Assert.Same(scene.Source.Inventory, proxy.Inventory); Assert.NotSame(scene.Source.Medical, proxy.Medical); Assert.NotSame(scene.Source.Stats, proxy.Stats);
        Assert.True(proxy.Medical!.Dead); Assert.Null(proxy.Ai); Assert.False(proxy.IsPlayer); Assert.Equal(-1, proxy.SquadId); Assert.Null(proxy.Save);
        Assert.True(BrutalityRuntime.ControlsPhysics(proxy)); Assert.Null(proxy.Fighter!.Weapon);
        float before = scene.Source.Medical!.Parts[0].Flesh; proxy.Medical.Parts[0].Flesh = -1000; Assert.Equal(before, scene.Source.Medical.Parts[0].Flesh);
        proxy.Inventory!.Items.Remove(item); scene.World.RunTick(); Assert.Empty(scene.Source.Inventory.Items); Assert.Same(scene.Source, refreshed);
        var frame = scene.Proxies.FrameAt(scene.World.Tick)!; Assert.Equal(scene.Owner, frame.Proxies[lower].Owner); Assert.Equal(1, frame.Proxies[lower].StableKey);
    }
    [Fact]
    public void Durable_owner_and_carrier_links_wait_for_the_missing_actor_and_follow_changed_runtime_slots()
    {
        using var original = new Scene(); original.Cut(); var carrier = original.Carrier(CarrierHand); original.World.RunTicks(2);
        Assert.True(original.Proxies.TryProxy(original.Owner, 1, out var lower));
        original.World.Commands.Enqueue(new PickUpOrder([carrier], lower) { Tick = original.World.Tick }); original.World.RunTicks(20);
        var bytes = Write(original.Proxies);
        using var restored = new Scene(padding: 3); restored.Cut(); restored.Proxies.Read(new MemoryStream(bytes)); restored.World.RunTicks(2);
        Assert.True(restored.Proxies.TryProxy(restored.Owner, 1, out var moved, out var cold)); Assert.NotEqual(lower, moved); Assert.True(cold.CarriedBy.IsNone);
        Assert.Contains(SaveIdentityAdapter.KeyFor(CarrierHand)!, System.Text.Encoding.UTF8.GetString(Write(restored.Proxies)));
        var nextCarrier = restored.Carrier(CarrierHand, new(50, 0, 60)); restored.World.RunTicks(2);
        Assert.Equal(nextCarrier, cold.CarriedBy); Assert.Equal(moved, restored.World.Characters.Cold(nextCarrier.Slot)!.Carrying);
        Assert.True(restored.Proxies.TryTakeRestoredPlacement(restored.Owner, 1, out var position)); Assert.True(float.IsFinite(position.Position.LengthSquared()));
        Assert.False(restored.Proxies.TryTakeRestoredPlacement(restored.Owner, 1, out _));
    }
    [Fact]
    public void A_missing_owner_stays_in_the_sidecar_and_an_unheld_proxy_is_removed_with_its_owner()
    {
        using var scene = new Scene(); scene.Cut(); scene.World.RunTicks(2); Assert.True(scene.Proxies.TryProxy(scene.Owner, 1, out var proxy, out var cold));
        var bytes = Write(scene.Proxies); var pending = new BodyFragmentProxySystem(); pending.Read(new MemoryStream(bytes)); Assert.Equal(bytes, Write(pending));
        Assert.True(scene.World.Characters.Remove(scene.Owner)); scene.World.RunTick();
        Assert.False(scene.World.Characters.IsAlive(proxy)); Assert.False(BrutalityRuntime.ControlsPhysics(cold)); Assert.False(scene.Proxies.TryProxy(scene.Owner, 1, out _));
        Assert.Contains(SaveIdentityAdapter.KeyFor(OwnerHand)!, System.Text.Encoding.UTF8.GetString(Write(scene.Proxies)));
    }
    [Fact]
    public void Native_save_capture_writes_the_shared_inventory_once_and_the_proxy_only_uses_its_mod_sidecar()
    {
        var harmony = new Harmony("brutality.proxy-save." + Guid.NewGuid().ToString("N")); SaveIdentityAdapter.Install(harmony);
        try
        {
            using var scene = new Scene(); scene.Cut(); var data = SyntheticTown.Data(SyntheticTown.Database(anatomy: true, items: true));
            scene.Source.RecordId = "2-t"; scene.Source.IsPlayer = true; scene.Source.Faction = data.PlayerFaction;
            var squad = new Squad { Id = scene.World.Squads.NextId(), TemplateId = "10-t", Faction = data.PlayerFaction };
            scene.World.Squads.Add(squad); scene.World.Player.Squad = squad.Id; scene.World.Player.Faction = data.PlayerFaction;
            squad.Members.Add(scene.Owner); squad.Leader = scene.Owner; scene.Source.SquadId = squad.Id;
            scene.Source.Inventory!.Items.Add(data.Items.Create("70-t")!); scene.World.RunTicks(2);
            var save = SaveCapture.Capture(scene.World, data, new(1, 12, 0));
            Assert.Single(save.Characters); Assert.Single(save.AllFiles().SelectMany(file => file.File.Records), record => record.RecordType == FcsRecordType.INVENTORY_ITEM_STATE);
            var rows = JsonNode.Parse(Write(scene.Proxies))!["Halves"]!.AsArray(); Assert.Single(rows);
            Assert.Equal(SaveIdentityAdapter.KeyFor(Assert.Single(save.Characters).Handle), rows[0]!["Owner"]!.GetValue<string>());
            Assert.DoesNotContain(save.Characters, character => character.Name.EndsWith("Lower body", StringComparison.Ordinal));
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
    [Fact]
    public void A_carried_negative_half_keeps_its_owner_and_proxy_through_real_native_offscreen_corpse_cleanup()
    {
        var harmony = new Harmony("brutality.proxy-retention." + Guid.NewGuid().ToString("N")); BodyFragmentProxySystem.InstallRetention(harmony);
        try
        {
            using var scene = new Scene(body: true); scene.Cut(); var carrier = scene.Carrier(); scene.World.RunTicks(2);
            Assert.True(scene.Proxies.TryProxy(scene.Owner, 1, out var proxy, out var cold));
            scene.World.Commands.Enqueue(new PickUpOrder([carrier], proxy) { Tick = scene.World.Tick }); scene.World.RunTicks(20);
            Assert.Equal(carrier, cold.CarriedBy);
            scene.World.LoadedZones = [];
            scene.World.RunTicks(3); // Native BodySystem removes both unloaded corpses without the module's narrow keepalive.
            Assert.True(scene.World.Characters.IsAlive(scene.Owner)); Assert.True(scene.World.Characters.IsAlive(proxy));
            Assert.Equal(proxy, scene.World.Characters.Cold(carrier.Slot)!.Carrying);
            scene.World.Commands.Enqueue(new PutDownOrder([carrier]) { Tick = scene.World.Tick }); scene.World.RunTicks(20);
            Assert.False(scene.World.Characters.IsAlive(scene.Owner)); Assert.False(scene.World.Characters.IsAlive(proxy));
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
    [Fact]
    public void The_keepalive_does_not_block_an_explicit_removal_outside_native_corpse_cleanup()
    {
        var harmony = new Harmony("brutality.proxy-explicit-removal." + Guid.NewGuid().ToString("N")); BodyFragmentProxySystem.InstallRetention(harmony);
        try
        {
            using var scene = new Scene(body: true); scene.Cut(); var carrier = scene.Carrier(); scene.World.RunTicks(2);
            Assert.True(scene.Proxies.TryProxy(scene.Owner, 1, out var proxy));
            scene.World.Commands.Enqueue(new PickUpOrder([carrier], proxy) { Tick = scene.World.Tick }); scene.World.RunTicks(20);
            Assert.True(scene.World.Characters.Remove(scene.Owner)); Assert.False(scene.World.Characters.IsAlive(scene.Owner));
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }    [Fact]
    public void Paused_native_half_looting_keeps_the_original_stolen_owner_full_handle_and_actual_item()
    {
        var harmony = new Harmony("brutality.proxy-loot-owner." + Guid.NewGuid().ToString("N"));
        BodyFragmentProxySystem.InstallRetention(harmony); SaveIdentityAdapter.Install(harmony);
        try
        {
            using var scene = new Scene(); scene.Cut();
            var data = SyntheticTown.Data(SyntheticTown.Database(anatomy: true, items: true));
            scene.Source.RecordId = "2-t"; scene.Source.Faction = data.FactionIndex("20-t"); scene.Source.SquadId = 101;
            scene.World.Player.Faction = data.PlayerFaction;
            var looter = scene.Carrier(); var looterCold = scene.World.Characters.Cold(looter.Slot)!;
            looterCold.RecordId = "2-t"; looterCold.Faction = data.PlayerFaction; looterCold.Inventory = new();
            var squad = new Squad { Id = scene.World.Squads.NextId(), TemplateId = "10-t", Faction = data.PlayerFaction };
            scene.World.Squads.Add(squad); scene.World.Player.Squad = squad.Id; squad.Members.Add(looter); squad.Leader = looter; looterCold.SquadId = squad.Id;
            var item = data.Items.Create("70-t", quality: 81)!; scene.Source.Inventory!.Items.Add(item);
            scene.World.RunTicks(2); Assert.True(scene.Proxies.TryProxy(scene.Owner, 1, out var proxy));
            scene.World.Player.OpenLoot = proxy; scene.World.Player.LootOpener = looter;
            scene.World.Commands.Enqueue(new TakeLootItem(new(0, item.Record, item.Section, item.X, item.Y, item.Quantity, item.Quality)) { Tick = scene.World.Tick });
            scene.World.ApplyPausedInterfaceCommands();
            Assert.Same(item, Assert.Single(looterCold.Inventory.Items)); Assert.Empty(scene.Source.Inventory.Items);
            Assert.Equal(scene.Owner, item.Owner); Assert.Equal(101, item.OwnerSquad); Assert.Equal(scene.Source.Faction, item.Mark.Faction); Assert.Equal(81, item.Quality);
            var save = SaveCapture.Capture(scene.World, data, new(1, 12, 0));
            var original = Assert.Single(save.Characters, character => character.Name == "corpse");
            var savedItem = Assert.Single(save.AllFiles().SelectMany(file => file.File.Records), record => record.RecordType == FcsRecordType.INVENTORY_ITEM_STATE && record.Strings.GetValueOrDefault("base data sid") == "70-t");
            Assert.Equal(original.Handle, Hand.Read(savedItem, "ownedby"));
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }    [Theory]
    [InlineData("bandit")]
    [InlineData("slave")]
    [InlineData("bounty")]
    [InlineData("player")]
    public void Half_loot_permissions_read_the_actual_owner_without_sharing_its_bounty_ledger(string reason)
    {
        var harmony = new Harmony("brutality.proxy-loot-permission." + Guid.NewGuid().ToString("N")); BodyFragmentProxySystem.InstallRetention(harmony);
        try
        {
            using var scene = new Scene(); scene.World.Player.Faction = 1;
            switch (reason)
            {
                case "bandit": scene.Source.NpcClass = Meitou.Data.Gameplay.FundamentalType.Bandit; break;
                case "slave": scene.Source.SlaveState = 1; break;
                case "bounty": scene.Source.Bounties = new(); scene.Source.Bounties.Get(2, "owner").Amount = 100; break;
                case "player": scene.Source.IsPlayer = true; scene.Source.Faction = 1; break;
            }
            scene.Cut(); var looter = scene.Carrier(); scene.World.RunTicks(2);
            Assert.True(scene.Proxies.TryProxy(scene.Owner, 1, out _, out var proxy)); Assert.Null(proxy.Bounties);
            var taker = scene.World.Characters.Cold(looter.Slot)!;
            var allowed = AccessTools.Method(typeof(Meitou.Simulation.Crime.CrimeSystem), nameof(Meitou.Simulation.Crime.CrimeSystem.LootAllowed));
            Assert.Equal(allowed.Invoke(null, [taker, scene.Source]), allowed.Invoke(null, [taker, proxy]));
            var code = AccessTools.Method(typeof(Meitou.Simulation.Crime.CrimeSystem), nameof(Meitou.Simulation.Crime.CrimeSystem.TakeCode));
            Assert.Null(code.Invoke(null, [scene.World, taker, proxy, Meitou.Simulation.Crime.CrimeSystem.TakeKind.RightClick]));
            if (reason == "bounty") { scene.Source.Bounties!.Get(2, "owner").Amount = 0; Assert.False((bool)allowed.Invoke(null, [taker, proxy])!); }
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }}
