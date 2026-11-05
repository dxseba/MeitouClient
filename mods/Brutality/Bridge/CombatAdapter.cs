using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Data.Gameplay.Combat;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Items;

namespace Meitou.Brutality;

/// <summary>Adapts the pinned native hit pipeline without replacing its damage, part selection, or event processing.</summary>
public static partial class CombatAdapter
{
    [ThreadStatic] static Contact? current;
    static RngPurpose purpose;
    internal static bool HasActiveContact => current is not null;

    sealed class Contact
    {
        public required World World;
        public required BrutalityGameplaySystem Gameplay;
        public required CharacterId Attacker, Victim;
        public required MedicalState Medical;
        public required CombatSlot Target;
        public required CombatHitSource Source;
        public required int Sequence, Blow, Direction;
        public required Vector3 Travel;
        public required float Yaw, Brutality;
        public required bool LowStrike, Intended, WasAlive, WasIncapacitated;
        public bool Execution, RawCaptured, PrimaryFinished, MedicalActive, ApplyingCounterpart, HeadGate;
        public DamagePacket Packet;
        public MedicalContext Context;
        public CombatConstants Constants = null!;
        public float Toughness;
        public GoreHitBridge? Before;
        public ItemInstance? ActualWeapon;
        public HeadCutState? Head;
        public int Part = -1;
        public HitDamage Damage;
        public ArmourAbsorbed Absorbed;
        public CombatHit Hit => new(World.Tick, Source, Attacker, Victim, Sequence, Blow, Part, Damage, Absorbed,
            World.Characters.Previous[Victim.Slot].Position, Travel, Yaw, Direction, LowStrike, Intended, WasAlive, WasIncapacitated);
    }

    public static void Install(ModContext context) => Install(context.Harmony, context.RngPurpose);

    internal static void Install(Harmony harmony, RngPurpose randomPurpose = (RngPurpose)0xB17A1171)
    {
        purpose = randomPurpose;
        Patch(harmony, AccessTools.Method(typeof(CombatSystem), nameof(CombatSystem.Apply)), nameof(ApplyPrefix), finalizer: nameof(ContactFinalizer));
        Patch(harmony, AccessTools.Method(typeof(CombatSystem), "BoltHit"), nameof(BoltPrefix), finalizer: nameof(ContactFinalizer));
        Patch(harmony, AccessTools.Method(typeof(CombatSystem), nameof(CombatSystem.Execute)), nameof(ExecutionPrefix), nameof(ExecutionPostfix), nameof(ContactFinalizer));
        // These are the actual value-type instantiations called by native melee/execution and projectile contacts.
        // Patching the non-generic Resolve helper would rely on it not having been inlined into an earlier JIT caller.
        var land = typeof(HitResolver).GetMethods(BindingFlags.Public | BindingFlags.Static).Single(IsLandCore);
        foreach (string cover in new[] { "CoverRoll", "BoltCover" })
        {
            var type = typeof(CombatSystem).GetNestedType(cover, BindingFlags.NonPublic)
                ?? throw new MissingMemberException(typeof(CombatSystem).FullName, cover);
            Patch(harmony, land.MakeGenericMethod(type), nameof(LandPrefix), nameof(LandPostfix));
        }
        Patch(harmony, AccessTools.Method(typeof(MedicalState), nameof(MedicalState.ApplyHit)), nameof(MedicalPrefix), nameof(MedicalPostfix), nameof(MedicalFinalizer));
        Patch(harmony, AccessTools.Method(typeof(MedicalState), nameof(MedicalState.Decapitate)), nameof(DecapitationPrefix));
    }

    // Patch only the exact-part core. The original CLR overload forwards here, so patching both would observe one contact twice.
    static bool IsLandCore(MethodInfo method)
    {
        if (method.Name != nameof(HitResolver.LandDetailed) || !method.IsGenericMethodDefinition
            || method.GetGenericArguments().Length != 1) return false;
        Type[] parameters = [typeof(MedicalState), typeof(MedicalContext).MakeByRefType(), typeof(DamagePacket).MakeByRefType(),
            typeof(ReadOnlySpan<ArmourPiece>), method.GetGenericArguments()[0], typeof(float), typeof(bool), typeof(bool),
            typeof(CombatConstants), typeof(List<MedicalEvent>), typeof(int?)];
        return method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(parameters);
    }

    static void Patch(Harmony harmony, MethodInfo original, string? prefix = null, string? postfix = null, string? finalizer = null) =>
        harmony.Patch(original, Method(prefix), Method(postfix), finalizer: Method(finalizer));
    static HarmonyMethod? Method(string? name) => name is null ? null : new HarmonyMethod(AccessTools.Method(typeof(CombatAdapter), name));

