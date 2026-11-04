using System.Numerics;
using HarmonyLib;
using Meitou.Brutality;
using Meitou.Data.Gameplay.Combat;
using Meitou.Data.Save;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Locks;
using Meitou.Tests.Combat;
using static Meitou.Tests.Bodies.BodyFixtures;
using static Meitou.Tests.Combat.CombatFixtures;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Modding;

[Collection("Brutality native combat")]
public sealed class BrutalityIncapacitatedCombatTests
{
    sealed class Patches : IDisposable
    {
        readonly Harmony harmony = new("brutality.targeted.test." + Guid.NewGuid().ToString("N"));
        public Patches() => CombatAdapter.Install(harmony);
        public void Dispose() => harmony.UnpatchAll(harmony.Id);
    }
    sealed class Recorder : ICombatHitObserver
    {
        public readonly List<CombatHit> Hits = [];
        public void HitResolved(SimWorld world, in CombatHit hit) => Hits.Add(hit);
    }
    sealed class Scene : IDisposable
    {
        public CombatSystem Combat { get; }
        public BrutalityGameplaySystem Gameplay { get; }
        public IncapacitatedCombatAdapter Targeting { get; }
        public Recorder Observer { get; } = new();
        public SimWorld World { get; }
        public CharacterCold Attacker { get; } = new Combatant().MakeCold("attacker");
        public CharacterCold Victim { get; } = new Combatant().MakeCold("target");
        public CharacterId AttackerId { get; }
        public CharacterId VictimId { get; }
        public Scene(int padding = 0, int threads = 1)
        {
            var body = BodyOptions.Default with { ForcedHitPart = (Chest.StringId, Chest.Name), GlobalDamageMultiplier = .25f };
            Combat = new([Cut("cut left", direction: 2), Cut("combo", direction: 1)], Constants, BaseGame, body, DuelFixture.Lengths,
                new CombatOptions { TickMedical = false, RecordLog = true });
            Gameplay = new(Combat, BaseGame, body, (RngPurpose)0xB17A1171);
            Targeting = new(Combat);
            World = new(new WorldSettings { Seed = 13, Threads = threads, MinPartitionSize = 1, PublishSnapshots = false },
                new OpenGroundWalkability((_, _) => 150), [Targeting, Combat, Senses(Combat), Gameplay, Observer]);
            for (int i = 0; i < padding; i++) World.Characters.Spawn(new(), new(), 0);
            Attacker.IsPlayer = true; Attacker.Faction = 1; Victim.Faction = 2;
            AttackerId = World.Characters.Spawn(new() { Position = new(1000, 150, 1000), Health = 100 }, Attacker, 0);
            VictimId = World.Characters.Spawn(new() { Position = new(1014, 150, 1000), Health = 100 }, Victim, 0);
        }
        public void Order(int part, bool queued = false) => World.Commands.Enqueue(new TargetBodyPartAttackOrder([AttackerId], VictimId, part) { Tick = World.Tick, Queued = queued });
        public void UntilHit()
        {
            for (int i = 0; i < 500 && Observer.Hits.Count == 0; i++) World.RunTick();
            Assert.NotEmpty(Observer.Hits);
        }
        public void Dispose() => World.Dispose();
    }

    [Theory]
    [InlineData("unconscious")]
    [InlineData("bed")]
    [InlineData("prone")]
    [InlineData("getting up")]
    public void A_deliberate_living_helpless_target_uses_the_selected_part_and_a_ground_swing(string state)
    {
        using var patches = new Patches();
        using var scene = new Scene();
        switch (state)
        {
            case "unconscious": scene.Victim.Medical!.KnockOut(100); break;
            case "bed": scene.Victim.InSomething = HeldState.Bed; break;
            case "prone": scene.Victim.Medical!.StayingLow = true; break;
            case "getting up": scene.Victim.GettingUpUntil = 10000; break;
        }
        var before = scene.Victim.Medical!.Parts.Select(part => part.Flesh).ToArray();
        Assert.True(IncapacitatedCombatAdapter.CanAttack(scene.World, scene.AttackerId, scene.VictimId, HeadIx));
        scene.Order(HeadIx); scene.UntilHit();
        var hit = Assert.Single(scene.Observer.Hits);
        Assert.Equal(HeadIx, hit.Part); // Exact focus overrides the deliberately forced Chest setting.
        Assert.Equal(CombatHitSource.Melee, hit.Source);
        Assert.True(hit.VictimWasAlive);
        Assert.Contains(hit.CutDirection, new[] { 1, 7 });
        Assert.True(scene.Victim.Medical.Parts[HeadIx].Flesh < before[HeadIx]);
        for (int part = 1; part < before.Length; part++) Assert.Equal(before[part], scene.Victim.Medical.Parts[part].Flesh);
        if (state != "prone") Assert.Equal(0, Assert.Single(scene.Gameplay.ActorViews, row => row.Character == scene.AttackerId).Experience);
        Assert.Equal(GoreIntegrationGap.None, scene.Gameplay.Gaps);
    }

