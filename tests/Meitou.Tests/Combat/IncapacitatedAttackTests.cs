using System.Collections.Concurrent;
using System.Numerics;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Ai;
using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Locks;
using static Meitou.Tests.Bodies.BodyFixtures;
using static Meitou.Tests.Combat.CombatFixtures;
using static Meitou.Tests.Combat.DuelFixture;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Combat;

public class IncapacitatedAttackTests
{
    sealed class Scene : IDisposable
    {
        public SimWorld World { get; }
        public CombatSystem Combat { get; }
        public CharacterId Attacker { get; }
        public CharacterId Target { get; }
        public CharacterCold Victim => World.Characters.Cold(Target.Slot)!;
        public ConcurrentQueue<CombatEvent> Events { get; } = new();
        public Scene(float distance = 14, bool fatal = false, int extraHits = 1, bool gore = true)
        {
            var bodies = BodyOptions.Default with { Gore = gore };
            if (fatal) bodies.ForcedHitPart = (Head.StringId, Head.Name);
            Combat = new([Cut("down", direction: 1) with { MaxSimultaneousHits = extraHits }, Cut("side", chance: 1000, direction: 2)], Constants, BaseGame, bodies,
                new AnimationLengths().Add("down", 0.9f).Add("side", 0.9f), new CombatOptions { TickMedical = false, RecordLog = true });
            World = new(new WorldSettings { Seed = 31, TickSeconds = 1f / 30, PublishSnapshots = false }, new OpenGroundWalkability((_, _) => 150), [Combat, Senses(Combat)]);
            World.CombatEvents = Events;
            var attacker = new Combatant { Attack = 100, WeaponSkill = fatal ? 100 : 10, Dexterity = fatal ? 100 : 10, Strength = fatal ? 100 : 10, Quality = fatal ? 1 : 0 }.MakeCold();
            attacker.IsPlayer = true; attacker.Faction = 0;
            Attacker = World.Characters.Spawn(new() { Position = new(1000, 150, 1000), Yaw = MathF.PI / 2, Health = 100 }, attacker, 0);
            var victim = new Combatant { Defence = 0, Toughness = 1 }.MakeCold();
            victim.Faction = 1;
            Target = World.Characters.Spawn(new() { Position = new(1000 + distance, 150, 1000), Yaw = -MathF.PI / 2, Health = 100 }, victim, 0);
        }
        public void Order(bool picked = true, bool allow = true) => World.Commands.Enqueue(new AttackOrder([Attacker], Target) { Picked = picked, AllowIncapacitated = allow, Tick = World.Tick });
        public void ToHit()
        {
            for (int i = 0; i < 600 && Combat.Log.All(hit => hit.Outcome != BlowOutcome.Hit); i++) World.RunTick();
            Assert.Contains(Combat.Log, hit => hit.Outcome == BlowOutcome.Hit && hit.Defender == Target.Slot);
        }
        public void Dispose() => World.Dispose();
    }

    [Theory]
    [InlineData(false, OrderVerdict.Run)]
    [InlineData(true, OrderVerdict.Gone)]
    public void Native_order_admission_is_not_tightened_by_the_disabled_helpless_target_extension(bool gore, OrderVerdict expected)
    {
        using var scene = new Scene(gore: gore);
        scene.Victim.InSomething = HeldState.Imprisoned;
        var order = new OrderEntry(TaskType.FOCUSED_MELEE_ATTACK, scene.Combat) { Target = scene.Target };
        // Native admission considers consciousness here; Act applies the furniture gate later.
        Assert.Equal(expected, ((IOrderOwner)scene.Combat).CanRun(scene.World, scene.Attacker.Slot, order).Verdict);
    }

