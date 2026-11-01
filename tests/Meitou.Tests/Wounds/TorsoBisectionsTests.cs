using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Saving;
using Meitou.Simulation.Wounds;
using Meitou.Tests.Simulation;

namespace Meitou.Tests.Wounds;

[Collection("SevereHeadWoundCapture")]
public class TorsoBisectionsTests
{
    static readonly CharacterId Original = new(3, 1), Reloaded = new(17, 2);
    static readonly TorsoBisectionState Flat = new(new(0, 1, 0, -12.5f));
    static readonly TorsoBisectionState FullBody = new(new(1, 0, 0, -0.31415927f), true);
    static TorsoBisectionState Tilted
    {
        get
        {
            var normal = Vector3.Normalize(new Vector3(0.041234568f, 1, -0.03521432f));
            return new(new(normal, -Vector3.Dot(normal, new(0, 12.5f, 0))));
        }
    }

    [Fact]
    public void Save_load_preserves_the_exact_bind_plane_under_remapped_runtime_ids()
    {
        var source = new TorsoBisections();
        Assert.True(source.Set(Original, Tilted));
        var written = source.Write(_ => (12, 4)).ToArray();
        Assert.Equal("MEITOU-TORSO-WOUNDS 2", written[0]);
        Assert.Equal(8, written[1].Split(' ').Length);
        var after = new TorsoBisections();
        var result = after.Read(written, h => h == (12, 4) ? Reloaded : null);
        Assert.True(result.SupportedVersion);
        Assert.Equal(1, result.Restored);
        Assert.Equal(0, result.Pending);
        Assert.Equal(Tilted, after.Of(Reloaded));
        Assert.Null(after.Of(Original));
    }

    [Fact]
    public void Unloaded_saved_npc_keeps_its_plane_through_resave_and_new_runtime_generation()
    {
        var source = new TorsoBisections();
        source.Set(Original, FullBody);
        source.Bind(Original, (3, 8));
        Assert.Empty(source.Refresh(_ => null, _ => false));
        Assert.Null(source.Of(Original));
        Assert.Equal(1, source.PendingCount);
        var first = source.Write(_ => null, h => h == (3, 8)).ToArray();
        var after = new TorsoBisections();
        Assert.Equal(1, after.Read(first, _ => null).Pending);
        Assert.Equal(first, after.Write(_ => null).ToArray());
        Assert.Equal([Reloaded], after.Refresh(h => h == (3, 8) ? Reloaded : null));
        Assert.Equal(FullBody, after.Of(Reloaded));
        Assert.Equal(0, after.PendingCount);
        Assert.Null(after.Of(new(Original.Slot, Original.Generation + 1)));
    }

    [Fact]
    public void Current_capture_wins_over_stale_bound_and_cold_records_while_cold_state_updates_pending_npcs()
    {
        var store = new TorsoBisections();
        store.Read([TorsoBisections.Header, "T 4 2 0 1 0 -12.5 0", "T 4 3 0 1 0 -12.5 0"], h => h.I == 2 ? Original : null);
        store.Set(Reloaded, FullBody);
        var lines = store.Write(id => id == Reloaded ? (4, 2) : null, h => h.C == 4,
            [((4, 2), Flat), ((4, 3), FullBody), ((9, 9), Flat)]).ToArray();
        Assert.Equal(3, lines.Length);
        var after = new TorsoBisections();
        after.Read(lines, h => new(h.I, 8));
        Assert.Equal(FullBody, after.Of(new(2, 8)));
        Assert.Equal(FullBody, after.Of(new(3, 8)));
        Assert.Equal([Reloaded], store.Refresh(h => h == (4, 2) ? Reloaded : null));
        Assert.Null(store.Of(Original));
        Assert.Equal(FullBody, store.Of(Reloaded));
    }

    [Fact]
    public void Imported_omitted_and_permanently_deleted_handles_cannot_attach_to_reused_identities()
    {
        var store = new TorsoBisections();
        var read = store.Read([TorsoBisections.Header, "T 7 1 0 1 0 -12.5 0", "T 7 2 0 1 0 -12.5 0"],
            h => h == (7, 1) ? Original : null, h => h == (7, 1));
        Assert.Equal(1, read.Restored);
        Assert.Equal(0, read.Pending);
        Assert.Equal(0, store.Resolve(h => h == (7, 1) ? Original : Reloaded));
        Assert.Null(store.Of(Reloaded));
        Assert.Equal([TorsoBisections.Header], store.Write(_ => null, _ => false).ToArray());
        store.Remove(Original);
        Assert.Empty(store.All);
        Assert.Equal([TorsoBisections.Header], store.Write(_ => null).ToArray());
    }

