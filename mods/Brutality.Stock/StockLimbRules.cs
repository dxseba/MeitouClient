using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Brutality.Physics;
using Meitou.Data;
using Meitou.Data.Gameplay.Combat;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;

namespace Meitou.Brutality;

/// <summary>Captures real native limb-loss events and optionally resolves the original rare follow-through on the opposite limb.</summary>
internal static class StockLimbRules
{
    internal readonly record struct Request(CharacterId Owner, long Tick, LimbCutState Shape, Vector2 Travel,
        ulong Seed, string Bone, string[] Records, bool Robot, bool Restoring = false, string? ItemKey = null);

    sealed class State
    {
        public readonly ConcurrentDictionary<CharacterId, Request[]> Cuts = new();
        public bool Previewed;
#if BRUTALITY_STOCK_SAVE_PROBE
        public long NextPreviewWave;
        public int PreviewedVictims;
#endif
    }

    sealed class Contact(World world, CharacterId attacker, CharacterId owner, MedicalState medical,
        int direction, bool low, Vector2 travel, float yaw, int sequence, int blow)
    {
        public readonly World World = world;
        public readonly CharacterId Attacker = attacker, Owner = owner;
        public readonly MedicalState Medical = medical;
        public readonly int Direction = direction, Sequence = sequence, Blow = blow;
        public readonly bool Low = low;
        public readonly Vector2 Travel = travel;
        public readonly float Yaw = yaw;
        public DamagePacket Packet;
        public CombatConstants? Constants;
        public float Toughness;
        public bool Applied, Severed;
    }

    [ThreadStatic] static Contact? current;
    static readonly ConditionalWeakTable<World, State> worlds = new();
    static readonly RngPurpose chancePurpose = (RngPurpose)(uint)(Rng.StableHash("dxseba.brutality") | 0x80000000UL);
    static readonly string? preview = Environment.GetCommandLineArgs().Contains("--sandbox", StringComparer.Ordinal)
        ? Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_STOCK_LIMB_PREVIEW") : null;
    static readonly bool trace = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_TRACE_HITS") == "1";
    static GameDatabase? database;
    internal static bool Enabled { get; set; }

    internal static bool Install(ModContext context)
    {
        try
        {
            database = context.Data;
            var apply = AccessTools.Method(typeof(CombatSystem), nameof(CombatSystem.Apply),
                [typeof(World), typeof(Effect).MakeByRefType()]) ?? throw new MissingMethodException(nameof(CombatSystem.Apply));
            var land = typeof(HitResolver).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(m => m.Name == nameof(HitResolver.LandDetailed) && m.IsGenericMethodDefinition);
            var cover = AccessTools.Inner(typeof(CombatSystem), "CoverRoll") ?? throw new MissingMemberException("CoverRoll");
            var medical = AccessTools.Method(typeof(MedicalState), nameof(MedicalState.ApplyHit),
                [typeof(int), typeof(HitDamage).MakeByRefType(), typeof(MedicalContext).MakeByRefType(), typeof(List<MedicalEvent>)])
                ?? throw new MissingMethodException(nameof(MedicalState.ApplyHit));
            var knockdown = AccessTools.Method(typeof(CombatSystem), "Knockdown",
                [typeof(World), typeof(int), typeof(CharacterCold), typeof(Vector2), typeof(float), typeof(int), typeof(float)])
                ?? throw new MissingMethodException("Knockdown");
            context.Harmony.Patch(apply, prefix: Patch(nameof(ApplyPrefix)), finalizer: Patch(nameof(ApplyFinalizer)));
            context.Harmony.Patch(knockdown, postfix: Patch(nameof(KnockdownPostfix)));
            context.Harmony.Patch(land.MakeGenericMethod(cover), prefix: Patch(nameof(LandPrefix)));
            context.Harmony.Patch(medical, postfix: Patch(nameof(MedicalPostfix)));
            return true;
        }
        catch (Exception error)
        {
            context.Log($"Brutality stock limb rules unavailable: {error.GetBaseException().Message}");
            return false;
        }
    }

    static HarmonyMethod Patch(string name) => new(typeof(StockLimbRules), name);