    static Contact? Create(World world, CharacterId attacker, int victim, CombatHitSource source, int sequence, int blow,
        int direction, bool lowStrike, bool intended, Vector3 travel, in CombatSlot target)
    {
        if (BrutalityRuntime.For(world) is not { } gameplay || (uint)victim >= (uint)world.Characters.HighWater
            || world.Characters.Cold(victim)?.Medical is not { } medical) return null;
        return new Contact
        {
            World = world, Gameplay = gameplay, Attacker = attacker, Victim = world.Characters.IdOf(victim), Medical = medical, Target = target,
            Source = source, Sequence = sequence, Blow = blow, Direction = direction, LowStrike = lowStrike, Intended = intended, Travel = travel,
            Yaw = world.Characters.Previous[victim].Yaw, Brutality = gameplay.LevelOf(world, attacker),
            WasAlive = !medical.Dead, WasIncapacitated = medical.Incapacitated,
        };
    }

    static void ApplyPrefix(CombatSystem __instance, World world, in Effect e, CombatSlot[] ___write, out Contact? __state)
    {
        __state = current; current = null;
        if (e.Kind != CombatSystem.BlowEffect || (uint)e.Source >= (uint)___write.Length || (uint)e.Target >= (uint)___write.Length) return;
        var state = ___write[e.Source];
        if (state.AttackSeq != (int)e.Amount || (uint)state.AttackTech >= (uint)__instance.Techniques.Count) return;
        var table = world.Characters; var prev = table.Previous;
        var tech = __instance.Techniques[state.AttackTech];
        int direction = HitOutcomes.Facing(tech.DirectionOf(e.Arg), HitOutcomes.Behind(prev[e.Source].Position, prev[e.Target].Position, prev[e.Target].Yaw));
        current = Create(world, table.IdOf(e.Source), e.Target, CombatHitSource.Melee, (int)e.Amount, e.Arg, direction, tech.LowStrike,
            state.TargetSlot == e.Target && state.TargetGeneration == prev[e.Target].Generation,
            prev[e.Target].Position - prev[e.Source].Position, ___write[e.Target]);
        if (current is { } contact)
        {
            contact.Travel = new(contact.Travel.X, 0, contact.Travel.Z);
            int strikeLimb = e.Arg <= 1 ? tech.Limb1 : tech.Limb2;
            if (table.Cold(e.Source)?.Fighter is { IsAnimal: false, Unarmed: false } fighter && strikeLimb == 4) contact.ActualWeapon = fighter.Weapon?.SourceItem;
        }
    }

    static readonly Type ProjectileType = typeof(CombatSystem).GetNestedType("Projectile", BindingFlags.NonPublic)!;
    static readonly FieldInfo ProjectileId = Field("Id"), ProjectileShooter = Field("Shooter"), ProjectileGeneration = Field("ShooterGeneration"), ProjectileDirection = Field("Direction");
    static FieldInfo Field(string name) => ProjectileType.GetField(name, BindingFlags.Public | BindingFlags.Instance)
        ?? throw new MissingMemberException(ProjectileType.FullName, name);

    static void BoltPrefix(World world, object[] __args, CombatSlot[] ___write, out Contact? __state)
    {
        __state = current; current = null;
        object projectile = __args[1]; int victim = (int)__args[2];
        if ((uint)victim >= (uint)___write.Length) return;
        current = Create(world, new((int)ProjectileShooter.GetValue(projectile)!, (int)ProjectileGeneration.GetValue(projectile)!), victim,
            CombatHitSource.Projectile, (int)ProjectileId.GetValue(projectile)!, -1, -1, false, (bool)__args[3],
            (Vector3)ProjectileDirection.GetValue(projectile)!, ___write[victim]);
    }

    static void ExecutionPrefix(World world, int a, int d, int blow, CombatSlot[] ___write, out Contact? __state)
    {
        __state = current; current = null;
        if ((uint)a >= (uint)world.Characters.HighWater || (uint)d >= (uint)___write.Length) return;
        var prev = world.Characters.Previous;
        current = Create(world, world.Characters.IdOf(a), d, CombatHitSource.Melee, blow, 0, -1, false, true,
            new(prev[d].Position.X - prev[a].Position.X, 0, prev[d].Position.Z - prev[a].Position.Z), ___write[d]);
        if (current is { } contact)
        {
            contact.Execution = true;
            if (world.Characters.Cold(a)?.Fighter is { IsAnimal: false, Unarmed: false } fighter) contact.ActualWeapon = fighter.Weapon?.SourceItem;
        }
    }

    static void ExecutionPostfix()
    {
        // Execute has no generic observer emission in API 1. Deliver this captured contact only to this mod, once native consequences are complete.
        if (current is { Execution: true, PrimaryFinished: true, Part: >= 0 } contact)
        {
            contact.Gameplay.HitResolved(contact.World, contact.Hit);
            ImpactEffects.ForWorld(contact.World)?.HitResolved(contact.World, contact.Hit);
        }
    }

