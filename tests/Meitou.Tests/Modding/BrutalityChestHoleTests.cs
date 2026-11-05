using System.Numerics;
using System.Text.Json.Nodes;
using Meitou.Brutality;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Tests.Combat;
using static Meitou.Tests.Bodies.BodyFixtures;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Modding;

/// <summary>The martial-arts punch-through: which blows open it, where it runs, and that it survives a save.</summary>
public sealed class BrutalityChestHoleTests
{
    static ChestHoleInput Punch(float damage = 60, bool lethal = true) => new(CombatHitSource.Melee, CombatWoundMechanism.Crush,
        (int)WeaponCategory.Unarmed, ChestHolePolicy.ChestPartId, damage, 100, AliveBeforeHit: true, lethal,
        Holed: false, Bisected: false, Robot: false, Animal: false);

    [Fact]
    public void Only_a_bare_handed_crush_to_the_chest_or_stomach_at_the_threshold_opens_a_hole()
    {
        var rule = ChestHoleRule.Default;
        Assert.True(ChestHolePolicy.Qualifies(Punch(), rule));
        Assert.True(ChestHolePolicy.Qualifies(Punch(damage: 50), rule));
        Assert.False(ChestHolePolicy.Qualifies(Punch(damage: 49.9f), rule));
        Assert.False(ChestHolePolicy.Qualifies(Punch(damage: float.NaN), rule));
        Assert.False(ChestHolePolicy.Qualifies(Punch() with { WeaponCategory = (int)WeaponCategory.Blunt }, rule));
        Assert.False(ChestHolePolicy.Qualifies(Punch() with { Mechanism = CombatWoundMechanism.Slash }, rule));
        Assert.True(ChestHolePolicy.Qualifies(Punch() with { PartId = GorePartIds.Stomach }, rule));
        Assert.False(ChestHolePolicy.Qualifies(Punch() with { PartId = "28-gamedata.quack" }, rule));
        Assert.False(ChestHolePolicy.Qualifies(Punch() with { Source = CombatHitSource.Projectile }, rule));
        Assert.False(ChestHolePolicy.Qualifies(Punch() with { AliveBeforeHit = false }, rule));
        Assert.False(ChestHolePolicy.Qualifies(Punch() with { Holed = true }, rule));
        Assert.False(ChestHolePolicy.Qualifies(Punch() with { Bisected = true }, rule));
        Assert.False(ChestHolePolicy.Qualifies(Punch() with { Robot = true }, rule));
        Assert.False(ChestHolePolicy.Qualifies(Punch() with { Animal = true }, rule));
    }

    [Fact]
    public void The_settings_disable_the_hole_require_a_kill_and_move_the_threshold()
    {
        Assert.True(ChestHolePolicy.Qualifies(Punch(lethal: false), ChestHoleRule.Default));
        Assert.False(ChestHolePolicy.Qualifies(Punch(lethal: false), ChestHoleRule.Default with { LethalOnly = true }));
        Assert.True(ChestHolePolicy.Qualifies(Punch(), ChestHoleRule.Default with { LethalOnly = true }));
        Assert.False(ChestHolePolicy.Qualifies(Punch(), ChestHoleRule.Default with { Enabled = false }));
        Assert.False(ChestHolePolicy.Qualifies(Punch(damage: 60), ChestHoleRule.Default with { DamageFraction = .8f }));
        Assert.True(ChestHolePolicy.Qualifies(Punch(damage: 15), ChestHoleRule.Default with { DamageFraction = .1f }));
    }

    [Theory]
    [InlineData(0f, 0f, -1f, 1, 0f)]                // facing +Z, struck from the front
    [InlineData(0f, 0f, 1f, -1, 0f)]                // facing +Z, struck from behind
    [InlineData(MathF.PI / 2, -1f, 0f, 1, 0f)]      // facing +X, struck from the front
    [InlineData(0f, .198669f, -.980067f, 1, .2f)]   // 0.2 rad towards the victim's +X
    [InlineData(0f, .564642f, -.825336f, 1, .35f)]  // 0.6 rad: held at the largest yaw
    public void The_hole_runs_along_the_blow_and_enters_on_the_struck_side(float victimYaw, float travelX, float travelZ, int entry, float yaw)
    {
        var hole = ChestHolePolicy.StateFor(new(travelX, 0, travelZ), victimYaw, 50, 100, 0x0123_4567_89AB_CDEFUL);
        Assert.NotNull(hole);
        Assert.True(hole.Value.IsValid);
        Assert.Equal(entry, hole.Value.Entry);
        Assert.Equal(yaw, hole.Value.Yaw, 4);
    }

    [Fact]
    public void A_blow_from_the_side_makes_no_hole_and_a_harder_blow_opens_a_wider_one()
    {
        Assert.Null(ChestHolePolicy.StateFor(Vector3.UnitX, 0, 50, 100, 7));
        Assert.Null(ChestHolePolicy.StateFor(Vector3.UnitY, 0, 50, 100, 7));
        var threshold = ChestHolePolicy.StateFor(-Vector3.UnitZ, 0, 50, 100, 7)!.Value;
        var crushing = ChestHolePolicy.StateFor(-Vector3.UnitZ, 0, 200, 100, 7)!.Value;
        Assert.True(crushing.Radius > threshold.Radius);
        Assert.Equal(threshold with { Radius = crushing.Radius }, crushing);
    }

