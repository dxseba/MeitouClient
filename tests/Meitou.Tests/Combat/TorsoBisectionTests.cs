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

public class TorsoBisectionTests
{
    static readonly BodyPartTemplate CanonicalChest = Chest with { StringId = ArmourData.ChestPartId };
    static TorsoBisectionInput Eligible => new(CombatHitSource.Melee, CombatWoundMechanism.Slash, CanonicalChest, 2, 75, 100);

    [Fact]
    public void Only_extreme_delivered_cuts_qualify_and_the_chance_stays_rare()
    {
        Assert.Equal(0, TorsoBisectionPolicy.Chance(Eligible with { Cut = 74.99f }));
        Assert.Equal(0.10f, TorsoBisectionPolicy.Chance(Eligible));
        Assert.Equal(0, TorsoBisectionPolicy.Chance(Eligible with { Maximum = 200, Cut = 149.99f }));
        Assert.Equal(0.10f, TorsoBisectionPolicy.Chance(Eligible with { Maximum = 200, Cut = 150 }));
        Assert.Equal(0, TorsoBisectionPolicy.Chance(Eligible with { Maximum = 50, Cut = 69.99f }));
        Assert.Equal(0.10f, TorsoBisectionPolicy.Chance(Eligible with { Maximum = 50, Cut = 70 }));
        Assert.InRange(TorsoBisectionPolicy.Chance(Eligible with { Cut = 112.5f }), 0.1499f, 0.1501f);
        Assert.Equal(0.20f, TorsoBisectionPolicy.Chance(Eligible with { Cut = 150 }));
        Assert.Equal(0.20f, TorsoBisectionPolicy.Chance(Eligible with { Cut = 10000 }));
        Assert.True(TorsoBisectionPolicy.ShouldBisect(Eligible, 0.099f));
        Assert.False(TorsoBisectionPolicy.ShouldBisect(Eligible, 0.10f));
    }

    [Fact]
    public void Armour_can_prevent_bisection_even_when_the_weapon_packet_is_huge()
    {
        var packet = new DamagePacket(500, 250, 250, 0, 1, 0);
        var exposed = HitResolver.Resolve(packet, ArmourEffect.None, 50, 1, CombatFixtures.Constants);
        var protectedHit = HitResolver.Resolve(packet, new ArmourEffect(0.99f, 0, 0, 0), 50, 1, CombatFixtures.Constants);
        Assert.True(TorsoBisectionPolicy.ShouldBisect(Eligible with { Cut = exposed.Cut }, 0));
        Assert.False(TorsoBisectionPolicy.ShouldBisect(Eligible with { Cut = protectedHit.Cut }, 0));
        Assert.True(protectedHit.Blunt + protectedHit.Pierce > 70);
    }

    [Fact]
    public void The_mechanism_sideways_axis_chest_and_living_organic_target_are_all_required()
    {
        TorsoBisectionInput[] excluded =
        [
            Eligible with { Source = CombatHitSource.Bolt }, Eligible with { Source = CombatHitSource.Execution },
            Eligible with { Mechanism = CombatWoundMechanism.Unspecified }, Eligible with { Mechanism = CombatWoundMechanism.Thrust },
            Eligible with { Mechanism = CombatWoundMechanism.Crush }, Eligible with { CutDirection = 0 },
            Eligible with { CutDirection = 1 }, Eligible with { CutDirection = 4 }, Eligible with { CutDirection = 5 },
            Eligible with { CutDirection = 7 }, Eligible with { Part = Stomach }, Eligible with { Part = Head },
            Eligible with { Part = CanonicalChest with { Type = BodyPartType.Arm } },
            Eligible with { AliveBeforeHit = false }, Eligible with { AlreadyBisected = true },
            Eligible with { Robot = true }, Eligible with { Animal = true },
        ];
        foreach (var input in excluded) Assert.False(TorsoBisectionPolicy.ShouldBisect(input, 0));
        foreach (int direction in new[] { 2, 3, 9 })
            Assert.True(TorsoBisectionPolicy.ShouldBisect(Eligible with { CutDirection = direction }, 0));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(-1f)]
    public void Invalid_damage_health_and_rolls_do_not_open_a_cut(float invalid)
    {
        Assert.False(TorsoBisectionPolicy.ShouldBisect(Eligible with { Cut = invalid }, 0));
        Assert.False(TorsoBisectionPolicy.ShouldBisect(Eligible with { Maximum = invalid }, 0));
        Assert.False(TorsoBisectionPolicy.ShouldBisect(Eligible, invalid));
        Assert.False(TorsoBisectionPolicy.ShouldBisect(Eligible with { Maximum = 0 }, 0));
        Assert.False(TorsoBisectionPolicy.ShouldBisect(Eligible, 1));
    }

