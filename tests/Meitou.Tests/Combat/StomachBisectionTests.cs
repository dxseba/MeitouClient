using System.Collections.Concurrent;
using System.Numerics;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Wounds;
using static Meitou.Tests.Bodies.BodyFixtures;
using static Meitou.Tests.Combat.DuelFixture;

namespace Meitou.Tests.Combat;

public class StomachBisectionTests
{
    static readonly BodyPartTemplate CanonicalStomach = Stomach with { StringId = BodyPartTemplate.StomachPartId };
    static readonly BodyPartTemplate CanonicalChest = Chest with { StringId = ArmourData.ChestPartId };
    static TorsoBisectionInput Eligible => new(CombatHitSource.Melee, CombatWoundMechanism.Slash, CanonicalStomach, 2, 75, 100)
        { Brutality = 60, Lethal = true };

    [Fact]
    public void Canonical_stomach_hits_use_the_same_skill_chance_and_actual_struck_health_threshold()
    {
        Assert.Equal(TorsoBisectionPolicy.Chance(Eligible with { Part = CanonicalChest }), TorsoBisectionPolicy.Chance(Eligible));
        Assert.Equal(0.10f, TorsoBisectionPolicy.Chance(Eligible));
        Assert.Equal(0.95f, TorsoBisectionPolicy.Chance(Eligible with { Brutality = 100 }));
        Assert.Equal(0, TorsoBisectionPolicy.Chance(Eligible with { Maximum = 200, Cut = 149.99f }));
        Assert.Equal(0.10f, TorsoBisectionPolicy.Chance(Eligible with { Maximum = 200, Cut = 150 }));
        Assert.Equal(0, TorsoBisectionPolicy.Chance(Eligible with { Maximum = 50, Cut = 69.99f }));
        Assert.Equal(0.10f, TorsoBisectionPolicy.Chance(Eligible with { Maximum = 50, Cut = 70 }));

        TorsoBisectionInput[] excluded =
        [
            Eligible with { Lethal = false }, Eligible with { AliveBeforeHit = false }, Eligible with { AlreadyBisected = true },
            Eligible with { Robot = true }, Eligible with { Animal = true }, Eligible with { Brutality = 59.99f },
            Eligible with { Source = CombatHitSource.Bolt }, Eligible with { Mechanism = CombatWoundMechanism.Thrust },
            Eligible with { Mechanism = CombatWoundMechanism.Crush }, Eligible with { CutDirection = 5 },
            Eligible with { Part = Stomach }, Eligible with { Part = CanonicalStomach with { Type = BodyPartType.Arm } },
        ];
        foreach (var input in excluded) Assert.False(TorsoBisectionPolicy.ShouldBisect(input, 0));
        Assert.True(TorsoBisectionPolicy.ShouldBisect(Eligible, 0.099f));
        Assert.False(TorsoBisectionPolicy.ShouldBisect(Eligible, 0.10f));
    }

    [Fact]
    public void Stomach_damage_must_already_be_lethal_before_bisection_commits()
    {
        var victim = Victim();
        var medical = victim.Medical!;
        var events = new List<MedicalEvent>();
        var damage = new HitDamage(120, 0, 0);
        medical.ApplyHit(0, damage, Context(victim.Race), events);
        Assert.False(medical.Dead);
        var part = medical.Parts[0];
        var before = (part.Flesh, part.Stun, part.Bandage, medical.Blood);
        Assert.False(CombatSystem.TryBisect(victim, 0, damage, CombatWoundMechanism.Slash, true, 0, 2, events, brutality: 100));
        Assert.Equal(before, (part.Flesh, part.Stun, part.Bandage, medical.Blood));
        Assert.Null(victim.TorsoBisection);

        victim = Victim();
        medical = victim.Medical!;
        events.Clear();
        damage = new HitDamage(350, 0, 0);
        medical.ApplyHit(0, damage, Context(victim.Race), events);
        Assert.True(medical.Dead);
        Assert.True(CombatSystem.TryBisect(victim, 0, damage, CombatWoundMechanism.Slash, true, 0, 2, events,
            visualSeed: 1, style: TorsoBisectionStyle.Transverse, brutality: 100));
        Assert.Single(events, e => e.Kind == MedicalEventKind.Died);
        var cut = Assert.IsType<TorsoBisectionState>(victim.TorsoBisection);
        Assert.Equal(TorsoBisectionPolicy.StateForHit(CanonicalStomach, 2, 1, style: TorsoBisectionStyle.Transverse), cut);
        Assert.False(CombatSystem.TryBisect(victim, 0, damage, CombatWoundMechanism.Slash, false, 0, 2, events, brutality: 100));
    }

