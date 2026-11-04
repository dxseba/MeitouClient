using System.Globalization;
using System.Numerics;
using HarmonyLib;
using Meitou.Brutality;
using Meitou.Data.Save;
using Meitou.Data.Gameplay.Ai;
using Meitou.Simulation.Carrying;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Saving;
using Meitou.Tests.Combat;
using Meitou.Tests.Simulation;
using static Meitou.Tests.Bodies.BodyFixtures;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Modding;

[CollectionDefinition("Brutality save adapter", DisableParallelization = true)]
public sealed class BrutalitySaveAdapterCollection;

[Collection("Brutality save adapter")]
public sealed class BrutalitySaveAdapterTests : IClassFixture<BrutalitySaveAdapterTests.Patches>
{
    public sealed class Patches : IDisposable
    {
        readonly Harmony harmony = new("meitou.tests.brutality.save-adapter");
        public Patches() { SaveIdentityAdapter.Install(harmony); BodyFragmentProxySystem.InstallOrderPersistence(harmony); }
        public void Dispose() => harmony.UnpatchAll(harmony.Id);
    }

    static BrutalityGameplaySystem Gameplay() => new(null, BaseGame, BodyOptions.Default, (RngPurpose)0xB17A1171);
    static SimWorld World(BrutalityGameplaySystem system) => new(new WorldSettings { Seed = 21, PublishSnapshots = false },
        new OpenGroundWalkability((_, _) => 0), [system]);
    static CharacterCold Cold() => new Combatant().MakeCold("same name");
    static byte[] Write(BrutalityGameplaySystem system)
    {
        using var stream = new MemoryStream();
        ((IModSaveData)system).Write(stream);
        return stream.ToArray();
    }
    static void Read(BrutalityGameplaySystem system, byte[] bytes) => ((IModSaveData)system).Read(new MemoryStream(bytes, writable: false));
    static IReadOnlyList<BrutalityDurableState> Decode(byte[] bytes) => BrutalityStateCodec.Read(new MemoryStream(bytes, writable: false));
    static byte[] Payload(params BrutalityDurableState[] saved)
    {
        using var stream = new MemoryStream();
        BrutalityStateCodec.Write(stream, saved);
        return stream.ToArray();
    }
    static BrutalityDurableState State(string key, float level) => new(key, BrutalityProgress.FromLevel(level).Experience,
        new(4, SevereHeadWoundKind.BrokenSkull, 2, new(new(1, 2, 3), Vector3.UnitZ, Vector3.UnitY, 1.2f, .8f)),
        new(new(1, 0, 0, .1f), HeadCutMode.DetachedSplit), new(new(0, 1, 0, -12)), [new(0, .32f, new(.2f, -.1f)), new(3, .71f, new(-.3f, .4f))]);
    static void EqualState(BrutalityDurableState expected, BrutalityDurableState actual)
    {
        Assert.Equal(expected.Key, actual.Key);
        Assert.Equal(expected.Experience, actual.Experience);
        Assert.Equal(expected.SevereHead, actual.SevereHead);
        Assert.Equal(expected.Head, actual.Head);
        Assert.Equal(expected.Torso, actual.Torso);
        Assert.Equal(expected.Limbs, actual.Limbs);
    }
    static SaveCharacter Saved(Hand hand)
    {
        var builder = new SaveBuilder();
        var platoon = builder.AddPlatoon("test_0", "Testfolk", "20-t", "10-t", Vector3.Zero);
        var saved = builder.AddCharacter(platoon, "2-t", "same name", "20-t", Vector3.Zero, Quaternion.Identity);
        saved.Handle = hand;
        return saved;
    }
    static CharacterCold LoadedCold(SaveCharacter source)
    {
        var cold = Cold();
        cold.Save = new() { Source = source, PlatoonName = source.Platoon.Name, Slot = source.Handle.I };
        return cold;
    }

