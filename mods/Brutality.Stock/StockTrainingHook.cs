using HarmonyLib;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Carrying;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Items;

namespace Meitou.Brutality;

internal readonly record struct TrainingContact(float Remaining, float Challenge);

/// <summary>Captures the native melee target's health and challenge before damage; the observer alone awards XP.</summary>
internal static class StockTrainingHook
{
    [ThreadStatic] static Pending? current;

    sealed class Pending(World world, CombatSystem combat, StockProgression progression, CharacterId attacker, CharacterId victim,
        MedicalState medical, CombatSlot target, int sequence, int blow)
    {
        public readonly World World = world;
        public readonly CombatSystem Combat = combat;
        public readonly StockProgression Progression = progression;
        public readonly CharacterId Attacker = attacker, Victim = victim;
        public readonly MedicalState Medical = medical;
        public readonly CombatSlot Target = target;
        public readonly long Tick = world.Tick;
        public readonly int Sequence = sequence, Blow = blow;
        public int Part = -1;
        public ItemInstance? Weapon;
        public RaceData? OrganicVictim;
        public float PartMaximum;
        public TrainingContact? Captured;
    }

    internal static void Install(ModContext context)
    {
        var apply = AccessTools.Method(typeof(CombatSystem), nameof(CombatSystem.Apply), [typeof(World), typeof(Effect).MakeByRefType()])
            ?? throw new MissingMethodException(typeof(CombatSystem).FullName, nameof(CombatSystem.Apply));
        var medical = AccessTools.Method(typeof(MedicalState), nameof(MedicalState.ApplyHit),
            [typeof(int), typeof(HitDamage).MakeByRefType(), typeof(MedicalContext).MakeByRefType(), typeof(List<MedicalEvent>)])
            ?? throw new MissingMethodException(typeof(MedicalState).FullName, nameof(MedicalState.ApplyHit));
        context.Harmony.Patch(apply, prefix: new HarmonyMethod(typeof(StockTrainingHook), nameof(ApplyPrefix)),
            finalizer: new HarmonyMethod(typeof(StockTrainingHook), nameof(ApplyFinalizer)));
        context.Harmony.Patch(medical, prefix: new HarmonyMethod(typeof(StockTrainingHook), nameof(MedicalPrefix)));
    }

    static void ApplyPrefix(CombatSystem __instance, World world, in Effect e, CombatSlot[] ____write, out Pending? __state)
    {
        __state = current;
        current = null;
        var table = world.Characters;
        if (e.Kind != CombatSystem.BlowEffect || (uint)e.Source >= (uint)table.HighWater || (uint)e.Target >= (uint)table.HighWater
            || (uint)e.Source >= (uint)____write.Length || (uint)e.Target >= (uint)____write.Length
            || ____write[e.Source].CancelTick == (int)world.Tick || StockProgression.For(world) is not { } progression
            || table.Cold(e.Target)?.Medical is not { } medical) return;

        // An emitted blow can still land after the attacker has ended or replaced the swing in Act.
        current = new(world, __instance, progression, table.IdOf(e.Source), table.IdOf(e.Target), medical,
            ____write[e.Target], (int)e.Amount, e.Arg);
        if (StockWeaponStains.Enabled)
            current.Weapon = StockWeaponItems.Swing(table.Cold(e.Source), __instance, e);
    }

    static Exception? ApplyFinalizer(Exception? __exception, Pending? __state)
    {
        current = __state;
        return __exception;
    }

    internal static void SandboxDamagePrefix(ref float globalDamageMultiplier)
    {
        if (current is { } pending) globalDamageMultiplier *= StockSandboxProfile.Outgoing(pending.World, pending.Attacker);
    }

