using System.Numerics;
using Meitou.Brutality;
using Meitou.Brutality.Physics;
using Meitou.Simulation;
using Meitou.Brutality.Effects;
using Meitou.Data.Particles;
using Meitou.Data;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Simulation.Combat;

namespace Meitou.Tests.Modding;

public sealed class BrutalityLegacyBloodSpriteTests
{
    [Fact]
    public void A_mod_only_container_stays_sprite_free_until_native_effects_request_it_and_prepares_once()
    {
        var owner = new NativeSpriteEligibility(); owner.MarkModOnly();
        int prepared = 0;
        owner.PrepareOnce(() => prepared++); owner.RequestNative(false); owner.PrepareOnce(() => prepared++);
        Assert.False(owner.NativeRequested); Assert.Equal(0, prepared);
        owner.RequestNative(true); owner.PrepareOnce(() => prepared++); owner.PrepareOnce(() => prepared++);
        owner.RequestNative(false); owner.PrepareOnce(() => prepared++);
        Assert.True(owner.NativeRequested); Assert.Equal(1, prepared);
    }

    [Theory]
    [InlineData(false, true, .53, false)]
    [InlineData(true, false, .53, false)]
    [InlineData(true, true, 1.01, false)]
    [InlineData(true, true, -.01, false)]
    [InlineData(true, true, .53, true)]
    public void Ordinary_sprites_respect_native_container_ownership_NoParticles_and_event_expiration(bool native, bool enabled, double age, bool emits)
    {
        var owner = new NativeSpriteEligibility(); if (!native) owner.MarkModOnly();
        var calls = new List<LegacyBloodBurst>();
        LegacyBloodSprites.Emit(Impact(), 5, 5 + age, enabled, owner, calls.Add);
        Assert.Equal(emits, calls.Count != 0);
    }

    [Fact]
    public void Fake_burst_transport_preserves_seconds_size_life_scale_and_the_native_emitter_expires()
    {
        var calls = new List<LegacyBloodBurst>();
        LegacyBloodSprites.Emit(Impact(), 5, 5.1, true, new(), calls.Add);
        Assert.Equal(2, calls.Count);
        Assert.All(calls, burst =>
        {
            Assert.Equal("HackBlood-system", burst.System);
            Assert.Equal(.2f, burst.Seconds, .00001f); Assert.Equal(1, burst.Size); Assert.Equal(1.8f, burst.LifeScale);
            Assert.Null(burst.Recolour); Assert.InRange(burst.Position.Y, 27, 34);
            Assert.Equal(10, burst.Position.X); Assert.Equal(30, burst.Position.Z);
            Assert.InRange(Vector3.Transform(Vector3.UnitZ, burst.Orientation).Y, 0, 1);
        });
        const string script = """
            system HackBlood-system
            {
                technique t
                {
                    visual_particle_quota 40
                    emitter Box
                    {
                        emission_rate 50
                        time_to_live 0.2
                        velocity 1
                        direction 0 1 0
                    }
                }
            }
            """;
        var def = PuSystemDef.From(PuScriptReader.Parse(script).Single(node => node.Kind == "system"));
        var group = new BurstEffectGroup(def, 1, new EffectWorld());
        foreach (var burst in calls) group.Spawn(burst.Position, burst.Orientation, burst.Seconds, burst.Size, burst.LifeScale, burst.Recolour);
        var camera = new EffectCamera(new(10, 25, 35), -Vector3.UnitZ);
        group.Update(.05f, camera, WeatherEffectInput.None); group.Simulate();
        Assert.True(group.ParticleCount > 0);
        for (int i = 0; i < 40; i++) { group.Update(.05f, camera, WeatherEffectInput.None); group.Simulate(); }
        Assert.Equal(0, group.ParticleCount); Assert.Empty(group.Units);
    }