    static void ApplyPrefix(CombatSystem __instance, World world, in Effect e, CombatSlot[] ____write, out Contact? __state)
    {
        __state = current;
        current = null;
        var table = world.Characters;
        if (!Enabled || e.Kind != CombatSystem.BlowEffect || (uint)e.Source >= (uint)table.HighWater
            || (uint)e.Target >= (uint)table.HighWater || (uint)e.Source >= (uint)____write.Length
            || ____write[e.Source].CancelTick == (int)world.Tick
            || (uint)(int)e.Position.X >= (uint)__instance.Techniques.Count
            || table.Cold(e.Target) is not { Medical: { Dead: false } medical, Fighter.IsAnimal: false }) return;
        var tech = __instance.Techniques[(int)e.Position.X];
        var from = table.Previous[e.Source].Position;
        var target = table.Previous[e.Target];
        int direction = HitOutcomes.Facing(tech.DirectionOf(e.Arg), HitOutcomes.Behind(from, target.Position, target.Yaw));
        var travel = new Vector2(target.Position.X - from.X, target.Position.Z - from.Z);
        current = new(world, table.IdOf(e.Source), table.IdOf(e.Target), medical, direction, tech.LowStrike,
            travel, target.Yaw, (int)e.Amount, e.Arg);
    }

    static Exception? ApplyFinalizer(Exception? __exception, Contact? __state)
    {
        current = __state;
        return __exception;
    }

    static void KnockdownPostfix(World world, int victim, CharacterCold vc)
    {
        if (current is not { Severed: true } contact || !ReferenceEquals(world, contact.World)
            || victim != contact.Owner.Slot || !ReferenceEquals(vc.Medical, contact.Medical) || vc.Blow is not { } raw) return;

        // Knockdown just assigned the shove for this actual severance. Avoid lifting the victim
        // or concentrating the blow on the lost limb's bone as the body begins its fall.
        float response = RobotRagdollWeight.Response(vc.Race?.IsRobot == true);
        var motion = StockTorsoLaunch.Clamp(raw.Velocity - raw.Push, 8);
        var push = StockTorsoLaunch.Clamp(new(raw.Push.X, 0, raw.Push.Z), 8 * response);
        vc.Blow = new(motion + push, push, null);
        vc.Knockdown = motion + push;
        if (trace) Console.WriteLine(FormattableString.Invariant($"Brutality stock limb body launch owner={contact.Owner} nativePush={raw.Push.Length():F1}->{push.Length():F1} bodySpeed={vc.Knockdown.Length():F1}"));
    }

    static void LandPrefix(MedicalState medical, in DamagePacket packet, float toughness, CombatConstants c)
    {
        if (current is not { Applied: false } contact || !ReferenceEquals(contact.Medical, medical)) return;
        contact.Packet = packet;
        contact.Toughness = toughness;
        contact.Constants = c;
    }