    [Fact]
    public void The_committed_plane_is_normalized_and_keeps_the_upper_half_positive()
    {
        foreach (int direction in new[] { 2, 3, 9 })
        {
            var plane = TorsoBisectionPolicy.PlaneForDirection(direction);
            Assert.True(new TorsoBisectionState(plane).IsValid);
            float height = -plane.W / plane.Y;
            Assert.InRange(height, TorsoBisectionPolicy.MinimumHeight, TorsoBisectionPolicy.MaximumHeight);
            Assert.InRange(Vector4.Dot(plane, new(0, height, 0, 1)), -0.00001f, 0.00001f);
            Assert.True(Vector4.Dot(plane, new(0, 14, 0, 1)) > 0);
            Assert.True(Vector4.Dot(plane, new(0, 11, 0, 1)) < 0);
        }
    }

    [Fact]
    public void Continuous_plane_variants_cover_distinct_heights_and_angles_inside_the_shared_limb_envelope()
    {
        var heights = new HashSet<float>();
        var normals = new HashSet<Vector3>();
        foreach (ulong seed in Enumerable.Range(0, 256).Select(i => (ulong)i))
            foreach (int direction in new[] { 2, 3, 9 })
                foreach (var travel in new[] { Vector2.UnitX, Vector2.UnitY, new Vector2(-0.7f, 0.4f) })
                {
                    var plane = TorsoBisectionPolicy.PlaneForDirection(direction, seed, travel, 0.7f);
                    Assert.Equal(plane, TorsoBisectionPolicy.PlaneForDirection(direction, seed, travel, 0.7f));
                    Assert.True(new TorsoBisectionState(plane).IsValid);
                    float height = -plane.W / plane.Y, sx = plane.X / plane.Y, sz = plane.Z / plane.Y;
                    Assert.InRange(height, TorsoBisectionPolicy.MinimumHeight - 0.00001f, TorsoBisectionPolicy.MaximumHeight + 0.00001f);
                    Assert.InRange(MathF.Abs(sx), 0, TorsoBisectionPolicy.MaximumSlopeX + 0.00001f);
                    Assert.InRange(MathF.Abs(sz), 0, TorsoBisectionPolicy.MaximumSlopeZ + 0.00001f);
                    float reach = TorsoBisectionPolicy.JunctionHalfWidth * MathF.Abs(sx) + TorsoBisectionPolicy.JunctionHalfDepth * MathF.Abs(sz);
                    Assert.True(height - reach >= TorsoBisectionPolicy.LegJunctionCeiling - 0.00002f);
                    Assert.True(height + reach <= TorsoBisectionPolicy.ShoulderJunctionFloor + 0.00002f);
                    Assert.True(plane.Y >= MathF.Cos(TorsoBisectionPolicy.MaximumTiltDegrees * MathF.PI / 180) - 0.00001f);
                    heights.Add(height);
                    normals.Add(new(plane.X, plane.Y, plane.Z));
                }
        Assert.True(heights.Count > 200);
        Assert.True(heights.Max() - heights.Min() > 1.8f);
        Assert.True(normals.Count > 1000);
        Assert.True(normals.Min(n => n.Y) < 0.96f);
    }