    [Fact]
    public void A_real_melee_stomach_hit_publishes_its_actual_part_and_stomach_cut_after_native_death()
    {
        using var world = NewWorld(7, 1, out var combat, techniques: [CombatFixtures.Cut("cut left", direction: 2)]);
        var queue = new ConcurrentQueue<CombatEvent>();
        world.CombatEvents = queue;
        var (attacker, defender) = Pair(world,
            new Combatant { Attack = 100, Defence = 100, WeaponSkill = 100, Dexterity = 100,
                Weapon = CombatFixtures.Katana with { CutDamageMultiplier = 25 } },
            new Combatant { Attack = 0, Defence = 0, Dodge = 0, Weapon = null });
        world.Characters.Cold(attacker.Slot)!.Brutality = BrutalityProgress.FromLevel(100);
        var victim = world.Characters.Cold(defender.Slot)!;
        var body = Victim();
        victim.Race = body.Race;
        victim.Medical = body.Medical;
        RunToEnd(world, attacker, defender, 300);
        var hit = Assert.Single(queue, e => e.Kind == CombatEventKind.Hit && e.Victim == defender);
        var cut = Assert.Single(queue, e => e.Kind == CombatEventKind.BodyBisected && e.Victim == defender);
        Assert.Equal(BodyPartTemplate.StomachPartId, victim.Medical!.Parts[hit.Part].Template.StringId);
        Assert.Equal(hit.Part, cut.Part);
        Assert.True(victim.Medical.Dead);
        Assert.Single(queue, e => e.Kind == CombatEventKind.Died && e.Victim == defender);
        var log = Assert.Single(combat.Log, e => e.Outcome == BlowOutcome.Hit && e.Defender == defender.Slot);
        ulong counter = CombatRolls.BlowCounter(attacker.Slot, combat.StateOf(attacker).AttackSeq, log.Blow, 0);
        ulong visual = TorsoBisectionPolicy.VisualSeedFor(world.Seed, defender, attacker, counter);
        Assert.Equal(TorsoBisectionPolicy.StateForHit(CanonicalStomach, hit.CutDirection, visual, hit.Direction, hit.VictimYaw),
            victim.TorsoBisection);
        var published = queue.ToArray();
        Assert.True(Array.IndexOf(published, cut) > Array.FindIndex(published, e => e.Kind == CombatEventKind.Died));
    }

