using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Wounds;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Saving;
using Meitou.Tests.Simulation;

namespace Meitou.Tests.Wounds;

[Collection("SevereHeadWoundCapture")]
public class SevereHeadWoundsTests
{
    static readonly CharacterId Original = new(3, 1), Reloaded = new(17, 2);
    static readonly HeadWoundPlacement Placement = new(new(0.37f, 18.72f, 0.91f), Vector3.UnitZ, Vector3.UnitX);

    [Fact]
    public void Save_load_preserves_each_form_profile_opening_frame_preopening_hits_and_impact_mechanism()
    {
        var before = new SevereHeadWounds();
        SevereHeadWoundState[] states =
        [
            new(3),
            new(4, SevereHeadWoundKind.JawLost, Mechanism: CombatWoundMechanism.Crush),
            new(5, SevereHeadWoundKind.Scalp, 1, Placement, CombatWoundMechanism.Thrust),
            new(8, SevereHeadWoundKind.BrokenSkull, 5, Placement, CombatWoundMechanism.Crush),
        ];
        for (int i = 0; i < states.Length; i++) Assert.True(before.Set(new(i, 1), states[i]));
        var lines = before.Write(id => (20, id.Slot + 100)).ToArray();
        var after = new SevereHeadWounds();
        var result = after.Read(lines, h => h.C == 20 ? new(h.I + 40, 9) : null);
        Assert.True(result.SupportedVersion);
        Assert.Equal(4, result.Restored);
        Assert.Equal(0, result.Pending);
        for (int i = 0; i < states.Length; i++)
        {
            Assert.Equal(states[i], after.Of(new(i + 140, 9)));
            Assert.Null(after.Of(new(i, 1)));
        }
    }

    [Fact]
    public void Unspawned_saved_characters_survive_another_save_and_resolve_when_their_handle_appears()
    {
        var before = new SevereHeadWounds();
        var state = new SevereHeadWoundState(6, SevereHeadWoundKind.BrokenSkull, 4, Placement, CombatWoundMechanism.Thrust);
        before.Set(Original, state);
        var first = before.Write(_ => (30, 7)).ToArray();
        var during = new SevereHeadWounds();
        Assert.Equal(1, during.Read(first, _ => null).Pending);
        Assert.Null(during.Of(Original));
        var second = during.Write(_ => null).ToArray();
        Assert.Equal(first, second);
        Assert.Equal(1, during.Resolve(h => h == (30, 7) ? Reloaded : null));
        Assert.Equal(0, during.PendingCount);
        Assert.Equal(state, during.Of(Reloaded));
    }

    [Fact]
    public void Runtime_slot_reuse_does_not_attach_an_old_character_wound_to_the_new_generation()
    {
        var store = new SevereHeadWounds();
        store.Set(Original, new(4, SevereHeadWoundKind.JawLost));
        var replacement = new CharacterId(Original.Slot, Original.Generation + 1);
        Assert.Null(store.Of(replacement));
        // Save capture gives no handle to the removed generation.
        Assert.Equal([SevereHeadWounds.Header], store.Write(id => id == replacement ? (1, 2) : null).ToArray());
    }

    [Fact]
    public void New_live_damage_wins_over_an_older_unresolved_save_record()
    {
        var store = new SevereHeadWounds();
        store.Read([SevereHeadWounds.Header, "H 1 8 3 0 0 1"], _ => null);
        var newer = new SevereHeadWoundState(5, SevereHeadWoundKind.BrokenSkull, 3, Placement);
        store.Set(Reloaded, newer);
        Assert.Equal(1, store.Resolve(_ => Reloaded));
        Assert.Equal(newer, store.Of(Reloaded));
    }

    [Fact]
    public void Missing_sidecar_legacy_wound_rows_and_unknown_versions_leave_no_severe_state()
    {
        string[][] inputs =
        [
            [],
            ["7 9 -1 8 0.5 0 0 1 1 0 0.2 1 1.2 4 4 1 120"],
            ["MEITOU-HEAD-WOUNDS 3", "H 1 1 4 3 5 1"],
        ];
        foreach (var lines in inputs)
        {
            var store = new SevereHeadWounds();
            store.Set(Original, new(4, SevereHeadWoundKind.JawLost));
            Assert.False(store.Read(lines, _ => Original).SupportedVersion);
            Assert.Null(store.Of(Original));
            Assert.Equal(0, store.PendingCount);
        }
    }