    [Fact]
    public void Incoming_direction_and_opposing_blade_axes_change_the_angle_in_victim_space()
    {
        var left = TorsoBisectionPolicy.PlaneForDirection(2, 1, new(0, -1));
        var right = TorsoBisectionPolicy.PlaneForDirection(3, 1, new(0, -1));
        Assert.Equal(left.W, right.W);
        Assert.Equal(left.Y, right.Y);
        Assert.Equal(left.X, -right.X);
        Assert.Equal(left.Z, -right.Z);
        Assert.True(Vector4.Distance(left, TorsoBisectionPolicy.PlaneForDirection(2, 1, Vector2.UnitX)) > 0.02f);
        float yaw = 1.1f;
        var rotatedWorldTravel = -new Vector2(MathF.Sin(yaw), MathF.Cos(yaw));
        Assert.InRange(Vector4.Distance(left, TorsoBisectionPolicy.PlaneForDirection(2, 1, rotatedWorldTravel, yaw)), 0, 0.00001f);
        Assert.Equal(TorsoBisectionPolicy.PlaneForDirection(2, 1), TorsoBisectionPolicy.PlaneForDirection(2, 1, new(float.NaN, 1), float.NaN));
    }

    [Fact]
    public void Each_opening_plane_survives_save_load_exactly_and_cannot_be_moved_by_a_later_hit()
    {
        foreach (ulong seed in new ulong[] { 0, 1, 3, 123456789 })
        {
            var victim = Victim(out int chest);
            var events = new List<MedicalEvent>();
            Assert.True(CombatSystem.TryBisect(victim, chest, new(200, 0, 0), CombatWoundMechanism.Slash, true, 0, 3, events, seed, Vector2.UnitX, 0.4f));
            var fixedPlane = victim.TorsoBisection!.Value;
            Assert.False(CombatSystem.TryBisect(victim, chest, new(500, 0, 0), CombatWoundMechanism.Slash, true, 0, 2, events, seed + 1, Vector2.UnitY, 2));
            Assert.Equal(fixedPlane, victim.TorsoBisection);
            var source = new TorsoBisections();
            Assert.True(source.Set(new(3, 1), fixedPlane));
            var written = source.Write(_ => (12, 4)).ToArray();
            var loaded = new TorsoBisections();
            Assert.Equal(1, loaded.Read(written, handle => handle == (12, 4) ? new CharacterId(17, 2) : null).Restored);
            Assert.Equal(fixedPlane, loaded.Of(new(17, 2)));
        }
    }

    [Fact]
    public void An_actual_melee_hit_uses_its_own_visual_hash_and_preserves_the_existing_chance_roll()
    {
        using var world = NewWorld(7, 1, out var combat, techniques: [CombatFixtures.Cut("cut left", direction: 2)]);
        var queue = new ConcurrentQueue<CombatEvent>();
        world.CombatEvents = queue;
        var (attacker, defender) = Pair(world,
            new Combatant { Attack = 100, Defence = 100, WeaponSkill = 100, Dexterity = 100,
                Weapon = CombatFixtures.Katana with { CutDamageMultiplier = 25 } },
            new Combatant { Attack = 0, Defence = 0, Dodge = 0, Weapon = null });
        var victim = world.Characters.Cold(defender.Slot)!;
        var race = new RaceData { StringId = Human.StringId, Name = Human.Name, Anatomy = [new(CanonicalChest, 100, 100)] };
        victim.Race = race;
        victim.Medical = MedicalState.Create(race, 50);
        RunToEnd(world, attacker, defender, 300);
        var hit = Assert.Single(queue, e => e.Kind == CombatEventKind.Hit && e.Victim == defender);
        var log = Assert.Single(combat.Log, e => e.Outcome == BlowOutcome.Hit && e.Defender == defender.Slot);
        ulong counter = CombatRolls.BlowCounter(attacker.Slot, combat.StateOf(attacker).AttackSeq, log.Blow, 0);
        float chance = CombatRolls.Float(world.Seed, Rng.Key(defender), CombatRoll.BodyBisection, counter);
        Assert.InRange(chance, 0, TorsoBisectionPolicy.MaximumChance);
        Assert.True(TorsoBisectionPolicy.ShouldBisect(new(CombatHitSource.Melee, hit.Mechanism, CanonicalChest,
            hit.CutDirection, hit.Damage.Cut, victim.Medical.Parts[0].MaxHp), chance));
        Assert.NotNull(victim.TorsoBisection);
        ulong visual = TorsoBisectionPolicy.VisualSeedFor(world.Seed, defender, attacker, counter);
        Assert.Equal(TorsoBisectionPolicy.PlaneForDirection(hit.CutDirection, visual, hit.Direction, hit.VictimYaw), victim.TorsoBisection!.Value.Plane);
        Assert.Equal(chance, CombatRolls.Float(world.Seed, Rng.Key(defender), CombatRoll.BodyBisection, counter));
    }