    [Theory]
    [InlineData(false, true, 400, true)]
    [InlineData(false, false, 400, true)]
    [InlineData(false, true, 10, false)]
    [InlineData(true, true, 400, false)]
    public void A_resolved_punch_records_the_hole_and_its_request(bool armed, bool kill, float blunt, bool expected)
    {
        using var fight = new Fight(armed);
        fight.Strike(blunt, armed ? CombatWoundMechanism.Slash : CombatWoundMechanism.Crush, kill);
        Assert.Equal(expected, fight.Hole is not null);
        var requests = fight.System.CutRequests.Where(request => request.Kind == GoreCutKind.ChestHole).ToArray();
        if (!expected) { Assert.Empty(requests); return; }
        Assert.Equal(1, fight.Hole!.Value.Entry);
        Assert.Equal(fight.Hole, Assert.Single(requests).ChestHole);
    }

    [Fact]
    public void A_punched_hole_survives_export_the_sidecar_and_import_into_a_new_world()
    {
        using var fight = new Fight(armed: false);
        fight.Strike(400, CombatWoundMechanism.Crush, kill: true);
        var hole = Assert.NotNull(fight.Hole);
        using var stream = new MemoryStream();
        BrutalityStateCodec.Write(stream, fight.System.ExportDurable(id => id == fight.Victim ? "victim" : null));
        stream.Position = 0;
        using var restored = new Fight(armed: false);
        var decoded = BrutalityStateCodec.Read(stream);
        Assert.Equal(1, restored.System.ImportDurable(restored.World, decoded, key => key == "victim" ? restored.Victim : null));
        restored.System.SlowWorld(restored.World);
        Assert.Equal(hole, restored.Hole);
    }

    [Fact]
    public void A_save_from_before_the_hole_existed_loads_without_one_and_an_invalid_hole_is_refused()
    {
        var saved = new BrutalityDurableState("npc/key", 5, new(0), null, null, []) { ChestHole = ChestHolePolicy.Preview(1) };
        using var stream = new MemoryStream();
        BrutalityStateCodec.Write(stream, [saved]);
        var payload = JsonNode.Parse(stream.ToArray())!;
        foreach (var actor in payload["Actors"]!.AsArray()) actor!.AsObject().Remove("ChestHole");
        using var older = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(payload.ToJsonString()));
        var loaded = Assert.Single(BrutalityStateCodec.Read(older));
        Assert.Equal("npc/key", loaded.Key);
        Assert.Null(loaded.ChestHole);
        var hole = saved.ChestHole!.Value;
        Assert.True(BrutalityStateCodec.Valid(saved));
        Assert.False(BrutalityStateCodec.Valid(saved with { ChestHole = hole with { Radius = float.NaN } }));
        Assert.False(BrutalityStateCodec.Valid(saved with { ChestHole = hole with { Entry = 0 } }));
        Assert.False(BrutalityStateCodec.Valid(saved with { ChestHole = hole with { Height = .2f } }));
    }

    /// <summary>A Greenlander whose chest carries the game's own chest id, as the policy reads it.</summary>
    static readonly RaceData Greenlander = new()
    {
        StringId = "17-hole", Name = "Greenlander", StatMultipliers = Human.StatMultipliers,
        Anatomy = Human.Anatomy.Select(entry => entry.Part.StringId == Chest.StringId
            ? entry with { Part = Chest with { StringId = ChestHolePolicy.ChestPartId } } : entry).ToArray(),
    };

    /// <summary>One attacker (bare-handed or with a katana) and one victim facing +Z, struck in the chest from the front.</summary>
    sealed class Fight : IDisposable
    {
        public readonly BrutalityGameplaySystem System = new(null, BaseGame, BodyOptions.Default, (RngPurpose)0xB17A1171);
        public readonly SimWorld World;
        public readonly CharacterId Attacker, Victim;
        readonly CharacterCold victim;
        readonly int chest;

        public Fight(bool armed)
        {
            World = new SimWorld(new WorldSettings { PublishSnapshots = false }, new OpenGroundWalkability((_, _) => 0), [System]);
            var source = (armed ? new Combatant() : new Combatant { Weapon = null }).MakeCold("source");
            source.IsPlayer = true;
            source.Faction = 1;
            var stats = new Combatant().MakeStats();
            victim = new CharacterCold
            {
                Name = "victim", Stats = stats, Race = Greenlander, Medical = MedicalState.Create(Greenlander, stats.Strength),
                Fighter = new Combatant().MakeFighter(),
            };
            victim.Faction = 2;
            Attacker = World.Characters.Spawn(new(), source, 0);
            Victim = World.Characters.Spawn(new(), victim, 0);
            chest = victim.Medical!.Parts.ToList().FindIndex(part => part.Template.StringId == ChestHolePolicy.ChestPartId);
            System.Schedule(World);
        }

        public ChestHoleState? Hole => System.ActorViews.SingleOrDefault(view => view.Character == Victim)?.ChestHole;

        public void Strike(float blunt, CombatWoundMechanism mechanism, bool kill)
        {
            var hit = new CombatHit(0, CombatHitSource.Melee, Attacker, Victim, 1, 0, chest, new(0, blunt, 0), default,
                Vector3.Zero, -Vector3.UnitZ, 0, 0, false, true, true, false);
            var bridge = System.CaptureBeforeHit(World, Attacker, Victim, chest, new(0, blunt, 0, 0, 1, 0), mechanism, Context(Greenlander));
            System.RecordHitBridge(GoreHitKey.From(hit), bridge);
            if (kill) victim.Medical!.ApplyHit(chest, new(10000, 0, 0), Context(Greenlander));
            System.HitResolved(World, hit);
            System.SlowWorld(World);
        }

        public void Dispose() => World.Dispose();
    }
}