    [Fact]
    public void Malformed_rows_cannot_corrupt_a_valid_character_or_abort_other_records()
    {
        string[] lines =
        [
            SevereHeadWounds.Header,
            "H 8 1 4 3 2 1", // Valid, followed by a malformed duplicate.
            "H 8 1 -1 3 2 1",
            "H -1 1 4 3 2 1",
            "H 8 2 4 3 6 1",
            "H 8 3 4 9 0 1",
            "H 8 4 4 1 2 1",
            "H 8 5 4 3 2 1 NaN 1 2 0 0 1 1 0 0",
            "H 8 6 4 3 2 1 1 2 3 0 0 0 1 0 0",
            "H 8 7 4 3 2 1 1 2 3 0 0 1 0 0 1", // Parallel frame vectors.
            "H 8 8 many 3 2 1",
            "H 8 9 4 2 1 1",
        ];
        var store = new SevereHeadWounds();
        var result = store.Read(lines, h => new(h.I, 1));
        Assert.Equal(2, result.Restored);
        Assert.Equal(9, result.Rejected);
        Assert.Equal(new SevereHeadWoundState(4, SevereHeadWoundKind.BrokenSkull, 2), store.Of(new(1, 1)));
        Assert.Equal(new SevereHeadWoundState(4, SevereHeadWoundKind.Scalp, 1), store.Of(new(9, 1)));
        Assert.Equal(2, store.All.Count());
    }

    [Fact]
    public void Renderer_flags_round_trip_jaw_scalp_and_skull_without_limb_bits()
    {
        SevereHeadWoundState[] states = [new(4, SevereHeadWoundKind.JawLost), new(5, SevereHeadWoundKind.Scalp, 2, Mechanism: CombatWoundMechanism.Thrust), new(9, SevereHeadWoundKind.BrokenSkull, 5, Mechanism: CombatWoundMechanism.Crush)];
        foreach (var state in states)
            Assert.Equal(state, SevereHeadWoundState.FromRendererFlags(state.HeavyHits, state.RendererFlags | 0xFu));
        Assert.Equal(CombatWoundMechanism.Slash, SevereHeadWoundState.FromRendererFlags(4, 1u << 7).Mechanism);
    }
    [Fact]
    public void Loaded_npc_keeps_its_state_while_unloaded_and_rebinds_to_the_new_runtime_generation()
    {
        var store = new SevereHeadWounds();
        var state = new SevereHeadWoundState(7, SevereHeadWoundKind.BrokenSkull, 4, Placement);
        var source = new SevereHeadWounds();
        source.Set(Original, state);
        store.Read(source.Write(_ => (3, 8)), _ => Original);
        Assert.Empty(store.Refresh(_ => null, _ => false));
        Assert.Null(store.Of(Original));
        Assert.Equal(1, store.PendingCount);
        var unloadedSave = store.Write(_ => null, h => h == (3, 8)).ToArray();
        Assert.Equal(source.Write(_ => (3, 8)).ToArray(), unloadedSave);
        Assert.Equal([Reloaded], store.Refresh(h => h == (3, 8) ? Reloaded : null));
        Assert.Equal(state, store.Of(Reloaded));
        Assert.Null(store.Of(Original));
        Assert.Equal(0, store.PendingCount);
    }

    [Fact]
    public void Captured_live_identity_wins_a_collision_with_an_old_binding_and_pending_record()
    {
        var store = new SevereHeadWounds();
        store.Read([SevereHeadWounds.Header, "H 4 2 3 0 0 1", "H 4 3 2 0 0 1"], h => h.I == 2 ? Original : null);
        var newer = new SevereHeadWoundState(9, SevereHeadWoundKind.BrokenSkull, 5, Placement);
        store.Set(Reloaded, newer);
        // The current capture is authoritative even before Refresh has seen the new runtime generation.
        var lines = store.Write(id => id == Reloaded ? (4, 2) : null).ToArray();
        Assert.Equal(3, lines.Length); // Header, one current character, and one still-unspawned character.
        var reread = new SevereHeadWounds();
        reread.Read(lines, h => h == (4, 2) ? Reloaded : null);
        Assert.Equal(newer, reread.Of(Reloaded));
        Assert.Equal(1, reread.PendingCount);
        store.Refresh(h => h == (4, 2) ? Reloaded : null);
        Assert.Equal(newer, store.Of(Reloaded));
        Assert.Null(store.Of(Original));
    }