    [Fact]
    public void A_successful_cut_kills_once_without_applying_damage_twice_or_changing_the_saved_plane()
    {
        var victim = Victim(out int chest);
        var medical = victim.Medical!;
        var events = new List<MedicalEvent>();
        var damage = new HitDamage(80, 0, 0);
        medical.ApplyHit(chest, damage, Context(victim.Race), events);
        Assert.False(medical.Dead);
        var part = medical.Parts[chest];
        var before = (part.Flesh, part.Stun, part.Bandage, part.Rig, medical.Blood);
        Assert.True(CombatSystem.TryBisect(victim, chest, damage, CombatWoundMechanism.Slash, true, 0, 2, events));
        Assert.True(medical.Dead);
        Assert.Equal(DeathCause.VitalPart, medical.Cause);
        Assert.Equal(before, (part.Flesh, part.Stun, part.Bandage, part.Rig, medical.Blood));
        Assert.Single(events, e => e.Kind == MedicalEventKind.Died);
        var saved = victim.TorsoBisection;
        Assert.NotNull(saved);
        Assert.False(CombatSystem.TryBisect(victim, chest, damage, CombatWoundMechanism.Slash, true, 0, 3, events));
        Assert.Equal(saved, victim.TorsoBisection);
        medical.KillFromBisection(events);
        Assert.Single(events, e => e.Kind == MedicalEventKind.Died);
    }

    [Fact]
    public void A_native_lethal_hit_can_bisect_but_a_preexisting_corpse_cannot()
    {
        var victim = Victim(out int chest);
        var events = new List<MedicalEvent>();
        var damage = new HitDamage(350, 0, 0);
        victim.Medical!.ApplyHit(chest, damage, Context(victim.Race), events);
        Assert.True(victim.Medical.Dead);
        Assert.Single(events, e => e.Kind == MedicalEventKind.Died);
        Assert.True(CombatSystem.TryBisect(victim, chest, damage, CombatWoundMechanism.Slash, true, 0, 3, events));
        Assert.Single(events, e => e.Kind == MedicalEventKind.Died);

        var corpse = Victim(out chest);
        corpse.Medical!.Butcher();
        events.Clear();
        Assert.False(CombatSystem.TryBisect(corpse, chest, damage, CombatWoundMechanism.Slash, false, 0, 3, events));
        Assert.Null(corpse.TorsoBisection);
        Assert.Empty(events);
        Assert.Equal(DeathCause.Butchered, corpse.Medical.Cause);
    }