    [Fact]
    public void Rebinding_retires_old_runtime_id_and_pruning_keeps_bound_pending_and_active_states()
    {
        var store = new TorsoBisections();
        store.Read([TorsoBisections.Header, "T 2 1 0 1 0 -12.5 0", "T 2 2 0 1 0 -12.5 0"], h => h.I == 1 ? Original : null);
        store.Set(Reloaded, Tilted);
        Assert.True(store.Bind(Reloaded, (2, 1)));
        Assert.Null(store.Of(Original));
        var oldGenerated = new CharacterId(30, 1);
        var liveGenerated = new CharacterId(30, 2);
        store.Set(oldGenerated, Flat);
        store.Set(liveGenerated, Tilted);
        Assert.Equal(1, store.PruneUnboundInactive(id => id == liveGenerated));
        Assert.Null(store.Of(oldGenerated));
        Assert.Equal(Tilted, store.Of(liveGenerated));
        Assert.Equal(Tilted, store.Of(Reloaded));
        Assert.Equal(1, store.PendingCount);
        Assert.Empty(store.Refresh(_ => null, _ => true));
        Assert.Equal(Tilted, store.Of(Reloaded));
        store.Clear();
        Assert.Empty(store.All);
        Assert.Equal(0, store.PendingCount);
    }

    [Fact]
    public void New_live_state_is_not_overwritten_when_an_older_pending_record_resolves()
    {
        var store = new TorsoBisections();
        store.Read([TorsoBisections.Header, "T 8 3 0 1 0 -12.5 0"], _ => null);
        store.Set(Reloaded, FullBody);
        Assert.Equal(1, store.Resolve(_ => Reloaded));
        Assert.Equal(FullBody, store.Of(Reloaded));
        Assert.Equal(0, store.PendingCount);
    }

    [Fact]
    public void Missing_unrelated_legacy_and_unknown_version_sidecars_leave_no_torso_state()
    {
        string[][] inputs = [[], ["T 1 1 0 1 0 -12.5 0"], ["MEITOU-HEAD-WOUNDS 3", "H 1 1 4 3 4 1"], ["MEITOU-TORSO-WOUNDS 3", "T 1 1 0 1 0 -12.5 0"]];
        foreach (var lines in inputs)
        {
            var store = new TorsoBisections();
            store.Set(Original, Flat);
            Assert.False(store.Read(lines, _ => Original).SupportedVersion);
            Assert.Empty(store.All);
            Assert.Equal(0, store.PendingCount);
        }
    }

    [Fact]
    public void Malformed_nonfinite_and_nonunit_planes_are_rejected_without_erasing_valid_records()
    {
        string[] bad = ["T 8 1 0 2 0 -25 0", "T -1 2 0 1 0 -12.5 0", "T 8 many 0 1 0 -12.5 0", "T 8 3 NaN 1 0 -12.5 0", "T 8 4 0 1 0 Infinity 0", "T 8 5 0 0 0 -12.5 0", "T 8 6 0 0.1 0 -12.5 0", "T 8 7 3.4028235E38 1 0 -12.5 0", "T 8 8 0 1 0 -12.5 extra 0"];
        var store = new TorsoBisections();
        var read = store.Read([TorsoBisections.Header, "T 8 1 0 1 0 -12.5 0", .. bad], h => new(h.I, 1));
        Assert.Equal(1, read.Restored);
        Assert.Equal(bad.Length, read.Rejected);
        Assert.Equal(Flat, store.Of(new(1, 1)));
        Assert.False(store.Set(CharacterId.None, Flat));
        Assert.False(store.Set(Reloaded, default));
        Assert.False(store.Set(Reloaded, new(new(0, 1, 0, float.NaN))));
        Assert.Single(store.All);
    }