    static void MedicalPrefix(MedicalState __instance, int part)
    {
        if (current is not { Part: < 0 } pending || !ReferenceEquals(pending.Medical, __instance)
            || (uint)part >= (uint)__instance.Parts.Count) return;
        pending.Part = part;
        if (pending.Weapon is not null && pending.World.Characters.Cold(pending.Victim.Slot)?.Race is { IsRobot: false } race
            && !__instance.Parts[part].IsRobotic(race))
            pending.OrganicVictim = race;
        pending.PartMaximum = __instance.Parts[part].MaxHp;

        var table = pending.World.Characters;
        var attacker = pending.Attacker;
        var victim = pending.Victim;
        var target = pending.Target;
        if (attacker == victim || !table.TryResolveNext(attacker, out int a) || !table.TryResolveNext(victim, out int d)
            || table.Cold(a) is not { Medical: { Dead: false, Incapacitated: false } sourceMedical, Stats: { } sourceStats, Fighter: { } sourceFighter } source
            || table.Cold(d) is not { Medical: { Dead: false, Incapacitated: false, PlayingDead: false } medical,
                Fighter: { CanAttack: true } fighter, Stats: { } targetStats, CarriedBy.IsNone: true } other
            || other.InSomething != HeldState.None || other.IsImprisoned || CharacterFacts.GettingUp(other, pending.Tick)
            || CarrySystem.Asleep(table, d) || target.Down) return;
        if (!fighter.IsAnimal && !medical.ArmWorks(left: true) && !medical.ArmWorks(left: false)
            && (medical.Prone || medical.Parts.Any(p => p.IsLeg && p.Limb is LimbState.Stump or LimbState.Crushed))) return;

        var relations = pending.Combat.Relations;
        bool allied = source.Faction == other.Faction || relations is not null && relations.Contains(source.Faction) && relations.Contains(other.Faction)
            && (relations.IsAlly(source.Faction, other.Faction) || relations.IsAlly(other.Faction, source.Faction));
        bool retaliating = target.TargetSlot == attacker.Slot && target.TargetGeneration == attacker.Generation;
        if (allied || !(retaliating || CharacterFacts.RegardsEnemy(relations, source.Faction, other.Faction))
            || !(other.InCombat || target.TargetSlot >= 0 || target.AttackTech >= 0 || target.ReactTech >= 0)) return;

        static float Effective(CharacterStats stats, MedicalState state, StatsEnumerated stat) => stats[stat] * stats.Scale * state.StatMultiplier(stat);
        float challenge = Math.Clamp(pending.Progression.Xp.SkillDifferenceFactor(
            Effective(sourceStats, sourceMedical, sourceFighter.Unarmed ? StatsEnumerated.MartialArts : StatsEnumerated.MeleeAttack),
            Effective(targetStats, medical, StatsEnumerated.MeleeDefence)), 0, 6);
        var struck = medical.Parts[part];
        pending.Captured = new(MathF.Max(0, struck.Flesh + struck.MaxHp - struck.Stun), challenge);
    }

    internal static bool TryGetTorsoMaximum(World world, in CombatHit hit, out float maximum) =>
        TryGetPartMaximum(world, hit, out maximum);

    internal static bool TryGetPartMaximum(World world, in CombatHit hit, out float maximum)
    {
        maximum = 0;
        if (current is not { PartMaximum: > 0 } pending || !ReferenceEquals(pending.World, world)
            || pending.Tick != hit.Tick || pending.Attacker != hit.Attacker || pending.Victim != hit.Victim
            || pending.Sequence != hit.Sequence || pending.Blow != hit.Blow || pending.Part != hit.Part) return false;
        maximum = pending.PartMaximum;
        return true;
    }

    internal static bool TryGetWeapon(World world, in CombatHit hit, out ItemInstance item, out RaceData race)
    {
        item = null!;
        race = null!;
        if (current is not { Weapon: { } source, OrganicVictim: { } organic } pending || !ReferenceEquals(pending.World, world)
            || pending.Tick != hit.Tick || pending.Attacker != hit.Attacker || pending.Victim != hit.Victim
            || pending.Sequence != hit.Sequence || pending.Blow != hit.Blow || pending.Part != hit.Part) return false;
        item = source;
        race = organic;
        return true;
    }

    internal static bool TryConsume(World world, in CombatHit hit, out TrainingContact contact)
    {
        contact = default;
        if (hit.Source != CombatHitSource.Melee || !hit.VictimWasAlive || current is not { Captured: { } captured } pending
            || !ReferenceEquals(pending.World, world) || pending.Tick != hit.Tick || pending.Attacker != hit.Attacker || pending.Victim != hit.Victim
            || pending.Sequence != hit.Sequence || pending.Blow != hit.Blow || pending.Part != hit.Part) return false;
        pending.Captured = null;
        contact = captured;
        return true;
    }
}