    [Fact]
    public void Bisection_is_published_after_native_death_once_with_the_original_hit_metadata()
    {
        using var world = NewWorld(11, 1, out var combat);
        var queue = new ConcurrentQueue<CombatEvent>();
        world.CombatEvents = queue;
        var (attacker, defender) = Pair(world, new Combatant(), new Combatant());
        var cold = world.Characters.Cold(defender.Slot)!;
        var setup = Victim(out int chest);
        cold.Race = setup.Race;
        cold.Medical = setup.Medical;
        var events = new List<MedicalEvent>();
        var damage = new HitDamage(80, 0, 0);
        cold.Medical!.ApplyHit(chest, damage, Context(cold.Race), events);
        bool bisected = CombatSystem.TryBisect(cold, chest, damage, CombatWoundMechanism.Slash, true, 0, 2, events);
        combat.PublishHit(world, CombatHitSource.Melee, attacker, defender.Slot, cold.Medical, chest, damage,
            Vector2.UnitX, events, 2, mechanism: CombatWoundMechanism.Slash, bodyBisected: bisected);
        var published = queue.ToArray();
        Assert.Equal(CombatEventKind.Hit, published[0].Kind);
        Assert.Single(published, e => e.Kind == CombatEventKind.Died);
        var cut = Assert.Single(published, e => e.Kind == CombatEventKind.BodyBisected);
        Assert.Equal(CombatEventKind.BodyBisected, published[^1].Kind);
        Assert.Equal((attacker, defender, chest, damage, Vector2.UnitX, CombatWoundMechanism.Slash),
            (cut.Attacker, cut.Victim, cut.Part, cut.Damage, cut.Direction, cut.Mechanism));

        events.Clear();
        bisected = CombatSystem.TryBisect(cold, chest, damage, CombatWoundMechanism.Slash, false, 0, 2, events);
        combat.PublishHit(world, CombatHitSource.Melee, attacker, defender.Slot, cold.Medical, chest, damage,
            Vector2.UnitX, events, 2, mechanism: CombatWoundMechanism.Slash, bodyBisected: bisected);
        Assert.Single(queue, e => e.Kind == CombatEventKind.BodyBisected);
        Assert.Single(queue, e => e.Kind == CombatEventKind.Died);
    }

    [Fact]
    public void The_debug_command_runs_at_its_tick_and_publishes_the_actual_direction_only_once()
    {
        using var world = NewWorld(11, 1, out _);
        world.AddSystem(new PlayerSystem());
        var queue = new ConcurrentQueue<CombatEvent>();
        world.CombatEvents = queue;
        var (attacker, defender) = Pair(world, new Combatant(), new Combatant());
        var cold = world.Characters.Cold(defender.Slot)!;
        var setup = Victim(out int chest);
        cold.Race = setup.Race;
        cold.Medical = setup.Medical;
        world.Player.Selection.Add(attacker);
        float before = cold.Medical!.Parts[chest].Flesh;
        world.Commands.Enqueue(new DebugBisect(defender) { Tick = 3 });
        world.Commands.Enqueue(new DebugBisect(defender) { Tick = 3 });
        while (world.Tick <= 3) world.RunTick();

        var hit = Assert.Single(queue, e => e.Kind == CombatEventKind.Hit);
        var cut = Assert.Single(queue, e => e.Kind == CombatEventKind.BodyBisected);
        Assert.Single(queue, e => e.Kind == CombatEventKind.Died);
        Assert.Equal(3L, cut.Tick);
        Assert.Equal((attacker, defender, CombatWoundMechanism.Slash), (cut.Attacker, cut.Victim, cut.Mechanism));
        var a = world.Characters.Previous[attacker.Slot].Position;
        var b = world.Characters.Previous[defender.Slot].Position;
        Assert.Equal(new Vector2(b.X - a.X, b.Z - a.Z), cut.Direction);
        Assert.True(cold.Medical.Dead);
        Assert.Equal(before - hit.Damage.Cut, cold.Medical.Parts[chest].Flesh);
        Assert.True(cold.TorsoBisection!.Value.IsValid);
        var plane = cold.TorsoBisection;
        ulong visual = TorsoBisectionPolicy.VisualSeedFor(world.Seed, defender, attacker, 3);
        Assert.Equal(TorsoBisectionPolicy.PlaneForDirection(hit.CutDirection, visual, hit.Direction, hit.VictimYaw), plane!.Value.Plane);
        world.Commands.Enqueue(new DebugBisect(defender) { Tick = world.Tick, Attacker = attacker });
        world.RunTick();
        Assert.Single(queue, e => e.Kind == CombatEventKind.Hit);
        Assert.Single(queue, e => e.Kind == CombatEventKind.Died);
        Assert.Single(queue, e => e.Kind == CombatEventKind.BodyBisected);
        Assert.Equal(plane, cold.TorsoBisection);
    }