    [Fact]
    public void Offscreen_stored_cold_state_is_captured_under_its_written_handle_and_snapshotted_before_mutation()
    {
        var data = SyntheticTown.Data(SyntheticTown.Database());
        using var world = new Meitou.Simulation.World(new WorldSettings { Seed = 11, PublishSnapshots = false }, new OpenGroundWalkability(SyntheticTown.Ground), []);
        world.Player.Faction = data.PlayerFaction;
        var cold = new CharacterCold { Name = "Stored guard", RecordId = "2-t", Faction = data.FactionIndex("20-t"), TorsoBisection = FullBody };
        var stored = new StoredSquad { Name = "Stored guards" };
        stored.Members.Add(new(cold, SyntheticTown.Centre, 0.4f, true));
        world.Platoons.Add(new Platoon { Id = world.Platoons.NextId(), Origin = 0, TemplateId = "12-t", Key = 7, Faction = cold.Faction, State = PlatoonState.Unloaded, Stored = stored });
        var save = SaveCapture.Capture(world, data, new SaveClock(1, 12, 0));
        var coldHandles = SaveCapture.LastColdHandles;
        Assert.NotNull(coldHandles);
        var captured = Assert.Single(coldHandles);
        Assert.Same(cold, captured.Key);
        Assert.Equal(Assert.Single(save.Characters).Handle, captured.Value);
        var retained = save.Characters.Select(c => (c.Handle.C, c.Handle.I)).ToHashSet();
        var source = new TorsoBisections();
        var snapshot = source.Write(_ => null, retained.Contains,
            coldHandles.Select(p => (Handle: (p.Value.C, p.Value.I), State: p.Key.TorsoBisection!.Value))).ToArray();
        cold.TorsoBisection = Flat;
        var after = new TorsoBisections();
        Assert.Equal(1, after.Read(snapshot, _ => Reloaded, retained.Contains).Restored);
        Assert.Equal(FullBody, after.Of(Reloaded));
    }
    [Fact]
    public void Sidecar_rejects_sideways_and_downward_planes_that_cannot_clip_rendered_torso_halves()
    {
        var store = new TorsoBisections();
        var read = store.Read([TorsoBisections.Header, "T 1 1 0 1 0 -12.5 0", "T 1 2 1 0 0 -12.5 0", "T 1 3 0 -1 0 12.5 0", "T 1 4 0.86608315 0.4999 0 -6.24875 0"],
            h => new(h.I, 1));
        Assert.True(read.SupportedVersion);
        Assert.Equal(1, read.Restored);
        Assert.Equal(3, read.Rejected);
        Assert.Equal(Flat, store.Of(new(1, 1)));
        Assert.Null(store.Of(new(2, 1)));
        Assert.Null(store.Of(new(3, 1)));
        Assert.Null(store.Of(new(4, 1)));
        Assert.False(store.Set(Reloaded, new(new(1, 0, 0, -12.5f))));
        Assert.Equal(2, store.Write(_ => (1, 1)).Count());
    }
    [Fact]
    public void Full_body_vertical_and_steep_planes_round_trip_without_transverse_normal_clamping()
    {
        var diagonalNormal = Vector3.Normalize(new Vector3(0.95f, 0.2f, -0.13f));
        TorsoBisectionState[] states = [FullBody, new(new(diagonalNormal, -Vector3.Dot(diagonalNormal, new(0.17f, 12.5f, -0.09f))), true), Flat];
        var source = new TorsoBisections();
        for (int i = 0; i < states.Length; i++) Assert.True(source.Set(new(i, 1), states[i]));
        var lines = source.Write(id => (12, id.Slot + 1)).ToArray();
        var after = new TorsoBisections();
        Assert.Equal(states.Length, after.Read(lines, h => new(h.I + 10, 4)).Restored);
        for (int i = 0; i < states.Length; i++) Assert.Equal(states[i], after.Of(new(i + 11, 4)));
        Assert.True(after.Of(new(11, 4))!.Value.FullBody);
        Assert.True(after.Of(new(12, 4))!.Value.FullBody);
        Assert.False(after.Of(new(13, 4))!.Value.FullBody);
    }

    [Fact]
    public void Version_one_defaults_to_transverse_and_each_version_requires_its_exact_mode_record_shape()
    {
        var legacy = new TorsoBisections();
        var read = legacy.Read(["MEITOU-TORSO-WOUNDS 1", "T 1 1 0 1 0 -12.5", "T 1 2 0 1 0 -12.5 0", "T 1 3 1 0 0 0"], _ => Original);
        Assert.True(read.SupportedVersion);
        Assert.Equal(1, read.Restored);
        Assert.Equal(2, read.Rejected);
        Assert.Equal(Flat, legacy.Of(Original));
        var upgraded = legacy.Write(_ => (1, 1)).ToArray();
        Assert.Equal([TorsoBisections.Header, "T 1 1 0 1 0 -12.5 0"], upgraded);
        var current = new TorsoBisections();
        var modern = current.Read([TorsoBisections.Header, "T 1 1 0 1 0 -12.5", "T 1 2 1 0 0 -0.31415927 1", "T 1 3 1 0 0 -0.31415927 0"], _ => Reloaded);
        Assert.Equal(1, modern.Restored);
        Assert.Equal(2, modern.Rejected);
        Assert.Equal(FullBody, current.Of(Reloaded));
    }

    [Fact]
    public void Version_two_rejects_malformed_mode_flags_and_full_body_still_requires_a_valid_unit_plane()
    {
        string[] bad = ["T 9 1 0 1 0 -12.5 2", "T 9 3 0 1 0 -12.5 -1", "T 9 4 0 1 0 -12.5 true", "T 9 5 0 1 0 -12.5 false", "T 9 6 0 1 0 -12.5 0.5", "T 9 7 0 1 0 -12.5 NaN", "T 9 8 0 1 0 -12.5", "T 9 9 0 1 0 -12.5 1 extra", "T 9 10 2 0 0 0 1", "T 9 11 0 0 0 0 1", "T 9 12 1 0 0 NaN 1"];
        var source = new TorsoBisections();
        var read = source.Read([TorsoBisections.Header, "T 9 1 0 1 0 -12.5 0", "T 9 2 1 0 0 -0.31415927 1", .. bad], h => new(h.I, 1));
        Assert.Equal(2, read.Restored);
        Assert.Equal(bad.Length, read.Rejected);
        Assert.Equal(Flat, source.Of(new(1, 1)));
        Assert.Equal(FullBody, source.Of(new(2, 1)));
        Assert.False(source.Set(Reloaded, new(new(0, 0, 0, 0), true)));
        Assert.False(source.Set(Reloaded, new(new(2, 0, 0, 0), true)));
        Assert.Equal(3, source.Write(id => (9, id.Slot)).Count());
    }
}
