using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Gameplay.Combat;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;

namespace Meitou.Brutality;

/// <summary>Lets the native medical death path decide whether a head can be severed, then applies Brutality's cut policy.</summary>
internal static class StockHeadGate
{
    sealed class Contact(World world, MedicalState medical, CharacterId attacker, CharacterId victim,
        CombatHitSource source, int direction, int sequence, int blow)
    {
        internal readonly World World = world;
        internal readonly MedicalState Medical = medical;
        internal readonly CharacterId Attacker = attacker, Victim = victim;
        internal readonly CombatHitSource Source = source;
        internal readonly int Direction = direction, Sequence = sequence, Blow = blow;
        internal int Part = -1;
        internal DamagePacket? Packet;
        internal StockHeadRules.CutRequest? Accepted;
        internal bool MedicalActive;
    }

    [ThreadStatic] static Contact? active;
    [ThreadStatic] static MedicalState? projectile;

    internal static bool Install(ModContext context)
    {
        var combat = typeof(CombatSystem);
        var resolver = typeof(HitResolver).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == nameof(HitResolver.LandDetailed) && method.IsGenericMethodDefinition);
        var cover = AccessTools.Inner(combat, "CoverRoll");
        var bolt = AccessTools.Inner(combat, "BoltCover");
        if (cover is null || bolt is null) return false;
        var patches = new (MethodInfo? Host, string Prefix, string? Finalizer)[]
        {
            (AccessTools.Method(combat, nameof(CombatSystem.Apply), [typeof(World), typeof(Effect).MakeByRefType()]), nameof(BeginMelee), nameof(EndContact)),
            (AccessTools.Method(combat, nameof(CombatSystem.Execute)), nameof(BeginExecution), nameof(EndContact)),
            (resolver.MakeGenericMethod(cover), nameof(BeforeArmour), null),
            (resolver.MakeGenericMethod(bolt), nameof(BeginProjectile), nameof(EndProjectile)),
            (AccessTools.Method(typeof(MedicalState), nameof(MedicalState.ApplyHit), [typeof(int), typeof(HitDamage).MakeByRefType(),
                typeof(MedicalContext).MakeByRefType(), typeof(List<MedicalEvent>)]), nameof(BeginMedical), nameof(EndMedical)),
            (AccessTools.Method(typeof(MedicalState), nameof(MedicalState.Decapitate)), nameof(Decapitate), null),
        };
        var installed = new List<(MethodInfo Host, MethodInfo Hook)>();
        try
        {
            foreach (var (host, prefix, finalizer) in patches)
            {
                if (host is null) throw new MissingMethodException(prefix);
                HarmonyMethod? Patch(string? name)
                {
                    if (name is null) return null;
                    var hook = AccessTools.Method(typeof(StockHeadGate), name);
                    installed.Add((host, hook));
                    return new(hook);
                }
                context.Harmony.Patch(host, prefix: Patch(prefix), finalizer: Patch(finalizer));
            }
            return true;
        }
        catch (Exception error)
        {
            foreach (var (host, hook) in installed.AsEnumerable().Reverse()) context.Harmony.Unpatch(host, hook);
            context.Log($"Brutality medical head cuts unavailable: {error.GetBaseException().Message}");
            return false;
        }
    }

    static void BeginMelee(World world, in Effect e, CombatTechnique[] ____techniques, out Contact? __state)
    {
        __state = active;
        active = null;
        int technique = (int)e.Position.X;
        if (e.Kind != CombatSystem.BlowEffect || (uint)technique >= (uint)____techniques.Length) return;
        var table = world.Characters;
        var prev = table.Previous;
        if ((uint)e.Source >= (uint)prev.Length || (uint)e.Target >= (uint)prev.Length) return;
        var a = prev[e.Source];
        var d = prev[e.Target];
        int direction = HitOutcomes.Facing(____techniques[technique].DirectionOf(e.Arg), HitOutcomes.Behind(a.Position, d.Position, d.Yaw));
        active = Begin(world, e.Source, e.Target, CombatHitSource.Melee, direction, (int)e.Amount, e.Arg);
    }

    static void BeginExecution(World world, int a, int d, int blow, out Contact? __state)
    {
        __state = active;
        active = Begin(world, a, d, CombatHitSource.Execution, -1, 0, blow);
    }

    static Contact? Begin(World world, int a, int d, CombatHitSource source, int direction, int sequence, int blow)
    {
        var table = world.Characters;
        return a != d && (uint)a < (uint)table.HighWater && (uint)d < (uint)table.HighWater
            && table.Cold(d)?.Medical is { Dead: false } medical
            ? new(world, medical, table.IdOf(a), table.IdOf(d), source, direction, sequence, blow) : null;
    }

    static Exception? EndContact(Exception? __exception, Contact? __state) { active = __state; return __exception; }

    static void BeforeArmour(MedicalState medical, in DamagePacket packet)
    {
        if (active is { } contact && ReferenceEquals(medical, contact.Medical)) contact.Packet = packet;
    }

    static void BeginProjectile(MedicalState medical, out MedicalState? __state) { __state = projectile; projectile = medical; }
    static Exception? EndProjectile(Exception? __exception, MedicalState? __state) { projectile = __state; return __exception; }

    static void BeginMedical(MedicalState __instance, int part, in HitDamage hit, out Contact? __state)
    {
        __state = null;
        if (active is not { MedicalActive: false, Part: < 0 } contact || !ReferenceEquals(contact.Medical, __instance)
            || (uint)part >= (uint)__instance.Parts.Count || __instance.Parts[part] is not { Template.Type: BodyPartType.Head } struck) return;
        __state = contact;
        contact.MedicalActive = true;
        contact.Part = part;
        var world = contact.World;
        var table = world.Characters;
        if (table.Cold(contact.Attacker.Slot)?.Fighter is not { IsAnimal: false } fighter
            || table.Cold(contact.Victim.Slot) is not { Fighter.IsAnimal: false, Race: { } race }
            || StockProgression.For(world) is not { } progression) return;

        float level = progression.LevelFor(world, contact.Attacker);
        ulong seed = HeadCutPolicy.VisualSeedFor(world.Seed, contact.Victim, contact.Attacker,
            CombatRolls.BlowCounter(contact.Attacker.Slot, contact.Sequence, contact.Blow, 0));
        var victim = table.Previous[contact.Victim.Slot];
        var travel = victim.Position - table.Previous[contact.Attacker.Slot].Position;
        HeadCutState? cut;
        if (fighter.Unarmed)
            cut = !race.IsRobot && contact.Source == CombatHitSource.Melee ? HeadCutPolicy.MartialArtsBurst(level, hit, struck.MaxHp, seed) : null;
        else
        {
            var packet = contact.Packet ?? new DamagePacket(hit.Cut, hit.Blunt, hit.Pierce, hit.ExtraStun, 1, 0);
            var mechanism = CombatWoundMechanisms.Classify(fighter, packet, contact.Direction);
            cut = HeadCutPolicy.Candidate(level, mechanism, contact.Direction, packet.Cut, seed, new Vector2(travel.X, travel.Z), victim.Yaw);
        }
        if (cut is { IsValid: true } shape) contact.Accepted = new(world.Tick, seed, shape);
    }

    static Exception? EndMedical(Exception? __exception, Contact? __state)
    { if (__state is not null) __state.MedicalActive = false; return __exception; }

    static bool Decapitate(MedicalState __instance)
    {
        if (ReferenceEquals(projectile, __instance)) return false;
        // A stock debug/body call has no scoped combat hit and retains its own behavior.
        return active is not { MedicalActive: true } contact || !ReferenceEquals(contact.Medical, __instance)
            || contact.Accepted is not null;
    }

    internal static StockHeadRules.CutRequest? Result(World world, in CombatHit hit) => active is { } contact
        && ReferenceEquals(contact.World, world) && contact.Victim == hit.Victim && contact.Attacker == hit.Attacker
        && contact.Part == hit.Part && contact.Sequence == hit.Sequence && contact.Blow == hit.Blow
        && contact.Source == hit.Source && contact.Medical.Decapitated ? contact.Accepted : null;

    internal static DamagePacket? PacketFor(World world, in CombatHit hit) => active is { } contact
        && ReferenceEquals(contact.World, world) && contact.Victim == hit.Victim && contact.Attacker == hit.Attacker
        && contact.Sequence == hit.Sequence && contact.Blow == hit.Blow && contact.Source == hit.Source ? contact.Packet : null;
}