    [Fact]
    public void Save_identity_uses_all_five_handle_fields_and_has_no_culture_or_null_alias()
    {
        var original = new Hand(25, -2, 3, 4, -5);
        var prior = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            Assert.Equal("hand:v1:25:-2:3:4:-5", SaveIdentityAdapter.KeyFor(original));
            var variants = new[] { original, original with { Type = 26 }, original with { C = 2 }, original with { CS = 4 },
                original with { I = 5 }, original with { S = 5 } };
            Assert.Equal(variants.Length, variants.Select(SaveIdentityAdapter.KeyFor).Distinct().Count());
            Assert.Null(SaveIdentityAdapter.KeyFor(Hand.Null));
            Assert.Null(SaveIdentityAdapter.KeyFor(new(Hand.NullType, 12, 45, 67, 89)));
        }
        finally { CultureInfo.CurrentCulture = prior; }
    }

    [Fact]
    public void Native_capture_allocates_keys_for_fresh_players_and_restores_all_cut_state_before_a_tick()
    {
        var data = SyntheticTown.Data(SyntheticTown.Database(anatomy: true));
        var original = Gameplay();
        using var world = World(original);
        world.Player.Faction = data.PlayerFaction;
        var squad = new Squad { Id = world.Squads.NextId(), TemplateId = "10-t", Name = "test", Faction = data.PlayerFaction };
        world.Squads.Add(squad); world.Player.Squad = squad.Id;
        for (int i = 0; i < 2; i++)
        {
            var cold = Cold(); cold.IsPlayer = true; cold.Faction = data.PlayerFaction; cold.SquadId = squad.Id; cold.RecordId = "2-t";
            var id = world.Characters.Spawn(new() { Position = new(10 + i, 0, 10) }, cold, 0);
            squad.Members.Add(id); if (i == 0) squad.Leader = id;
            Assert.True(original.SetLevel(world, id, 20 + i));
        }
        var imported = State("test-only-resolved-key", 73);
        Assert.Equal(1, original.ImportDurable(world, [imported], _ => squad.Leader));
        // No Schedule/BindWorld call: the real writer prefix must establish the save context.
        var save = SaveCapture.Capture(world, data, new(1, 12, 0));
        var rows = Decode(Write(original));
        Assert.Equal(2, rows.Count);
        Assert.All(squad.Members, id => Assert.Null(world.Characters.Cold(id.Slot)!.Save));
        var firstSaved = save.Platoons.SelectMany(p => p.Characters).Single(c => c.IsLeader);
        var expected = imported with { Key = SaveIdentityAdapter.KeyFor(firstSaved.Handle)! };
        EqualState(expected, Assert.Single(rows, row => row.Key == expected.Key));
        Assert.DoesNotContain(rows, row => row.Key == imported.Key);

        var restored = Gameplay();
        using var loadedWorld = World(restored);
        SaveLoader.Load(loadedWorld, save, data);
        restored.BindWorld(loadedWorld);
        Read(restored, Write(original));
        var loaded = Enumerable.Range(0, loadedWorld.Characters.HighWater).Select(slot => loadedWorld.Characters.Cold(slot))
            .Single(cold => cold?.Save?.Source.Handle == firstSaved.Handle)!;
        EqualState(expected, restored.DurableFor(loaded, expected.Key)!);
        Assert.Equal(0, loadedWorld.Tick);
        Assert.Equal(rows.Select(row => row.Key), Decode(Write(restored)).Select(row => row.Key));
    }

    [Fact]
    public void Fresh_offscreen_cold_state_gets_the_actual_native_handle_instead_of_disappearing()
    {
        var data = SyntheticTown.Data(SyntheticTown.Database());
        var system = Gameplay();
        using var world = World(system);
        var cold = Cold(); cold.RecordId = "2-t"; cold.Faction = data.FactionIndex("20-t");
        var id = world.Characters.Spawn(new(), cold, 0);
        Assert.True(system.SetLevel(world, id, 66));
        Assert.True(world.Characters.Remove(id));
        system.SlowWorld(world);
        var stored = new StoredSquad { Name = "stored" };
        stored.Members.Add(new(cold, new(10, 0, 10), 0, true));
        world.Platoons.Add(new() { Id = world.Platoons.NextId(), Origin = 0, Faction = cold.Faction, TemplateId = "10-t",
            Key = 19, State = PlatoonState.Unloaded, Stored = stored });
        var save = SaveCapture.Capture(world, data, new(1, 12, 0));
        var written = Assert.Single(save.Platoons.SelectMany(platoon => platoon.Characters));
        var row = Assert.Single(Decode(Write(system)));
        Assert.Equal(SaveIdentityAdapter.KeyFor(written.Handle), row.Key);
        Assert.Equal(BrutalityProgress.FromLevel(66).Experience, row.Experience);
        Assert.Null(cold.Save);
    }

    [Fact]
    public void Loaded_NPC_state_survives_slot_reuse_and_reappears_on_a_new_cold_object_once()
    {
        var source = Saved(new(25, 31, 81, 0, 102));
        string key = SaveIdentityAdapter.KeyFor(source.Handle)!;
        var system = Gameplay();
        using var world = World(system);
        var oldCold = LoadedCold(source);
        var id = world.Characters.Spawn(new(), oldCold, 0);
        system.Schedule(world);
        var expected = State(key, 78);
        Read(system, Payload(expected));
        Assert.True(world.Characters.Remove(id));
        system.SlowWorld(world);
        EqualState(expected, Assert.Single(Decode(Write(system))));

        // Same name, template, container and slot; a different serial is a different actor.
        var wrongCold = LoadedCold(Saved(source.Handle with { S = 103 }));
        var reused = world.Characters.Spawn(new(), wrongCold, 0);
        Assert.Equal(id.Slot, reused.Slot); Assert.NotEqual(id.Generation, reused.Generation);
        system.Schedule(world);
        Assert.NotEqual(78, system.LevelOf(world, reused));
        EqualState(expected, Assert.Single(Decode(Write(system)), row => row.Key == key));
        Assert.True(world.Characters.Remove(reused));
        system.SlowWorld(world);

        var returned = LoadedCold(source);
        var restoredId = world.Characters.Spawn(new(), returned, 0);
        system.Schedule(world);
        EqualState(expected, system.DurableFor(returned, key)!);
        Assert.True(system.SetLevel(world, restoredId, 79));
        system.Schedule(world);
        Assert.Equal(79, system.LevelOf(world, restoredId), 5);
    }

    [Fact]
    public void Sidecar_read_before_binding_preserves_deferred_members_until_the_exact_handle_materializes()
    {
        var source = Saved(new(25, 90, 12, 4, 200));
        var pending = State(SaveIdentityAdapter.KeyFor(source.Handle)!, 81);
        var system = Gameplay();
        Read(system, Payload(pending));
        using var world = World(system);
        system.BindWorld(world);
        EqualState(pending, Assert.Single(Decode(Write(system))));
        var other = LoadedCold(Saved(source.Handle with { CS = 13 }));
        var otherId = world.Characters.Spawn(new(), other, 0);
        system.Schedule(world);
        Assert.NotEqual(81, system.LevelOf(world, otherId));
        var cold = LoadedCold(source);
        world.Characters.Spawn(new(), cold, 0);
        system.Schedule(world);
        EqualState(pending, system.DurableFor(cold, pending.Key)!);
        EqualState(pending, Assert.Single(Decode(Write(system)), row => row.Key == pending.Key));
    }

    [Fact]
    public void A_new_capture_replaces_fresh_handle_keys_without_duplicate_stale_rows()
    {
        var data = SyntheticTown.Data(SyntheticTown.Database());
        var system = Gameplay();
        using var world = World(system);
        world.Player.Faction = data.PlayerFaction;
        var squad = new Squad { Id = world.Squads.NextId(), TemplateId = "10-t", Faction = data.PlayerFaction };
        world.Squads.Add(squad); world.Player.Squad = squad.Id;
        var cold = Cold(); cold.IsPlayer = true; cold.RecordId = "2-t"; cold.Faction = data.PlayerFaction; cold.SquadId = squad.Id;
        var id = world.Characters.Spawn(new(), cold, 0); squad.Members.Add(id); squad.Leader = id;
        Assert.True(system.SetLevel(world, id, 62));
        var first = SaveCapture.Capture(world, data, new(1, 12, 0), options: new() { Seed = 1 });
        string firstKey = Assert.Single(Decode(Write(system))).Key;
        Assert.True(system.SetLevel(world, id, 63));
        var second = SaveCapture.Capture(world, data, new(1, 12, 0), options: new() { Seed = 2 });
        var row = Assert.Single(Decode(Write(system)));
        Assert.NotEqual(firstKey, row.Key);
        Assert.Equal(SaveIdentityAdapter.KeyFor(Assert.Single(second.Platoons.SelectMany(p => p.Characters)).Handle), row.Key);
        Assert.Equal(BrutalityProgress.FromLevel(63).Experience, row.Experience);
        Assert.NotEqual(Assert.Single(first.Platoons.SelectMany(p => p.Characters)).Handle,
            Assert.Single(second.Platoons.SelectMany(p => p.Characters)).Handle);
    }

    static (SaveGame Save, PopulationData Data, SaveCharacter Player, SaveCharacter Npc) LegacySave()
    {
        var data = SyntheticTown.Data(SyntheticTown.Database(anatomy: true));
        var builder = SaveCapture.PrepareBase(data);
        var faction = data.Factions[data.PlayerFaction];
        var playerPlatoon = builder.AddPlatoon("legacy_player", faction.Name, faction.Id, "10-t", new(10, 0, 10));
        var npcPlatoon = builder.AddPlatoon("legacy_npc", "Testfolk", "20-t", "10-t", new(1000, 0, 2000));
        npcPlatoon.Record.Strings["basetown"] = "30-t";
        npcPlatoon.Record.Bools["is resident"] = true;
        var player = builder.AddCharacter(playerPlatoon, "2-t", "same name", faction.Id, new(10, 0, 10), Quaternion.Identity, isLeader: true);
        var npc = builder.AddCharacter(npcPlatoon, "2-t", "same name", "20-t", new(1000, 0, 2000), Quaternion.Identity, isLeader: true);
        foreach (var source in new[] { player, npc })
        {
            source.GameState!.Floats["meitou brutality xp"] = BrutalityProgress.FromLevel(72).Experience;
            source.GameState.Strings["meitou head cut"] = "H1 2 1 0 0 0.1";
            source.GameState.Strings["meitou limb cuts"] = "L1 3 0.71 -0.3 0.4 0 0.32 0.2 -0.1";
        }
        builder.Game.UpdateDerived();
        return (builder.Game, data, player, npc);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    public void Integrated_gore_files_migrate_through_real_folder_load_and_clone_including_pending_NPCs(int headVersion, int torsoVersion)
    {
        var (save, data, player, npc) = LegacySave();
        string folder = Directory.CreateTempSubdirectory("brutality-legacy-save-").FullName;
        try
        {
            save.Write(folder);
            string HeadRow(SaveCharacter source) => $"H {source.Handle.C} {source.Handle.I} 4 3 2"
                + (headVersion == 1 ? "" : " 2") + " 1 2 3 0 0 1 0 1 0" + (headVersion == 3 ? " 1.2 0.8" : "");
            string TorsoRow(SaveCharacter source) => $"T {source.Handle.C} {source.Handle.I} " + (torsoVersion == 1 ? "0 1 0 -12" : "1 0 0 0 1");
            File.WriteAllLines(Path.Combine(folder, "head-wounds.meitou"), [$"MEITOU-HEAD-WOUNDS {headVersion}", "bad row", HeadRow(player), HeadRow(npc)]);
            File.WriteAllLines(Path.Combine(folder, "torso-wounds.meitou"), [$"MEITOU-TORSO-WOUNDS {torsoVersion}", TorsoRow(player), TorsoRow(npc)]);
            var loaded = SaveGame.Load(folder).Clone();
            var system = Gameplay();
            using var world = World(system);
            SaveLoader.Load(world, loaded, data);
            Assert.Equal(1, world.Characters.Count);
            Assert.Single(world.Saved!.Platoons);
            var rows = Decode(Write(system));
            Assert.Equal(2, rows.Count);
            foreach (var source in new[] { player, npc })
            {
                string key = SaveIdentityAdapter.KeyFor(source.Handle)!;
                var row = Assert.Single(rows, entry => entry.Key == key);
                Assert.Equal(BrutalityProgress.FromLevel(72).Experience, row.Experience);
                Assert.Equal(SevereHeadWoundKind.BrokenSkull, row.SevereHead.Kind);
                Assert.Equal(headVersion == 1 ? CombatWoundMechanism.Slash : CombatWoundMechanism.Thrust, row.SevereHead.Mechanism);
                Assert.Equal(headVersion == 3 ? 1.2f : 1, row.SevereHead.Placement!.Value.LengthScale);
                Assert.Equal(HeadCutMode.DetachedSplit, row.Head!.Value.Mode);
                Assert.Equal(torsoVersion == 2, row.Torso!.Value.FullBody);
                Assert.Equal(new[] { 0, 3 }, row.Limbs.Select(cut => cut.LimbSlot));
            }
            // A member that was only a stand-in resolves later using its complete saved handle.
            var cold = LoadedCold(loaded.Characters.Single(c => c.Handle == npc.Handle));
            world.Characters.Spawn(new(), cold, 0);
            system.Schedule(world);
            EqualState(Assert.Single(rows, entry => entry.Key == SaveIdentityAdapter.KeyFor(npc.Handle)),
                system.DurableFor(cold, SaveIdentityAdapter.KeyFor(npc.Handle)!)!);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_explicit_empty_DLL_sidecar_suppresses_stale_FCS_import_even_if_binding_happened_first(bool readBeforeBind)
    {
        var (_, _, source, _) = LegacySave();
        source.GameState!.Strings["meitou torso cut"] = "0 1 0 -12 0";
        var system = Gameplay();
        using var world = World(system);
        if (readBeforeBind) Read(system, Payload());
        var cold = LoadedCold(source); cold.IsPlayer = true;
        var id = world.Characters.Spawn(new(), cold, 0);
        system.Schedule(world);
        if (!readBeforeBind)
        {
            Assert.Equal(72, system.LevelOf(world, id), 5);
            Read(system, Payload());
        }
        system.Schedule(world);
        Assert.Equal(0, system.LevelOf(world, id));
        var row = system.DurableFor(cold, SaveIdentityAdapter.KeyFor(source.Handle)!)!;
        Assert.Null(row.Head); Assert.Null(row.Torso); Assert.Empty(row.Limbs);
        Assert.Equal(SevereHeadWoundKind.None, row.SevereHead.Kind);
        Assert.Equal(BrutalityProgress.FromLevel(72).Experience, source.GameState.Floats["meitou brutality xp"]);

        var later = Saved(source.Handle with { S = source.Handle.S + 1 });
        later.GameState!.Floats["meitou brutality xp"] = BrutalityProgress.MaximumExperience;
        var laterCold = LoadedCold(later); laterCold.IsPlayer = true;
        var laterId = world.Characters.Spawn(new(), laterCold, 0);
        system.Schedule(world);
        Assert.Equal(0, system.LevelOf(world, laterId));
    }

    [Fact]
    public void An_existing_DLL_sidecar_is_authoritative_before_Load_reads_any_legacy_state()
    {
        var (save, data, player, _) = LegacySave();
        save.ModData[LegacyGoreSave.ModId] = [];
        var system = Gameplay();
        using var world = World(system);
        SaveLoader.Load(world, save, data);
        string key = SaveIdentityAdapter.KeyFor(player.Handle)!;
        // Materialize the normal zero-level actor without running a simulation tick; an absent sidecar row is a default, too.
        Assert.Equal(0, system.LevelOf(world, world.Characters.IdOf(0)));
        var row = Assert.Single(Decode(Write(system)));
        Assert.Equal(key, row.Key); Assert.Equal(0, row.Experience);
        Assert.Null(row.Head); Assert.Empty(row.Limbs);
        var authoritative = State(key, 33);
        Read(system, Payload(authoritative));
        EqualState(authoritative, Assert.Single(Decode(Write(system))));
    }

    [Fact]
    public void Invalid_legacy_fields_and_ambiguous_old_handles_never_create_an_unvalidated_cut()
    {
        var (save, _, player, npc) = LegacySave();
        player.GameState!.Floats["meitou brutality xp"] = float.NaN;
        player.GameState.Strings["meitou head cut"] = "H1 2 NaN 0 0 0";
        player.GameState.Strings["meitou torso cut"] = "0 0 0 0 0";
        player.GameState.Strings["meitou limb cuts"] = "L1 0 0.5 0 0 0 0.6 0 0";
        npc.Handle = player.Handle with { S = player.Handle.S + 1 };
        string folder = Directory.CreateTempSubdirectory("brutality-ambiguous-save-").FullName;
        try
        {
            File.WriteAllLines(Path.Combine(folder, "head-wounds.meitou"), ["MEITOU-HEAD-WOUNDS 3",
                $"H {player.Handle.C} {player.Handle.I} 4 3 0 1"]);
            File.WriteAllLines(Path.Combine(folder, "torso-wounds.meitou"), ["MEITOU-TORSO-WOUNDS 2",
                $"T {player.Handle.C} {player.Handle.I} 0 1 0 -12 0"]);
            LegacyGoreSave.Loaded(SaveFolder.Open(folder), save);
            var rows = LegacyGoreSave.Read(save);
            Assert.Equal(2, rows.Count);
            var row = Assert.Single(rows, entry => entry.Key == SaveIdentityAdapter.KeyFor(player.Handle));
            Assert.Equal(0, row.Experience); Assert.Null(row.Head); Assert.Null(row.Torso); Assert.Empty(row.Limbs);
            Assert.All(rows, entry => Assert.Equal(SevereHeadWoundKind.None, entry.SevereHead.Kind));
            Assert.All(rows, entry => Assert.Null(entry.Torso));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void Writing_migrated_state_gives_the_continuing_world_and_reload_the_same_hashed_authority()
    {
        var (save, data, _, npc) = LegacySave();
        var original = Gameplay();
        using var world = World(original);
        SaveLoader.Load(world, save, data);
        byte[] bytes = Write(original);
        static ulong Hash(BrutalityGameplaySystem system)
        {
            var hash = new StateHasher(); system.Hash(ref hash); return hash.Value;
        }
        ulong afterWrite = Hash(original);
        var copy = save.Clone(); copy.ModData[LegacyGoreSave.ModId] = [];
        var restored = Gameplay();
        using var loadedWorld = World(restored);
        SaveLoader.Load(loadedWorld, copy, data);
        Read(restored, bytes);
        Assert.Equal(afterWrite, Hash(restored));
        Assert.Equal(2, Decode(bytes).Count);
        var a = LoadedCold(npc); var b = LoadedCold(copy.Characters.Single(c => c.Handle == npc.Handle));
        world.Characters.Spawn(new(), a, 0); loadedWorld.Characters.Spawn(new(), b, 0);
        original.Schedule(world); restored.Schedule(loadedWorld);
        Assert.Equal(BrutalityProgress.FromLevel(72).Experience, original.DurableFor(a, SaveIdentityAdapter.KeyFor(npc.Handle)!)!.Experience);
        Assert.Equal(Hash(original), Hash(restored));
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Standalone_cut_player_corpses_materialize_once_without_rejoining_a_squad_and_an_empty_DLL_payload_wins(bool torso, bool emptyDll)
    {
        var (save, data, player, _) = LegacySave();
        player.Medical!.Bools["dead"] = true;
        player.Platoon.Record.Bools[torso ? "meitou torso corpse" : "meitou head corpse"] = true;
        if (torso) player.GameState!.Strings["meitou torso cut"] = "0 1 0 -12 0";
        if (emptyDll)
        {
            using var bytes = new MemoryStream(); using (var writer = new BinaryWriter(bytes, System.Text.Encoding.UTF8, true))
            { var payload = Payload(); writer.Write(0x444D544Du); writer.Write(1); writer.Write(1); writer.Write("gore"); writer.Write(payload.Length); writer.Write(payload); }
            save.ModData[LegacyGoreSave.ModId] = bytes.ToArray();
        }
        var system = Gameplay(); using var world = World(system);
        var loaded = SaveLoader.Load(world, save, data);
        if (emptyDll) { Assert.Equal(0, world.Characters.Count); return; }
        Assert.Equal(1, world.Characters.Count);
        var id = Assert.Single(loaded.Characters); var cold = world.Characters.Cold(id.Slot)!;
        Assert.True(cold.Medical!.Dead); Assert.True(cold.IsPlayer); Assert.Equal(-1, cold.SquadId);
        Assert.Equal(player.Handle, cold.Save!.Source.Handle);
        Assert.DoesNotContain(world.Squads.All.SelectMany(squad => squad.Members), member => member == id);
        CorpseSaveAdapter.Materialize(world, save, data, null, loaded);
        Assert.Equal(1, world.Characters.Count);
        system.SlowWorld(world);
        var actor = Assert.Single(system.ActorViews, actor => actor.Character == id);
        Assert.NotNull(actor.Head); Assert.Equal(torso, actor.Torso is not null);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    public void Old_carry_side_one_retains_positive_ownership_and_side_zero_migrates_to_the_negative_proxy(int side, bool released)
    {
        var (save, data, player, npc) = LegacySave(); npc.Medical!.Bools["dead"] = true;
        npc.GameState!.Strings["meitou torso cut"] = "0 1 0 -12 0";
        if (side == 0)
        {
            npc.GameState.Strings.Remove("meitou torso cut");
            npc.GameState.Strings["meitou ragdoll"] = "T2 0 1 0 -12 0 | 1 7 1 1 12 3 0 0 0 1 | 1 7 1 1 2 3 0 0 0 1";
        }
        npc.GameState.Strings[released ? "meitou torso positive release" : "meitou torso positive"] = released ? "7 8 9" : "7 8 9 .4";
        player.GameState!.Bools["carrying"] = true; player.GameState.Ints["meitou carry torso side"] = side; npc.Handle.Write(player.GameState, "carrying");
        var system = Gameplay(); var proxies = new BodyFragmentProxySystem();
        using var world = new SimWorld(new WorldSettings { PublishSnapshots = false }, new OpenGroundWalkability((_, _) => 0), [system, proxies]);
        SaveLoader.Load(world, save, data);
        Assert.Equal(2, world.Characters.Count);
        var holder = Enumerable.Range(0, world.Characters.HighWater).Select(world.Characters.Cold).Single(cold => cold?.Save?.Source.Handle == player.Handle)!;
        var body = Enumerable.Range(0, world.Characters.HighWater).Select(world.Characters.Cold).Single(cold => cold?.Save?.Source.Handle == npc.Handle)!;
        Assert.Equal(side == 0, holder.Save!.CarryPartner.IsNull);
        Assert.True(CorpseSaveAdapter.TryTakePositive(body, out var placement));
        Assert.Equal(new Vector3(7, 8, 9), placement.Position); Assert.Equal(released ? 0 : .4f, placement.Yaw, 5);
        using var stream = new MemoryStream(); proxies.Write(stream);
        using var document = System.Text.Json.JsonDocument.Parse(stream.ToArray());
        var rows = document.RootElement.GetProperty("Halves"); Assert.Equal(side == 0 ? 1 : 0, rows.GetArrayLength());
        if (side == 0)
        {
            Assert.Equal(SaveIdentityAdapter.KeyFor(npc.Handle), rows[0].GetProperty("Owner").GetString());
            Assert.Equal(SaveIdentityAdapter.KeyFor(player.Handle), rows[0].GetProperty("Carrier").GetString());
        }
    }

    [Fact]
    public void An_unwritten_cut_corpse_is_captured_by_the_native_writer_and_reuses_its_full_handle_on_the_next_capture()
    {
        var data = SyntheticTown.Data(SyntheticTown.Database(anatomy: true));
        var system = Gameplay(); using var world = World(system); world.Player.Faction = data.PlayerFaction;
        var cold = Cold(); cold.RecordId = "2-t"; cold.Faction = data.FactionIndex("20-t");
        var id = world.Characters.Spawn(new(), cold, 0); system.Schedule(world);
        Assert.True(system.ApplyDebugCut(world, id, GorePreviewKind.Torso)); system.SlowWorld(world);
        var first = SaveCapture.Capture(world, data, new(1, 12, 0));
        var original = Assert.Single(first.Characters);
        Assert.True(original.Platoon.Record.Bools.GetValueOrDefault("meitou torso corpse"));
        Assert.Equal(SaveIdentityAdapter.KeyFor(original.Handle), system.SaveKeyOf(cold));
        var again = SaveCapture.Capture(world, data, new(1, 12, 1), prepared: new SaveBuilder(first.Clone()));
        Assert.Equal(original.Handle, Assert.Single(again.Characters).Handle);
        var restored = Gameplay(); using var loadedWorld = World(restored);
        SaveLoader.Load(loadedWorld, again, data);
        Assert.Equal(1, loadedWorld.Characters.Count);
        restored.SlowWorld(loadedWorld);
        Assert.NotNull(Assert.Single(restored.ActorViews).Torso);
    }

    static SimWorld OrderWorld(BrutalityGameplaySystem gameplay, BodyFragmentProxySystem proxies)
    {
        var walk = new OpenGroundWalkability((_, _) => 0);
        return new(new WorldSettings { PublishSnapshots = false }, walk,
            [gameplay, new MovementSystem(new PathService(walk, true)), new CarrySystem(BaseGame, BodyOptions.Default, 1), proxies]);
    }
    static byte[] Envelope(params (string Name, byte[] Data)[] entries)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
        writer.Write(0x444D544Du); writer.Write(1); writer.Write(entries.Length);
        foreach (var (name, data) in entries) { writer.Write(name); writer.Write(data.Length); writer.Write(data); }
        writer.Flush(); return stream.ToArray();
    }
    static byte[] ProxyBytes(BodyFragmentProxySystem proxies) { using var stream = new MemoryStream(); proxies.Write(stream); return stream.ToArray(); }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Legacy_half_pickup_loot_and_inventory_queues_keep_the_exact_side_detail_and_position_through_a_native_save(int legacyKind)
    {
        var (save, data, player, npc) = LegacySave(); npc.Medical!.Bools["dead"] = true;
        npc.GameState!.Strings["meitou torso cut"] = "0 1 0 -12 0";
        var task = legacyKind is 0 or 2 ? TaskType.LIFT_PERSON_PLAYER_ORDER : TaskType.LOOT_TARGET;
        var ai = player.Ai!;
        ai.Ints["tjob0"] = (int)TaskType.MOVE_CUS_ORDERED; ai.Vector3s["tjobP0"] = new(11, 0, 12);
        ai.Ints["tjob1"] = (int)task; ai.Ints["meitou tjobK1"] = legacyKind; npc.Handle.Write(ai, "tjobT1");
        ai.Ints["tjob2"] = (int)TaskType.MOVE_CUS_ORDERED; ai.Vector3s["tjobP2"] = new(21, 0, 22);
        var gameplay = Gameplay(); var proxies = new BodyFragmentProxySystem(); using var world = OrderWorld(gameplay, proxies);
        var loaded = SaveLoader.Load(world, save, data); proxies.Inputs(world, []);
        var holder = loaded.Characters.Single(id => world.Characters.Cold(id.Slot)?.Save?.Source.Handle == player.Handle);
        var owner = loaded.Characters.Single(id => world.Characters.Cold(id.Slot)?.Save?.Source.Handle == npc.Handle);
        int side = legacyKind >= 2 ? 0 : 1, detail = legacyKind is 1 or 4 ? 1 : 0;
        AssertQueue(world, proxies, holder, owner, side, detail, task);

        var captured = SaveCapture.Capture(world, data, new(1, 12, 0), loaded);
        byte[] gore = Write(gameplay), proxyData = ProxyBytes(proxies);
        captured.ModData[LegacyGoreSave.ModId] = Envelope(("gore", gore), ("body-fragments", proxyData));
        var again = Gameplay(); var againProxies = new BodyFragmentProxySystem(); using var reloaded = OrderWorld(again, againProxies);
        var restored = SaveLoader.Load(reloaded, captured, data);
        Read(again, gore); againProxies.Read(new MemoryStream(proxyData)); againProxies.Inputs(reloaded, []);
        var nextHolder = restored.Characters.Single(id => again.SaveKeyOf(reloaded.Characters.Cold(id.Slot)!) == gameplay.SaveKeyOf(world.Characters.Cold(holder.Slot)!));
        var nextOwner = restored.Characters.Single(id => again.SaveKeyOf(reloaded.Characters.Cold(id.Slot)!) == gameplay.SaveKeyOf(world.Characters.Cold(owner.Slot)!));
        AssertQueue(reloaded, againProxies, nextHolder, nextOwner, side, detail, task);

        static void AssertQueue(SimWorld value, BodyFragmentProxySystem system, CharacterId actor, CharacterId source, int side, int detail, TaskType task)
        {
            var queue = value.Characters.Cold(actor.Slot)!.OrderQueue;
            Assert.Equal(3, queue.Count); Assert.Equal(new Vector3(11, 0, 12), queue[0].Point); Assert.Equal(new Vector3(21, 0, 22), queue[2].Point);
            Assert.Equal(task, queue[1].Row); Assert.Equal(detail, queue[1].Owner.SaveKind(queue[1]));
            if (side == 0) Assert.Equal(source, queue[1].Target);
            else { Assert.True(system.TryProxy(source, 1, out var proxy)); Assert.Equal(proxy, queue[1].Target); Assert.NotEqual(source, proxy); }
        }
    }

    [Fact]
    public void A_queued_negative_half_waits_without_losing_its_native_queue_and_can_be_saved_before_the_corpse_materializes()
    {
        var (_, _, player, npc) = LegacySave(); npc.GameState!.Strings["meitou torso cut"] = "0 1 0 -12 0";
        string holderKey = SaveIdentityAdapter.KeyFor(player.Handle)!, bodyKey = SaveIdentityAdapter.KeyFor(npc.Handle)!;
        var row = new BodyFragmentProxySystem.SavedHalfQueue(holderKey,
            [new(0, bodyKey, 1, (int)TaskType.LIFT_PERSON_PLAYER_ORDER, 0, 0, 0, 0)]);
        byte[] bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { SchemaVersion = 1, Halves = Array.Empty<BodyFragmentProxySystem.SavedHalf>(), Queues = new[] { row } });
        var gameplay = Gameplay(); var proxies = new BodyFragmentProxySystem(); using var world = OrderWorld(gameplay, proxies);
        proxies.Read(new MemoryStream(bytes));
        var cold = LoadedCold(player); cold.IsPlayer = true;
        cold.SavedOrders = [new(TaskType.LIFT_PERSON_PLAYER_ORDER, Vector3.Zero, npc.Handle, null, null, 0, false)];
        world.Characters.Spawn(new(), cold, 0); gameplay.Schedule(world); proxies.Inputs(world, []);
        AccessTools.Method(typeof(OrderSave), "Restore").Invoke(null, [world, 0, cold]);
        Assert.NotNull(cold.SavedOrders); Assert.Empty(cold.OrderQueue);
        using (var saved = System.Text.Json.JsonDocument.Parse(ProxyBytes(proxies)))
            Assert.Equal(bodyKey, saved.RootElement.GetProperty("Queues")[0].GetProperty("Orders")[0].GetProperty("Target").GetString());
        var body = LoadedCold(npc); body.Medical!.Butcher(); var id = world.Characters.Spawn(new(), body, 0);
        gameplay.Schedule(world); proxies.Inputs(world, []);
        Assert.Null(cold.SavedOrders); var order = Assert.Single(cold.OrderQueue);
        Assert.True(proxies.TryProxy(id, 1, out var target)); Assert.Equal(target, order.Target);
    }

    [Fact]
    public void A_pending_half_order_and_deferred_carrier_follow_the_new_full_handle_when_the_player_changes_squad()
    {
        var (save, data, player, _) = LegacySave();
        var gameplay = Gameplay(); var proxies = new BodyFragmentProxySystem(); using var world = OrderWorld(gameplay, proxies);
        var loaded = SaveLoader.Load(world, save, data);
        var id = loaded.Characters.Single(character => world.Characters.Cold(character.Slot)?.Save?.Source.Handle == player.Handle);
        var cold = world.Characters.Cold(id.Slot)!; string prior = gameplay.SaveKeyOf(cold)!;
        const string missing = "hand:v1:5:9999:1:0:22";
        var half = new BodyFragmentProxySystem.SavedHalf(missing, 1, prior, 100, 0, 200, 0);
        var queue = new BodyFragmentProxySystem.SavedHalfQueue(prior, [new(0, missing, 1, (int)TaskType.LIFT_PERSON_PLAYER_ORDER, 0, 0, 0, 0)]);
        proxies.Read(new MemoryStream(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { SchemaVersion = 1, Halves = new[] { half }, Queues = new[] { queue } })));
        world.Squads.Find(cold.SquadId)!.Members.Remove(id);
        var squad = new Squad { Id = world.Squads.NextId(), TemplateId = "10-t", Faction = data.PlayerFaction, Name = "Moved" };
        world.Squads.Add(squad); squad.Members.Add(id); squad.Leader = id; cold.SquadId = squad.Id;
        SaveCapture.Capture(world, data, new(1, 12, 0), loaded);
        string next = gameplay.SaveKeyOf(cold)!; Assert.NotEqual(prior, next);
        using var document = System.Text.Json.JsonDocument.Parse(ProxyBytes(proxies));
        Assert.Equal(next, document.RootElement.GetProperty("Queues")[0].GetProperty("Owner").GetString());
        Assert.Equal(next, document.RootElement.GetProperty("Halves")[0].GetProperty("Carrier").GetString());
        Assert.Equal(missing, document.RootElement.GetProperty("Queues")[0].GetProperty("Orders")[0].GetProperty("Target").GetString());
    }

}

