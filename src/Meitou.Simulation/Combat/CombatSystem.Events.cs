using System.Numerics;
using Meitou.Simulation.Bodies;

namespace Meitou.Simulation.Combat;

public enum CombatEventKind { Hit, PartDown, PartSevered, KnockedOut, Died, BodyBisected }

public enum CombatHitSource { Melee, Bolt, Execution }

/// <summary>One thing a landed hit did, for readers outside the simulation (effects, mods). Published in the hit's order; it reads no roll and changes no state.</summary>
/// <param name="Attacker"><see cref="CharacterId.None"/> when the shooter of a bolt is gone.</param>
/// <param name="Part">Index in the victim's <see cref="MedicalState.Parts"/>: the part struck, or the part the event names; -1 for an event without one.</param>
/// <param name="LimbSlot">The part's limb slot, as <see cref="CharacterTable.LimbsChanged"/>; -1 without a part.</param>
/// <param name="Direction">Horizontal direction the blow travelled, not normalised; with <paramref name="VictimYaw"/> it gives the side struck.</param>
/// <param name="CutDirection">The melee blow's direction after <see cref="HitOutcomes.Facing"/>; -1 for a bolt or an execution.</param>
/// <param name="Mechanism">The visual attack kind, classified before armour; unspecified keeps legacy readers and synthetic events compatible.</param>
public readonly record struct CombatEvent(CombatEventKind Kind, long Tick, CombatHitSource Source, CharacterId Attacker, CharacterId Victim,
    int Part, int LimbSlot, Vector3 Position, Vector2 Direction, HitDamage Damage, float VictimYaw = 0, int CutDirection = -1, bool LowStrike = false, CombatWoundMechanism Mechanism = CombatWoundMechanism.Unspecified, float AttackerBrutality = 0, int WeaponCategory = -1);

public sealed partial class CombatSystem
{
    /// <summary>A sandbox test: this character's melee damage is multiplied by <see cref="TestPower"/>.</summary>
    public static CharacterId TestPowerOf { get; set; } = CharacterId.None;
    public static float TestPower { get; set; } = 1;

    /// <summary>Publishes a hit and its medical events. Serial, like the hits themselves.</summary>
    internal void PublishHit(World world, CombatHitSource source, CharacterId attacker, int victim, MedicalState medical, int part, in HitDamage damage, Vector2 direction, List<MedicalEvent> events, int cutDirection = -1, bool lowStrike = false, CombatWoundMechanism mechanism = CombatWoundMechanism.Unspecified, bool bodyBisected = false, float? attackerBrutality = null, ulong? limbCutSeed = null)
    {
        var owner = world.Characters.Cold(victim);
        var id = world.Characters.IdOf(victim);
        float yaw = world.Characters.Previous[victim].Yaw;
        ulong cutSeed = limbCutSeed ?? Rng.Mix(world.Seed ^ Rng.Key(id) ^ Rng.Key(attacker) ^ (ulong)world.Tick * 0x9E3779B97F4A7C15UL);
        foreach (var ev in events)
            if (options.Gore && ev.Kind == MedicalEventKind.PartSevered && medical.Parts[ev.Part].Template.LimbSlot is >= 0 and <= 3 and var limb)
            {
                if (cutDirection >= 0) Wounds.SeveredLevels.Note(id, limb, cutDirection, lowStrike);
                if (options.AdvancedRagdolls && Wounds.SeveredLevels.Forced is null && owner is not null)
                    Wounds.LimbCutPolicy.Note(owner, limb, cutDirection, lowStrike, direction, yaw, cutSeed);
            }
        if (world.CombatEvents is not { } queue) return;
        float brutality = attackerBrutality ?? (world.Characters.TryResolveNext(attacker, out int sourceSlot)
            ? world.Characters.Cold(sourceSlot)?.Brutality.Level ?? 0 : 0);
        int weaponCategory = world.Characters.TryResolveNext(attacker, out int weaponSlot)
            ? world.Characters.Cold(weaponSlot)?.Fighter?.Weapon?.Data.SkillCategory ?? -1 : -1;
        var position = world.Characters.Previous[victim].Position;
        Enqueue(CombatEventKind.Hit, part, damage);
        foreach (var ev in events)
        {
            switch (ev.Kind)
            {
                case MedicalEventKind.PartDown: Enqueue(CombatEventKind.PartDown, ev.Part, default); break;
                case MedicalEventKind.PartSevered: Enqueue(CombatEventKind.PartSevered, ev.Part, default); break;
                case MedicalEventKind.KnockedOut: Enqueue(CombatEventKind.KnockedOut, -1, default); break;
                case MedicalEventKind.Died: Enqueue(CombatEventKind.Died, -1, default); break;
            }
        }
        if (bodyBisected) Enqueue(CombatEventKind.BodyBisected, part, damage);

        void Enqueue(CombatEventKind kind, int eventPart, in HitDamage d) => queue.Enqueue(new CombatEvent(kind, world.Tick, source, attacker, id, eventPart,
            eventPart >= 0 ? medical.Parts[eventPart].Template.LimbSlot : -1, position, direction, d, yaw, cutDirection, lowStrike, mechanism, brutality, weaponCategory));
    }
}