    static Exception? ContactFinalizer(Exception? __exception, Contact? __state)
    {
        current = __state;
        return __exception;
    }

    static void LandPrefix(MedicalState medical, in MedicalContext ctx, in DamagePacket packet, float toughness, CombatConstants c)
    {
        if (current is not { PrimaryFinished: false, ApplyingCounterpart: false } contact || !ReferenceEquals(contact.Medical, medical)) return;
        contact.RawCaptured = true; contact.Packet = packet; contact.Context = ctx; contact.Toughness = toughness; contact.Constants = c;
    }
    static void LandPostfix((int Part, HitDamage Damage, ArmourAbsorbed Absorbed) __result)
    {
        if (current is not { RawCaptured: true } contact || contact.Part != __result.Part) return;
        contact.Absorbed = __result.Absorbed;
    }

    static void MedicalPrefix(MedicalState __instance, int part, in HitDamage hit, in MedicalContext ctx, out Contact? __state)
    {
        __state = null;
        if (current is not { RawCaptured: true, PrimaryFinished: false, ApplyingCounterpart: false } contact
            || !ReferenceEquals(contact.Medical, __instance) || (uint)part >= (uint)__instance.Parts.Count) return;
        __state = contact;
        contact.Part = part; contact.Damage = hit; contact.MedicalActive = true;
        var attacker = contact.World.Characters.TryResolveNext(contact.Attacker, out int slot) ? contact.World.Characters.Cold(slot) : null;
        var mechanism = contact.Source == CombatHitSource.Projectile ? CombatWoundMechanism.Thrust
            : attacker?.Fighter is { } fighter ? CombatWoundMechanisms.Classify(fighter, contact.Packet, contact.Direction) : CombatWoundMechanism.Unspecified;
        contact.Before = contact.Gameplay.CaptureBeforeHit(contact.World, contact.Attacker, contact.Victim, part, contact.Packet, mechanism, ctx, contact.Target);
        // A candidate changes the look only after the native vital/head/severance gate actually accepts this damage.
        if (contact.World.Characters.Cold(contact.Victim.Slot)?.Fighter is not { IsAnimal: true })
            contact.Head = HeadCutPolicy.Candidate(contact.Brutality, mechanism, contact.Direction, contact.Packet.Cut,
                HeadCutPolicy.VisualSeedFor(contact.World.Seed, contact.Victim, contact.Attacker,
                    CombatRolls.BlowCounter(contact.Attacker.Slot, contact.Sequence, contact.Blow, 0)), new(contact.Travel.X, contact.Travel.Z), contact.Yaw);
    }

    static bool DecapitationPrefix(MedicalState __instance)
    {
        if (current is not { MedicalActive: true } contact || !ReferenceEquals(contact.Medical, __instance)) return true;
        contact.HeadGate = true;
        return contact.Source != CombatHitSource.Projectile && contact.Head is { IsValid: true, Mode: not HeadCutMode.AttachedSplit };
    }

    static void MedicalPostfix(List<MedicalEvent>? events, Contact? __state)
    {
        if (__state is not { Before: { } before } contact) return;
        contact.MedicalActive = false;
        if (contact.World.Characters.Cold(contact.Victim.Slot)?.Race is { } race)
            WeaponBloodState.For(contact.World)?.Deposit(contact.ActualWeapon, race, contact.Damage, contact.WasAlive, before.OrganicBeforeHit == true, before.Mechanism);
        if (contact.Source == CombatHitSource.Melee && !contact.Execution) TryCounterpart(contact, events);
        var limbs = events?.Where(e => e.Kind == MedicalEventKind.PartSevered && (uint)e.Part < (uint)contact.Medical.Parts.Count)
            .Select(e => contact.Medical.Parts[e.Part].Template.LimbSlot).Where(limb => limb is >= 0 and <= 3).Distinct().ToArray() ?? [];
        int rupture = contact.Medical.Decapitated && contact.Medical.Parts[contact.Part].Template.Type == Meitou.Data.Gameplay.Bodies.BodyPartType.Head ? 1
            : events?.Any(e => e.Kind == MedicalEventKind.PartSevered && e.Part == contact.Part) == true ? 0 : -1;
        ImpactEffects.ForWorld(contact.World)?.Capture(contact.World, contact.Hit, before.Mechanism, contact.Execution, rupture);
        DeveloperCombatDiagnostics.CaptureNative(contact.World, contact.Hit, events?.Count ?? 0);
        contact.Gameplay.RecordHitBridge(GoreHitKey.From(contact.Hit), before with
        {
            HeadSeveranceAccepted = contact.HeadGate, SeveredLimbSlots = limbs, CounterpartResolved = true,
        });
        contact.PrimaryFinished = true;
    }
    static Exception? MedicalFinalizer(Exception? __exception, Contact? __state)
    {
        if (__state is not null) __state.MedicalActive = false;
        return __exception;
    }
}