    [Theory]
    [InlineData(false, false, "HackBlood-system", false)]
    [InlineData(false, false, "Bloodsplash01", false)]
    [InlineData(true, false, "HackBlood-system", true)]
    [InlineData(true, false, "Bloodsplash01", true)]
    [InlineData(true, true, "HackBlood-system", false)]
    [InlineData(true, true, "bLoOdSpLaSh01", false)]
    [InlineData(false, false, "kenshi_sparks", true)]
    [InlineData(false, true, "another-mod-electricity", true)]
    public void Direct_native_neck_and_landing_bursts_obey_blood_eligibility_without_filtering_unrelated_effects(
        bool native, bool noParticles, string system, bool expected)
    {
        var owner = new NativeSpriteEligibility(); if (!native) owner.MarkModOnly();
        Assert.Equal(expected, LegacyBloodSprites.AllowNativeBurst(system, owner, noParticles));
        // An ordinary renderer not owned by this DLL keeps its existing call path.
        Assert.True(LegacyBloodSprites.AllowNativeBurst("HackBlood-system", null, false));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Native_neck_spurts_keep_the_pinned_robot_exclusion(bool robot, bool allowed)
    {
        var file = new FcsFile(); var race = new FcsRecord
        { StringId = "neck-race", Name = "neck race", Flags = 0x10, RecordType = FcsRecordType.RACE };
        race.Bools["is robot"] = robot; file.Records.Add(race);
        var database = new GameDatabase(); database.Apply(file, "neck.mod");
        var look = new CharacterAppearance { Race = database.Find("neck-race")! };
        Assert.Equal(allowed, LegacyBloodSprites.AllowNeckSpurt(look));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void A_shown_owned_head_replaces_native_whole_head_and_neck_spurts_for_all_modes_and_races(int mode, bool robot)
    {
        var (owner, frame) = HeadFrame((HeadCutMode)mode, robot);
        Assert.True(LegacyBloodSprites.OwnsShownHead(frame, owner, 180, true));
    }

    [Fact]
    public void Pending_missing_future_wrong_generation_and_nonphysical_heads_keep_native_fallback()
    {
        var (owner, frame) = HeadFrame(HeadCutMode.DetachedSplit, false);
        var actor = frame.Characters[owner];
        GorePresentationFrame Changed(GoreCharacterFrame value) => new(180, new Dictionary<CharacterId, GoreCharacterFrame> { [owner] = value });
        Assert.False(LegacyBloodSprites.OwnsShownHead(null, owner, 180, true));
        Assert.False(LegacyBloodSprites.OwnsShownHead(frame, owner, 180, false));
        Assert.False(LegacyBloodSprites.OwnsShownHead(frame with { Tick = 181 }, owner, 180, true));
        Assert.False(LegacyBloodSprites.OwnsShownHead(frame, new(owner.Slot, owner.Generation + 1), 180, true));
        Assert.False(LegacyBloodSprites.OwnsShownHead(Changed(actor with { Detached = [] }), owner, 180, true));
        Assert.False(LegacyBloodSprites.OwnsShownHead(Changed(actor with { State = actor.State with { Head = null } }), owner, 180, true));
        Assert.False(LegacyBloodSprites.OwnsShownHead(Changed(actor with { Detached = [actor.Detached[0] with { StableKey = 10 }] }), owner, 180, true));
        Assert.False(LegacyBloodSprites.OwnsShownHead(Changed(actor with { Detached = [new(2, new BonePose(1), Vector3.Zero)] }), owner, 180, true));
    }

    [Fact]
    public void A_pending_physical_head_keeps_native_visual_fallback_but_accepted_geometry_skips_it_and_nested_failure_restores_spurts()
    {
        Assert.False(LegacyBloodSprites.NeckSpurtSuppressed);
        Assert.True(LegacyBloodSprites.BeginHeadCut(true, false, out bool beforePending));
        try
        {
            Assert.True(LegacyBloodSprites.NeckSpurtSuppressed);
            Assert.True(LegacyBloodSprites.BeginHeadCut(false, false, out bool beforeNested));
            try { Assert.True(LegacyBloodSprites.NeckSpurtSuppressed); throw new InvalidOperationException("Native head fallback failed."); }
            catch (InvalidOperationException) { }
            finally { LegacyBloodSprites.EndHeadCut(beforeNested); }
            Assert.True(LegacyBloodSprites.NeckSpurtSuppressed);
        }
        finally { LegacyBloodSprites.EndHeadCut(beforePending); }
        Assert.False(LegacyBloodSprites.NeckSpurtSuppressed);
        Assert.False(LegacyBloodSprites.BeginHeadCut(true, true, out bool beforeAccepted));
        try { Assert.True(LegacyBloodSprites.NeckSpurtSuppressed); }
        finally { LegacyBloodSprites.EndHeadCut(beforeAccepted); }
        Assert.False(LegacyBloodSprites.NeckSpurtSuppressed);
        Assert.True(LegacyBloodSprites.BeginHeadCut(false, true, out bool beforeOrdinary));
        try { Assert.False(LegacyBloodSprites.NeckSpurtSuppressed); }
        finally { LegacyBloodSprites.EndHeadCut(beforeOrdinary); }
    }

    static (CharacterId Owner, GorePresentationFrame Frame) HeadFrame(HeadCutMode mode, bool robot)
    {
        var file = new FcsFile(); var race = new FcsRecord
        { StringId = "head-race", Name = "head race", Flags = 0x10, RecordType = FcsRecordType.RACE };
        race.Bools["is robot"] = robot; file.Records.Add(race);
        var database = new GameDatabase(); database.Apply(file, "head.mod");
        var look = new CharacterAppearance { Race = database.Find("head-race")! };
        var owner = new CharacterId(4, 7);
        var state = new BrutalityActorView(owner, 0, 100, 0, default, new(new(1, 0, 0, 0), mode), null, []);
        var actor = new GoreCharacterFrame(owner, look, state, null, [new(2, new BonePose(1) { AbsolutePositions = true }, Vector3.Zero)]);
        return (owner, new(180, new Dictionary<CharacterId, GoreCharacterFrame> { [owner] = actor }));
    }

    static ImpactEffectEvent Impact()
    {
        var hit = default(CombatHit) with { Tick = 164, Damage = new(10, 0, 0), VictimPosition = new(10, 20, 30), Travel = Vector3.UnitX };
        return new(1, hit, default, false, false, 0x800000, "Bip01 Head", 17, 4, ImpactEffectKind.OrganicSpray);
    }
}