    [Fact]
    public void A_new_capture_handle_replaces_the_remembered_handle_without_writing_a_duplicate()
    {
        var store = new SevereHeadWounds();
        var state = new SevereHeadWoundState(4, SevereHeadWoundKind.JawLost);
        store.Set(Original, state);
        Assert.True(store.Bind(Original, (1, 1)));
        var lines = store.Write(_ => (9, 3)).ToArray();
        Assert.Equal([SevereHeadWounds.Header, "H 9 3 4 1 0 1"], lines);
    }

    [Fact]
    public void Newly_saved_live_npc_is_not_detached_just_because_its_handle_is_not_in_the_load_resolver()
    {
        var store = new SevereHeadWounds();
        var state = new SevereHeadWoundState(4, SevereHeadWoundKind.Scalp, 2);
        store.Set(Original, state);
        Assert.True(store.Bind(Original, (7, 6)));
        Assert.Empty(store.Refresh(_ => null, id => id == Original));
        Assert.Equal(state, store.Of(Original));
        Assert.Equal(0, store.PendingCount);
    }

    [Fact]
    public void Removed_saved_identities_are_filtered_while_unloaded_identities_are_retained()
    {
        var store = new SevereHeadWounds();
        store.Read([SevereHeadWounds.Header, "H 2 1 4 1 0 1", "H 2 2 4 3 4 1"], h => h.I == 1 ? Original : null);
        // A removed live identity can leave a remembered binding, while the second character is merely unloaded.
        var lines = store.Write(_ => null, h => h == (2, 2)).ToArray();
        Assert.Equal([SevereHeadWounds.Header, "H 2 2 4 3 4 1"], lines);
        store.Remove(Original);
        Assert.Null(store.Of(Original));
    }

    [Fact]
    public void Binding_a_current_generation_retires_the_old_runtime_identity_for_the_same_save_handle()
    {
        var store = new SevereHeadWounds();
        store.Set(Original, new(3));
        store.Bind(Original, (5, 1));
        var newer = new SevereHeadWoundState(5, SevereHeadWoundKind.BrokenSkull, 3);
        store.Set(Reloaded, newer);
        Assert.True(store.Bind(Reloaded, (5, 1)));
        Assert.Null(store.Of(Original));
        Assert.Equal(newer, store.Of(Reloaded));
        Assert.Equal([SevereHeadWounds.Header, "H 5 1 5 3 3 1"], store.Write(_ => null).ToArray());
    }

    [Fact]
    public void Cold_character_records_preserve_unloaded_fresh_npcs_and_current_capture_wins_over_stale_cold_data()
    {
        var store = new SevereHeadWounds();
        store.Read([SevereHeadWounds.Header, "H 8 1 2 0 0 1"], _ => null);
        var coldNpc = new SevereHeadWoundState(5, SevereHeadWoundKind.Scalp, 2, Placement);
        var stalePlayer = new SevereHeadWoundState(4, SevereHeadWoundKind.BrokenSkull, 0);
        var livePlayer = new SevereHeadWoundState(7, SevereHeadWoundKind.BrokenSkull, 3, Placement);
        store.Set(Original, livePlayer);
        var lines = store.Write(id => id == Original ? (8, 2) : null, h => h.C == 8 && h.I is 1 or 2,
            [((8, 1), coldNpc), ((8, 2), stalePlayer), ((9, 9), coldNpc)]).ToArray();
        Assert.Equal(3, lines.Length);
        var after = new SevereHeadWounds();
        after.Read(lines, h => new(h.I, 20));
        Assert.Equal(coldNpc, after.Of(new(1, 20)));
        Assert.Equal(livePlayer, after.Of(new(2, 20)));
    }