    static void MedicalPostfix(MedicalState __instance, int part, in HitDamage hit, in MedicalContext ctx, List<MedicalEvent>? events)
    {
        if (current is not { Applied: false, Constants: { } constants } contact || !ReferenceEquals(contact.Medical, __instance)) return;
        contact.Applied = true;
        if (!ctx.Options.AdvancedRagdolls || events is null || (uint)part >= (uint)__instance.Parts.Count
            || __instance.Parts[part].Template.LimbSlot is not (>= 0 and <= 3 and var slot)
            || !events.Any(e => e.Kind == MedicalEventKind.PartSevered && e.Part == part)) return;
        contact.Severed = true;
        var table = contact.World.Characters;
        if (table.Cold(contact.Owner.Slot) is not { Race: { } race, Fighter: { IsAnimal: false } fighter }) return;
        ulong seed = Rng.Mix(contact.World.Seed ^ Rng.Key(contact.Owner) ^ Rng.Key(contact.Attacker)
            ^ (ulong)contact.World.Tick * 0x9E3779B97F4A7C15UL);
        var primary = LimbCutPolicy.FromHit(slot, contact.Direction, contact.Low, contact.Travel, contact.Yaw, seed);
        Publish(contact.World, MakeRequest(contact.Owner, contact.World.Tick, primary, contact.Travel, seed, ctx, __instance.Parts[part]));

        // Same pre-armour packet, coverage roll and medical event buffer as the primary blow.
        if (__instance.Dead || ctx.Options.Dismemberment < Dismemberment.Rare || !ctx.HasStats || ctx.ExemptFromWear
            || !__instance.HasStatsObject || hit.Cut < PairedLimbPolicy.MinimumCut
            || contact.Direction is not (2 or 3 or 9)
            || table.Cold(contact.Attacker.Slot)?.Fighter is not { IsAnimal: false } source
            || __instance.LimbPart(slot ^ 1) is not { Limb: LimbState.Original or LimbState.Crushed, Template.Severance: true } other
            || other.Template.Type != __instance.Parts[part].Template.Type) return;
        float level = StockProgression.For(contact.World)?.LevelFor(contact.World, contact.Attacker) ?? 0;
        ulong counter = CombatRolls.BlowCounter(contact.Attacker.Slot, contact.Sequence, contact.Blow, 0);
        float roll = Rng.Float(Rng.Hash(contact.World.Seed, Rng.Key(contact.Owner) ^ 0x504149524544UL, chancePurpose, counter));
        var mechanism = CombatWoundMechanisms.Classify(source, contact.Packet, contact.Direction);
        if (!PairedLimbPolicy.ShouldFollowThrough(level, mechanism, contact.Direction, hit.Cut, other.MaxHp, roll)) return;
        int index = -1;
        for (int i = 0; i < __instance.Parts.Count; i++)
            if (ReferenceEquals(__instance.Parts[i], other)) { index = i; break; }
        if (index < 0) return;
        var armour = fighter.Armour;
        Span<bool> covering = armour.Length <= 32 ? stackalloc bool[armour.Length] : new bool[armour.Length];
        for (int i = 0; i < armour.Length; i++)
            covering[i] = ArmourStack.Covers(armour[i].Data, other.Template.StringId,
                CombatRolls.Float(contact.World.Seed, Rng.Key(contact.Owner), CombatRoll.Cover,
                    CombatRolls.BlowCounter(contact.Attacker.Slot, contact.Sequence, contact.Blow, i)));
        var protection = ArmourStack.Stack(armour, covering, contact.Packet.ArmourPenetration);
        var damage = HitResolver.Resolve(contact.Packet, protection, contact.Toughness, ctx.Options.GlobalDamageMultiplier, constants);
        float needed = other.Flesh - other.Stun + other.Rig + other.MaxHp;
        if (!float.IsFinite(needed) || !float.IsFinite(damage.Cut) || damage.Cut <= MathF.Max(0, needed)) return;
        __instance.ApplyHit(index, damage, ctx, events);
        if (!events.Any(e => e.Kind == MedicalEventKind.PartSevered && e.Part == index)) return;
        var paired = LimbCutPolicy.FromHit(slot ^ 1, contact.Direction, contact.Low, contact.Travel, contact.Yaw, seed);
        Publish(contact.World, MakeRequest(contact.Owner, contact.World.Tick, paired, contact.Travel, seed, ctx, other));
        if (trace) Console.WriteLine($"Brutality stock paired limb victim={contact.Owner} slots={slot},{slot ^ 1} tick={contact.World.Tick}");
    }

    static Request MakeRequest(CharacterId owner, long tick, LimbCutState shape, Vector2 travel, ulong seed,
        in MedicalContext ctx, HealthPart part) =>
        new(owner, tick, shape, travel, seed, part.Template.BoneName2,
            ctx.Race.SeveredLimbs.Where(limb => limb.Slot == shape.LimbSlot && database?.Find(limb.TargetStringId) is not null)
                .Select(limb => limb.TargetStringId).ToArray(), ctx.Race.IsRobot);

    static void Publish(World world, Request request)
    {
        if (!request.Shape.IsValid) return;
        var cuts = worlds.GetOrCreateValue(world).Cuts;
        if (!cuts.TryGetValue(request.Owner, out var prior)) prior = [];
        cuts[request.Owner] = [.. prior.Where(cut => cut.Shape.LimbSlot != request.Shape.LimbSlot).Append(request).OrderBy(cut => cut.Shape.LimbSlot)];
    }

    internal static bool HasCuts(World world) => worlds.TryGetValue(world, out var state) && !state.Cuts.IsEmpty;

    internal static Request[]? For(World world, CharacterId owner) =>
        worlds.TryGetValue(world, out var state) && state.Cuts.TryGetValue(owner, out var found) ? found : null;

    internal static bool Restore(World world, Request request)
    {
        if (For(world, request.Owner)?.Any(cut => cut.Shape.LimbSlot == request.Shape.LimbSlot) == true) return false;
        Publish(world, request);
        return true;
    }

