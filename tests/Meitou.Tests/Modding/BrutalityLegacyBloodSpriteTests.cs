using System.Numerics;
using Meitou.Brutality;
using Meitou.Brutality.Effects;
using Meitou.Data.Particles;
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

    static ImpactEffectEvent Impact()
    {
        var hit = default(CombatHit) with { Tick = 164, Damage = new(10, 0, 0), VictimPosition = new(10, 20, 30), Travel = Vector3.UnitX };
        return new(1, hit, default, false, false, 0x800000, "Bip01 Head", 17, 4, ImpactEffectKind.OrganicSpray);
    }
}