    [Fact]
    public void Pruning_obsolete_unbound_runtime_ids_keeps_active_bound_and_pending_states()
    {
        var store = new SevereHeadWounds();
        store.Read([SevereHeadWounds.Header, "H 6 1 4 1 0 1", "H 6 2 4 2 1 1"], h => h.I == 1 ? Original : null);
        var oldGenerated = new CharacterId(30, 1);
        var newGenerated = new CharacterId(30, 2);
        var generatedState = new SevereHeadWoundState(5, SevereHeadWoundKind.BrokenSkull, 5, Placement);
        store.Set(oldGenerated, generatedState);
        store.Set(newGenerated, generatedState);
        Assert.Equal(1, store.PruneUnboundInactive(id => id == newGenerated));
        Assert.Null(store.Of(oldGenerated));
        Assert.Equal(generatedState, store.Of(newGenerated));
        Assert.Equal(new SevereHeadWoundState(4, SevereHeadWoundKind.JawLost), store.Of(Original));
        Assert.Equal(1, store.PendingCount);
        Assert.Equal(0, store.PruneUnboundInactive(id => id == newGenerated));
        Assert.Equal(3, store.Write(_ => null).Count());
    }
    [Fact]
    public void Imported_sidecar_discards_omitted_npc_before_its_old_handle_is_reused()
    {
        var store = new SevereHeadWounds();
        var result = store.Read([SevereHeadWounds.Header, "H 9 1 4 1 0 1", "H 9 2 5 3 4 1"],
            h => h == (9, 1) ? Original : null, h => h == (9, 1));
        Assert.True(result.SupportedVersion);
        Assert.Equal(1, result.Restored);
        Assert.Equal(0, result.Pending);
        Assert.Equal(0, result.Rejected);
        Assert.Equal(0, store.Resolve(h => h == (9, 1) ? Original : h == (9, 2) ? Reloaded : null));
        Assert.Null(store.Of(Reloaded));
        Assert.Equal([SevereHeadWounds.Header, "H 9 1 4 1 0 1"], store.Write(id => id == Original ? (9, 1) : null).ToArray());
    }
    [Fact]
    public void Captured_offscreen_npc_keeps_its_cold_wound_under_the_exact_written_character_handle()
    {
        var data = SyntheticTown.Data(SyntheticTown.Database());
        using var world = new Meitou.Simulation.World(new WorldSettings { Seed = 11, PublishSnapshots = false }, new OpenGroundWalkability(SyntheticTown.Ground), []);
        world.Player.Faction = data.PlayerFaction;
        var state = new SevereHeadWoundState(6, SevereHeadWoundKind.BrokenSkull, 4, Placement, CombatWoundMechanism.Thrust);
        var cold = new CharacterCold { Name = "Injured offscreen guard", RecordId = "2-t", Faction = data.FactionIndex("20-t"), SevereHeadWound = state };
        var stored = new StoredSquad { Name = "Stored guards" };
        stored.Members.Add(new(cold, SyntheticTown.Centre, 0.4f, true));
        world.Platoons.Add(new Platoon
        {
            Id = world.Platoons.NextId(), Origin = 0, TemplateId = "12-t", Key = 7,
            Faction = cold.Faction, State = PlatoonState.Unloaded, Stored = stored,
        });
        var save = SaveCapture.Capture(world, data, new SaveClock(1, 12, 0));
        var coldHandles = SaveCapture.LastColdHandles;
        Assert.NotNull(coldHandles);
        var captured = Assert.Single(coldHandles);
        Assert.Same(cold, captured.Key);
        Assert.Equal(Assert.Single(save.Characters).Handle, captured.Value);
        var liveHandles = SaveCapture.LastHandles;
        Assert.NotNull(liveHandles);
        Assert.Empty(liveHandles);
        var retained = save.Characters.Select(c => (c.Handle.C, c.Handle.I)).ToHashSet();
        var store = new SevereHeadWounds();
        var snapshot = store.Write(_ => null, retained.Contains,
            coldHandles.Select(p => (Handle: (p.Value.C, p.Value.I), State: p.Key.SevereHeadWound!.Value))).ToArray();
        cold.SevereHeadWound = new(9, SevereHeadWoundKind.JawLost); // A later frame cannot mutate the already captured sidecar.
        var restored = new SevereHeadWounds();
        Assert.Equal(1, restored.Read(snapshot, _ => Reloaded, retained.Contains).Restored);
        Assert.Equal(state, restored.Of(Reloaded));
    }
    [Fact]
    public void Version_one_plain_and_placed_wounds_default_to_slash_and_upgrade_on_the_next_save()
    {
        var store = new SevereHeadWounds();
        var result = store.Read(["MEITOU-HEAD-WOUNDS 1", "H 5 1 3 0 0", "H 5 2 5 3 4 0.37 18.72 0.91 0 0 1 1 0 0"],
            h => h.I == 1 ? Original : null);
        Assert.True(result.SupportedVersion);
        Assert.Equal(1, result.Restored);
        Assert.Equal(1, result.Pending);
        Assert.Equal(new SevereHeadWoundState(3), store.Of(Original));
        var upgraded = store.Write(_ => (5, 1)).ToArray();
        Assert.Equal("MEITOU-HEAD-WOUNDS 2", upgraded[0]);
        Assert.Equal("H 5 1 3 0 0 1", upgraded[1]);
        var after = new SevereHeadWounds();
        var restored = after.Read(upgraded, h => h.I == 1 ? Original : Reloaded);
        Assert.Equal(2, restored.Restored);
        Assert.Equal(new SevereHeadWoundState(5, SevereHeadWoundKind.BrokenSkull, 4, Placement), after.Of(Reloaded));
    }