    internal static bool QueuePreview(World world, CharacterId owner, int slot, CharacterCold actor, int limb)
    {
        if (!Enabled || actor.Race is not { } race || actor.Medical?.LimbPart(limb) is not
            { Template.Severance: true, Limb: LimbState.Original or LimbState.Crushed } part) return false;
        var travel = Vector2.UnitX;
        ulong seed = Rng.Mix(Rng.Key(owner) ^ world.Seed);
        long tick = world.Tick + 1;
        var shape = LimbCutPolicy.FromHit(limb, 2, false, travel, world.Characters.Next[slot].Yaw, seed);
        string[] records = race.SeveredLimbs.Where(item => item.Slot == limb && database?.Find(item.TargetStringId) is not null)
            .Select(item => item.TargetStringId).ToArray();
        if (!shape.IsValid || records.Length == 0) return false;
        Publish(world, new(owner, tick, shape, travel, seed, part.Template.BoneName2, records, race.IsRobot));
        float damage = MathF.Max(1, part.Flesh - part.Stun + part.Rig + part.MaxHp + 1);
        world.Commands.Enqueue(new DebugDamage([owner], damage, part.Template.StringId) { Tick = tick });
        return true;
    }

    internal static void Step(World world)
    {
        StockLimbSave.Step(world);
        worlds.TryGetValue(world, out var state);
        if (state is not null)
            foreach (var (id, cuts) in state.Cuts)
            {
                if (!world.Characters.TryResolveNext(id, out int slot) || world.Characters.Cold(slot)?.Medical is not { } medical)
                    state.Cuts.TryRemove(id, out _);
                else if (cuts.Any(cut => cut.Tick < world.Tick && medical.LimbPart(cut.Shape.LimbSlot)?.Limb != LimbState.Stump))
                {
                    var retained = cuts.Where(cut => cut.Tick >= world.Tick || medical.LimbPart(cut.Shape.LimbSlot)?.Limb == LimbState.Stump).ToArray();
                    if (retained.Length == 0) state.Cuts.TryRemove(id, out _);
                    else state.Cuts[id] = retained;
                }
            }
        if (preview is null || world.Tick < 90 || state is { Previewed: true }) return;
        bool playerPreview = false;
        string mode = preview;
#if BRUTALITY_STOCK_SAVE_PROBE
        bool sustained = mode == "sustained-limbs";
        if (sustained)
        {
            state ??= worlds.GetOrCreateValue(world);
            if (world.Tick < state.NextPreviewWave) return;
            mode = "left-arm";
        }
        int wave = 0;
#endif
#if BRUTALITY_STOCK_SAVE_PROBE
        playerPreview = mode == "player-arms";
        if (playerPreview) mode = "arms";
#endif
        int[] slots = mode switch
        {
            "left-arm" => [0], "right-arm" => [1], "left-leg" => [2], "right-leg" => [3],
            "arms" => [0, 1], "legs" => [2, 3], _ => [],
        };
        if (slots.Length == 0) return;
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not
                { Race: { } race, Fighter.IsAnimal: false, Medical: { Dead: false } medical } actor
                || actor.IsPlayer != playerPreview || !slots.All(limb => medical.LimbPart(limb) is { Template.Severance: true, Limb: LimbState.Original })) continue;
            var owner = table.IdOf(slot);
            var travel = Vector2.UnitX;
            ulong seed = Rng.Mix(Rng.Key(owner) ^ world.Seed);
            long tick = world.Tick + 1;
            foreach (int limb in slots)
            {
                var part = medical.LimbPart(limb)!;
                var shape = LimbCutPolicy.FromHit(limb, 2, false, travel, table.Next[slot].Yaw, seed);
                string[] records = race.SeveredLimbs.Where(item => item.Slot == limb && database?.Find(item.TargetStringId) is not null)
                    .Select(item => item.TargetStringId).ToArray();
                Publish(world, new(owner, tick, shape, travel, seed, part.Template.BoneName2, records, race.IsRobot));
                // Exceed the native destruction threshold by one point. A 5000-point preview hit
                // can kill through immediate blood loss before the second native command runs.
                float damage = MathF.Max(1, part.Flesh - part.Stun + part.Rig + part.MaxHp + 1);
                world.Commands.Enqueue(new DebugDamage([owner], damage, part.Template.StringId) { Tick = tick });
                if (trace) Console.WriteLine(FormattableString.Invariant($"Brutality stock limb preview hit slot={limb} cut={damage:F1} tick={tick}"));
            }
            var previews = worlds.GetOrCreateValue(world);
#if BRUTALITY_STOCK_SAVE_PROBE
            if (sustained)
            {
                previews.PreviewedVictims++;
                previews.Previewed = previews.PreviewedVictims >= 20;
                previews.NextPreviewWave = world.Tick + 100;
            }
            else
#endif
                previews.Previewed = true;
            Console.WriteLine($"Brutality stock limb preview mode={preview} target={owner} tick={tick}");
#if BRUTALITY_STOCK_SAVE_PROBE
            if (sustained && !previews.Previewed && ++wave < 4) continue;
#endif
            return;
        }
    }
}