    [Fact]
    public void Disabled_gore_rejects_an_explicit_attack_on_a_living_unconscious_enemy()
    {
        using var scene = new Scene(gore: false);
        scene.Victim.Medical!.KnockOut(10000);
        scene.Order();
        scene.World.RunTicks(300);
        Assert.Empty(scene.Combat.Log);
        Assert.Empty(scene.Events);
        Assert.True(scene.Victim.Medical.Incapacitated);
        Assert.False(scene.Victim.Medical.Dead);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void A_manual_order_can_hit_a_living_unconscious_sleeping_or_getting_up_enemy(int state)
    {
        using var scene = new Scene();
        if (state == 0) scene.Victim.Medical!.KnockOut(600);
        else if (state == 1) scene.Victim.InSomething = HeldState.Bed;
        else scene.Victim.GettingUpUntil = 10000;
        scene.Order();
        scene.ToHit();
        var hit = Assert.Single(scene.Combat.Log, h => h.Outcome == BlowOutcome.Hit);
        Assert.True(hit.Cut > 0);
        Assert.Equal(1, scene.Combat.Techniques[scene.Combat.StateOf(scene.Attacker).AttackTech].AttackDirection1);
        Assert.False(scene.Victim.Medical!.Dead);
        Assert.Contains(scene.Events, e => e.Kind == CombatEventKind.Hit && e.Victim == scene.Target && e.Source == CombatHitSource.Melee);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Automatic_or_ordinary_attack_orders_do_not_start_a_fight_with_a_downed_target(bool picked, bool allow)
    {
        using var scene = new Scene();
        scene.Victim.Medical!.KnockOut(600);
        scene.Order(picked, allow);
        scene.World.RunTicks(180);
        Assert.Empty(scene.Combat.Log);
        Assert.False(scene.World.Characters.Cold(scene.Attacker.Slot)!.InCombat);
        Assert.False(scene.Combat.StateOf(scene.Attacker).AllowIncapacitated);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_added_permission_does_not_allow_attacking_a_friendly_downed_character_or_a_corpse(bool corpse)
    {
        using var scene = new Scene();
        if (corpse) scene.Victim.Medical!.KillFromBisection();
        else { scene.Victim.Faction = 0; scene.Victim.Medical!.KnockOut(600); }
        scene.Order();
        scene.World.RunTicks(180);
        Assert.Empty(scene.Combat.Log);
        Assert.Empty(scene.Events);
        Assert.False(scene.World.Characters.Cold(scene.Attacker.Slot)!.InCombat);
    }

    [Fact]
    public void An_incapacitated_target_that_becomes_friendly_during_the_windup_is_left_alone()
    {
        using var scene = new Scene();
        scene.Victim.Medical!.KnockOut(600);
        scene.Order();
        for (int i = 0; i < 180 && scene.Combat.StateOf(scene.Attacker).AttackTech < 0; i++) scene.World.RunTick();
        Assert.True(scene.Combat.StateOf(scene.Attacker).AttackTech >= 0);
        scene.Victim.Faction = 0;
        scene.World.RunTicks(180);
        Assert.Empty(scene.Combat.Log);
        Assert.False(scene.Combat.StateOf(scene.Attacker).AllowIncapacitated);
    }

    [Fact]
    public void A_ground_strike_closes_in_and_does_not_sweep_other_unconscious_enemies()
    {
        using var scene = new Scene(distance: 80, extraHits: 5);
        scene.Victim.Medical!.KnockOut(600);
        var other = new Combatant().MakeCold(); other.Faction = 1; other.Medical!.KnockOut(600);
        var extra = scene.World.Characters.Spawn(new() { Position = new(1080, 150, 1002), Health = 100 }, other, 0);
        scene.Order(); scene.ToHit();
        var state = scene.Combat.StateOf(scene.Attacker);
        Assert.InRange(state.Reach, 1, 8);
        var from = scene.World.Characters.Previous[scene.Attacker.Slot].Position;
        var target = scene.World.Characters.Previous[scene.Target.Slot].Position;
        Assert.True(CombatTuning.Gap(from, target, scene.World.Characters.Cold(scene.Attacker.Slot)!, scene.Victim) <= state.Reach + 0.1f);
        Assert.DoesNotContain(scene.Combat.Log, hit => hit.Defender == extra.Slot);
        Assert.All(other.Medical.Parts, part => Assert.Equal(part.MaxHp, part.Flesh));
    }

    [Fact]
    public void A_finishing_blow_has_normal_damage_and_wound_events_and_stops_when_the_enemy_dies()
    {
        using var scene = new Scene(fatal: true);
        scene.Victim.Medical!.KnockOut(600);
        scene.Victim.Medical.Parts[HeadIx].Flesh = -90;
        scene.Order(); scene.ToHit();
        Assert.True(scene.Victim.Medical.Dead);
        var events = scene.Events.ToArray();
        var hit = Assert.Single(events, e => e.Kind == CombatEventKind.Hit);
        var death = Assert.Single(events, e => e.Kind == CombatEventKind.Died);
        Assert.Equal(HeadIx, hit.Part);
        Assert.True(hit.Damage.Cut > 0);
        Assert.Equal((hit.Tick, hit.Attacker, hit.Victim), (death.Tick, death.Attacker, death.Victim));
        Assert.True(Array.IndexOf(events, hit) < Array.IndexOf(events, death));
        int count = scene.Combat.Log.Count;
        scene.World.RunTicks(180);
        Assert.Equal(count, scene.Combat.Log.Count);
        Assert.False(scene.Combat.StateOf(scene.Attacker).Fighting);
    }

    [Fact]
    public void Ground_targets_prefer_a_descending_clip_and_refuse_a_distant_air_swing()
    {
        var query = new AttackQuery { Category = (int)WeaponCategory.Katanas, Skill = 50, UnarmedSkill = 50, Encumbrance = 1, WeaponLength = 21, Scale = 1,
            Gap = 1, Previous = -1, LeftArm = true, RightArm = true, GroundTarget = true };
        var techniques = new[] { Cut("side", chance: 1000, direction: 2), Cut("down", direction: 1) };
        Assert.Equal(1, TechniqueChooser.ChooseAttack(techniques, query, 0, out float reach));
        Assert.InRange(reach, 2, 8);
        Assert.Equal(-1, TechniqueChooser.ChooseAttack(techniques, query with { Gap = reach + 0.01f }, 0, out _));
        Assert.Equal(0, TechniqueChooser.ChooseAttack(techniques, query with { GroundTarget = false }, 0, out _));
    }
}