    [Fact]
    public void Version_two_rejects_unspecified_unknown_and_noninteger_mechanisms_without_erasing_valid_damage()
    {
        var store = new SevereHeadWounds();
        var result = store.Read([SevereHeadWounds.Header,
            "H 3 1 4 2 3 2", "H 3 1 4 2 3 0", "H 3 2 4 2 3 -1", "H 3 3 4 2 3 4", "H 3 4 4 2 3 Slash", "H 3 5 4 2 3 2147483648"],
            _ => Original);
        Assert.Equal(1, result.Restored);
        Assert.Equal(5, result.Rejected);
        Assert.Equal(new SevereHeadWoundState(4, SevereHeadWoundKind.Scalp, 3, Mechanism: CombatWoundMechanism.Thrust), store.Of(Original));
        Assert.False(store.Set(Reloaded, new(4, SevereHeadWoundKind.Scalp, Mechanism: CombatWoundMechanism.Unspecified)));
        Assert.Null(store.Of(Reloaded));
    }

    [Fact]
    public void Each_sidecar_version_accepts_only_its_own_plain_and_placed_record_shapes()
    {
        var legacy = new SevereHeadWounds();
        var oldResult = legacy.Read(["MEITOU-HEAD-WOUNDS 1", "H 5 1 4 2 2 1", "H 5 2 4 3 2 2 0.37 18.72 0.91 0 0 1 1 0 0", "H 5 3 4 2 2"],
            _ => Original);
        Assert.Equal(1, oldResult.Restored);
        Assert.Equal(2, oldResult.Rejected);
        var current = new SevereHeadWounds();
        var newResult = current.Read([SevereHeadWounds.Header, "H 5 1 4 2 2", "H 5 2 4 3 2 0.37 18.72 0.91 0 0 1 1 0 0", "H 5 3 4 2 2 3"],
            _ => Reloaded);
        Assert.Equal(1, newResult.Restored);
        Assert.Equal(2, newResult.Rejected);
        Assert.Equal(CombatWoundMechanism.Slash, legacy.Of(Original)!.Value.Mechanism);
        Assert.Equal(CombatWoundMechanism.Crush, current.Of(Reloaded)!.Value.Mechanism);
    }
}

// SaveCapture publishes global latest-capture maps, so their assertions run apart from other capture tests.
[CollectionDefinition("SevereHeadWoundCapture", DisableParallelization = true)]
public sealed class SevereHeadWoundCaptureCollection { }