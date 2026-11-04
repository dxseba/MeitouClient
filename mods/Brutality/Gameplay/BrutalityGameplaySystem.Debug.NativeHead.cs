using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;

namespace Meitou.Brutality;

public sealed partial class BrutalityGameplaySystem
{
    static readonly MethodInfo nativePreviewContext = AccessTools.Method(typeof(CombatSystem), "MakeContext");
    static readonly MethodInfo nativePreviewKnockdown = AccessTools.Method(typeof(CombatSystem), "Knockdown");
    static readonly MethodInfo nativePreviewObserve = AccessTools.Method(typeof(World), "ObserveCombatHit");

    /// <summary>Opt-in developer reference fixture, matching DebugHeadCut at pinned source 3a15eeb9.
    /// Domain32 belongs only to that reference recipe; normal mod combat keeps its own named RNG domains.</summary>
    internal bool ApplyMatchedNativeHeadCut(World world, CharacterId target, CharacterId attacker, int variant = 1,
        int direction = 1, ulong? visualSeed = null)
    {
        var table = world.Characters;
        if (combat is null || !table.TryResolveNext(target, out int slot)
            || table.Cold(slot) is not { Medical: { Dead: false } medical, Race: { } race, Fighter: { IsAnimal: false }, Stats: not null } cold
            || Find(world, target) is not { Head: null } actor) return false;
        int part = medical.Parts.ToList().FindIndex(p => p.Template.Type == BodyPartType.Head);
        if (part < 0) return false;
        var source = table.TryResolveNext(attacker, out int sourceSlot) && sourceSlot != slot ? attacker : CharacterId.None;
        var position = table.Next[slot].Position; float yaw = table.Next[slot].Yaw;
        var travel = source.IsNone ? -new Vector2(MathF.Sin(yaw), MathF.Cos(yaw))
            : new Vector2(position.X - table.Next[source.Slot].Position.X, position.Z - table.Next[source.Slot].Position.Z);
        ulong seed = visualSeed ?? Rng.Hash(world.Seed, Rng.Key(target) ^ Rng.Mix(Rng.Key(source)), (RngPurpose)32, (ulong)world.Tick);
        string style = (variant % 3) switch { 0 => "oblique", 1 => "split", _ => "neck" };
        if (HeadCutPolicy.Preview(style, direction, seed, travel, yaw) is not { IsValid: true } cut) return false;
        var context = (MedicalContext)nativePreviewContext.Invoke(combat, [world, slot, cold])!;
        var struck = medical.Parts[part];
        if (!context.Options.Decapitation || context.Options.Dismemberment < Dismemberment.Rare || !context.HasStats || !medical.HasStatsObject
            || !struck.Template.Severance || struck.Limb is not (LimbState.Original or LimbState.Crushed)) return false;
        float maximum = struck.MaxHp; bool wasIncapacitated = medical.Incapacitated;
        var damage = new HitDamage(MathF.Max(300, maximum * 3), 0, 0);
        var packet = new DamagePacket(damage.Cut, 0, 0, 0, 1, 0);
        var before = CaptureBeforeHit(world, source, target, part, packet, CombatWoundMechanism.Slash, context) with
        {
            ChallengeFactor = 0, HeadSeveranceAccepted = true, CounterpartResolved = true,
            WeaponCategory = !source.IsNone ? table.Cold(source.Slot)?.Fighter?.Weapon?.Data.SkillCategory ?? -1 : -1,
        };
        if (cut.Mode == HeadCutMode.AttachedSplit)
        {
            // API1 lacks the pin's per-hit AllowDecapitation field. Copy this fixture's options only;
            // ordinary world options stay untouched while a still-attached head uses the same vital damage.
            context = new(context.Constants, context.Options with { Decapitation = false }, context.Race)
            { Toughness = context.Toughness, Strength = context.Strength, EncumbranceFactor = context.EncumbranceFactor,
                Seed = context.Seed, CharacterKey = context.CharacterKey };
        }
        var events = new List<MedicalEvent>(); medical.ApplyHit(part, damage, context, events);
        if (!medical.Dead) return false;
        actor.Head = cut;
        if (medical.Decapitated) table.LimbsChanged.Enqueue((slot, -1));
        nativePreviewKnockdown.Invoke(combat, [world, slot, cold, travel, damage.Cut]);
        TaskStart.Stand(table, slot); table.Next[slot].Velocity = Vector3.Zero;
        var hit = new CombatHit(world.Tick, CombatHitSource.Melee, source, target, 0, 0, part, damage, default,
            table.Previous[slot].Position, new(travel.X, 0, travel.Y), table.Previous[slot].Yaw, direction, false, true, true, wasIncapacitated);
        Add(hit, GoreCutKind.Head, head: cut);
        RecordHitBridge(GoreHitKey.From(hit), before);
        ImpactEffects.ForWorld(world)?.Capture(world, hit, CombatWoundMechanism.Slash, false, medical.Decapitated ? 1 : -1);
        nativePreviewObserve.Invoke(world, [hit]);
        // Pin PublishHit supplies event brutality100 without training the attacker's actual skill.
        // Preserve observer order and all other queued contacts; replace only this one debug event's override.
        int queued = pending.Count;
        for (int i = 0; i < queued; i++)
        {
            var work = pending.Dequeue();
            pending.Enqueue(GoreHitKey.From(work.Hit) == GoreHitKey.From(hit) ? work with { Brutality = 100 } : work);
        }
        changed = true;
        Console.WriteLine($"Brutality native reference hit: tick={world.Tick} style={style} seed={seed} part={part} cut={damage.Cut} travel={travel} yaw={yaw} plane={cut.Plane} blow={cold.Blow}");
        return true;
    }
}