    [Theory]
    [InlineData("dead")]
    [InlineData("carried")]
    [InlineData("caged")]
    [InlineData("ally")]
    [InlineData("missing part")]
    public void Invalid_downed_targets_or_missing_anatomy_are_rejected_without_a_substitute_hit(string reason)
    {
        using var patches = new Patches();
        using var scene = new Scene();
        scene.Victim.Medical!.KnockOut(100);
        int part = LArm;
        switch (reason)
        {
            case "dead": scene.Victim.Medical.Butcher(); break;
            case "carried": scene.Victim.CarriedBy = scene.AttackerId; break;
            case "caged": scene.Victim.InSomething = HeldState.Imprisoned; break;
            case "ally": scene.Victim.Faction = scene.Attacker.Faction; break;
            case "missing part": scene.Victim.Medical.Sever(LArm, Context()); break;
        }
        Assert.False(IncapacitatedCombatAdapter.CanAttack(scene.World, scene.AttackerId, scene.VictimId, part));
        scene.Order(part); scene.World.RunTicks(100);
        Assert.Empty(scene.Observer.Hits);
        Assert.Empty(scene.Attacker.OrderQueue);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_target_dying_or_losing_the_chosen_part_during_windup_cancels_the_hit(bool removePart)
    {
        using var patches = new Patches();
        using var scene = new Scene();
        scene.Victim.Medical!.KnockOut(100);
        scene.Order(LArm); scene.World.RunTicks(2);
        if (removePart) scene.Victim.Medical.Sever(LArm, Context()); else scene.Victim.Medical.Butcher();
        var before = scene.Victim.Medical.Parts.Select(part => part.Flesh).ToArray();
        scene.World.RunTicks(150);
        Assert.Empty(scene.Observer.Hits);
        Assert.Equal(before, scene.Victim.Medical.Parts.Select(part => part.Flesh).ToArray());
        Assert.NotEqual(CharacterTask.Attack, (CharacterTask)scene.World.Characters.Previous[scene.AttackerId.Slot].Task);
    }

    [Fact]
    public void Stop_replaces_the_focused_order_and_does_not_leave_a_targeting_override()
    {
        using var patches = new Patches();
        using var scene = new Scene();
        scene.Victim.Medical!.KnockOut(100);
        scene.Order(HeadIx); scene.World.RunTicks(2);
        scene.World.Commands.Enqueue(new StopCommand([scene.AttackerId]) { Tick = scene.World.Tick });
        scene.World.RunTicks(100);
        Assert.Empty(scene.Observer.Hits);
        Assert.Empty(scene.Attacker.OrderQueue);
        Assert.False(scene.Targeting.TryTarget(scene.World, scene.AttackerId, scene.VictimId, true, out _));
    }

    [Fact]
    public void Saved_focused_orders_follow_durable_keys_after_slot_changes_and_wait_for_unresolved_targets()
    {
        using var patches = new Patches();
        var ownerHand = new Hand(25, 1, 2, 3, 4); var targetHand = new Hand(25, 1, 2, 8, 4);
        using var original = new Scene();
        original.Victim.Medical!.KnockOut(100);
        original.Order(HeadIx); original.Order(LArm, queued: true); original.World.RunTick();
        original.Gameplay.CaptureSaveIdentity(original.World, original.Attacker, original.AttackerId, ownerHand);
        original.Gameplay.CaptureSaveIdentity(original.World, original.Victim, original.VictimId, targetHand);
        using var bytes = new MemoryStream(); original.Targeting.Write(bytes);
        Assert.Equal(2, original.Attacker.OrderQueue.Count);

        using var restored = new Scene(padding: 3);
        restored.Victim.Medical!.KnockOut(100);
        restored.Gameplay.CaptureSaveIdentity(restored.World, restored.Attacker, restored.AttackerId, ownerHand);
        bytes.Position = 0; restored.Targeting.Read(bytes);
        restored.World.RunTick();
        Assert.Empty(restored.Attacker.OrderQueue);
        using var pending = new MemoryStream(); restored.Targeting.Write(pending);
        Assert.Contains(SaveIdentityAdapter.KeyFor(targetHand)!, System.Text.Encoding.UTF8.GetString(pending.ToArray()));
        restored.Gameplay.CaptureSaveIdentity(restored.World, restored.Victim, restored.VictimId, targetHand);
        restored.World.RunTick();
        Assert.Equal(2, restored.Attacker.OrderQueue.Count);
        Assert.All(restored.Attacker.OrderQueue, order => Assert.Equal(restored.VictimId, order.Target));
        restored.UntilHit();
        Assert.Equal(HeadIx, Assert.Single(restored.Observer.Hits).Part);
    }

    sealed class NoClaim : ICombatTargetRules
    {
        public bool TryTarget(SimWorld world, CharacterId attacker, CharacterId target, bool picked, out CombatTargetOverride rule) { rule = default; return false; }
    }
    [Fact]
    public void An_unclaimed_policy_changes_neither_native_combat_results_nor_hashes()
    {
        static (ulong Hash, CombatLogEntry[] Hits) Run(bool policy)
        {
            using var world = DuelFixture.NewWorld(31, 4, out var combat);
            if (policy) world.AddSystem(new NoClaim());
            DuelFixture.Pair(world, new Combatant(), new Combatant()); world.RunTicks(600);
            return (world.StateHash(), combat.Log.ToArray());
        }
        var plain = Run(false); var ruled = Run(true);
        Assert.Equal(plain.Hash, ruled.Hash); Assert.Equal(plain.Hits, ruled.Hits);
    }
}