    [Fact]
    public void Stomach_planes_keep_the_lower_safe_envelope_and_chest_planes_keep_their_existing_results()
    {
        var heights = new HashSet<float>();
        for (ulong seed = 0; seed < 24; seed++)
            foreach (int direction in new[] { 2, 3, 9 })
            {
                var stomach = TorsoBisectionPolicy.StateForHit(CanonicalStomach, direction, seed, Vector2.UnitX, 0.7f,
                    TorsoBisectionStyle.Transverse);
                var plane = stomach.Plane;
                Assert.True(stomach.IsValid);
                Assert.False(stomach.FullBody);
                float height = -plane.W / plane.Y, sx = plane.X / plane.Y, sz = plane.Z / plane.Y;
                heights.Add(height);
                Assert.InRange(height, TorsoBisectionPolicy.StomachMinimumHeight - 0.00001f,
                    TorsoBisectionPolicy.StomachMaximumHeight + 0.00001f);
                float reach = TorsoBisectionPolicy.JunctionHalfWidth * MathF.Abs(sx)
                    + TorsoBisectionPolicy.JunctionHalfDepth * MathF.Abs(sz);
                Assert.True(height - reach >= TorsoBisectionPolicy.LegJunctionCeiling - 0.00002f);
                Assert.True(height + reach <= TorsoBisectionPolicy.ShoulderJunctionFloor + 0.00002f);
                Assert.True(Distance(plane, new(0, height + 1, 0)) > 0);
                Assert.True(Distance(plane, new(0, height - 1, 0)) < 0);
            }
        Assert.True(heights.Max() - heights.Min() > 0.45f);

        foreach (ulong seed in new ulong[] { 0, 1, 3, 7 })
            foreach (int direction in new[] { 1, 2, 3, 7, 9 })
                foreach (var style in Enum.GetValues<TorsoBisectionStyle>())
                {
                    var chest = TorsoBisectionPolicy.StateForDirection(direction, seed, Vector2.UnitX, 0.7f, style);
                    Assert.Equal(chest, TorsoBisectionPolicy.StateForHit(CanonicalChest, direction, seed, Vector2.UnitX, 0.7f, style));
                    var stomach = TorsoBisectionPolicy.StateForHit(CanonicalStomach, direction, seed, Vector2.UnitX, 0.7f, style);
                    Assert.Equal(chest.IsValid, stomach.IsValid);
                    if (!chest.IsValid) continue;
                    if (chest.FullBody)
                        Assert.Equal(new Vector3(chest.Plane.X, chest.Plane.Y, chest.Plane.Z),
                            new Vector3(stomach.Plane.X, stomach.Plane.Y, stomach.Plane.Z));
                    if (style == TorsoBisectionStyle.Vertical) Assert.Equal(chest, stomach);
                    if (style == TorsoBisectionStyle.Oblique)
                        Assert.True(-stomach.Plane.W / stomach.Plane.Y < -chest.Plane.W / chest.Plane.Y);
                }
    }

    [Fact]
    public void The_debug_command_can_target_the_stomach_without_damaging_the_chest()
    {
        using var world = NewWorld(11, 1, out _);
        world.AddSystem(new PlayerSystem());
        var queue = new ConcurrentQueue<CombatEvent>();
        world.CombatEvents = queue;
        var (attacker, defender) = Pair(world, new Combatant(), new Combatant());
        var cold = world.Characters.Cold(defender.Slot)!;
        cold.Race = new() { StringId = Human.StringId, Name = Human.Name,
            Anatomy = [new(CanonicalStomach, 100, 100), new(CanonicalChest, 100, 100)] };
        cold.Medical = MedicalState.Create(cold.Race, 50);
        int stomach = cold.Medical.Parts.ToList().FindIndex(p => p.Template.StringId == BodyPartTemplate.StomachPartId);
        int chest = cold.Medical.Parts.ToList().FindIndex(p => p.Template.StringId == ArmourData.ChestPartId);
        float chestBefore = cold.Medical.Parts[chest].Flesh;
        world.Commands.Enqueue(new DebugBisect(defender)
        {
            Tick = 0, Attacker = attacker, TargetBodyPart = BodyPartTemplate.StomachPartId, VisualSeed = 1,
            Style = TorsoBisectionStyle.Transverse,
        });
        world.RunTick();
        var hit = Assert.Single(queue, e => e.Kind == CombatEventKind.Hit);
        Assert.Equal(stomach, hit.Part);
        Assert.Equal(chestBefore, cold.Medical.Parts[chest].Flesh);
        Assert.True(cold.Medical.Dead);
        var cut = Assert.Single(queue, e => e.Kind == CombatEventKind.BodyBisected);
        Assert.Equal(stomach, cut.Part);
        Assert.Equal(TorsoBisectionPolicy.StateForHit(CanonicalStomach, hit.CutDirection, 1, hit.Direction, hit.VictimYaw,
            TorsoBisectionStyle.Transverse), cold.TorsoBisection);
    }

    static CharacterCold Victim()
    {
        var race = new RaceData { StringId = Human.StringId, Name = Human.Name, Anatomy = [new(CanonicalStomach, 100, 100)] };
        return new() { Race = race, Medical = MedicalState.Create(race, 50), Fighter = new Fighter(null) };
    }

    static float Distance(Vector4 plane, Vector3 point) => Vector3.Dot(new(plane.X, plane.Y, plane.Z), point) + plane.W;
}