    [Fact]
    public void Debug_visual_seeds_are_repeatable_across_worlds_and_support_opposite_diagonals()
    {
        var examples = new HashSet<Vector4>();
        foreach (ulong seed in new ulong[] { 0, 1, 3 })
        {
            var first = DebugPlane(11, seed);
            Assert.Equal(first, DebugPlane(31, seed));
            examples.Add(first);
        }
        Assert.Equal(3, examples.Count);
        Assert.True(examples.Max(p => -p.W / p.Y) - examples.Min(p => -p.W / p.Y) > 1.4f);
        Assert.NotEqual(DebugPlane(11, null), DebugPlane(31, null));
        var left = DebugPlane(11, 1, 2);
        var right = DebugPlane(11, 1, 3);
        Assert.Equal(left.Y, right.Y);
        Assert.Equal(left.W, right.W);
        Assert.Equal(left.X, -right.X);
        Assert.Equal(left.Z, -right.Z);
    }

    [Theory]
    [InlineData(true, false, 2)]
    [InlineData(false, true, 2)]
    [InlineData(false, false, 1)]
    public void The_debug_command_rejects_ineligible_targets_and_vertical_cuts_before_applying_damage(bool robot, bool animal, int direction)
    {
        using var world = NewWorld(11, 1, out _);
        world.AddSystem(new PlayerSystem());
        var queue = new ConcurrentQueue<CombatEvent>();
        world.CombatEvents = queue;
        var (attacker, defender) = Pair(world, new Combatant(), new Combatant());
        var cold = world.Characters.Cold(defender.Slot)!;
        var setup = Victim(out int chest);
        cold.Race = BisectionRace(robot);
        cold.Medical = setup.Medical;
        cold.Fighter = new Fighter(null) { IsAnimal = animal };
        float flesh = cold.Medical!.Parts[chest].Flesh, blood = cold.Medical.Blood;
        world.Commands.Enqueue(new DebugBisect(defender) { Tick = 0, Attacker = attacker, CutDirection = direction });
        world.RunTick();
        Assert.Equal(flesh, cold.Medical.Parts[chest].Flesh);
        Assert.Equal(blood, cold.Medical.Blood);
        Assert.False(cold.Medical.Dead);
        Assert.Null(cold.TorsoBisection);
        Assert.Empty(queue);
    }

    static Vector4 DebugPlane(ulong worldSeed, ulong? visualSeed, int direction = 2)
    {
        using var world = NewWorld(worldSeed, 1, out _);
        world.AddSystem(new PlayerSystem());
        var queue = new ConcurrentQueue<CombatEvent>();
        world.CombatEvents = queue;
        var (attacker, defender) = Pair(world, new Combatant(), new Combatant());
        var cold = world.Characters.Cold(defender.Slot)!;
        var setup = Victim(out _);
        cold.Race = setup.Race;
        cold.Medical = setup.Medical;
        world.Commands.Enqueue(new DebugBisect(defender) { Tick = 0, Attacker = attacker, VisualSeed = visualSeed, CutDirection = direction });
        world.RunTick();
        var hit = Assert.Single(queue, e => e.Kind == CombatEventKind.Hit);
        Assert.True(cold.Medical!.Dead);
        var plane = cold.TorsoBisection!.Value.Plane;
        ulong variation = visualSeed ?? TorsoBisectionPolicy.VisualSeedFor(worldSeed, defender, attacker, 0);
        Assert.Equal(TorsoBisectionPolicy.PlaneForDirection(hit.CutDirection, variation, hit.Direction, hit.VictimYaw), plane);
        return plane;
    }

    static CharacterCold Victim(out int chest)
    {
        var race = BisectionRace();
        var medical = MedicalState.Create(race, 50);
        chest = Enumerable.Range(0, medical.Parts.Count).Single(i => medical.Parts[i].Template.StringId == ArmourData.ChestPartId);
        return new CharacterCold { Race = race, Medical = medical, Fighter = new Fighter(null) };
    }

    static RaceData BisectionRace(bool robot = false) => new()
    {
        StringId = Human.StringId,
        Name = Human.Name,
        IsRobot = robot,
        StatMultipliers = Human.StatMultipliers,
        Anatomy = [.. Human.Anatomy.Select(e => e.Part == Chest ? e with { Part = CanonicalChest } : e)],
    };
}
