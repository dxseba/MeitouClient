using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;

namespace Meitou.Brutality;

public static partial class CombatAdapter
{
    // Same packet, toughness, per-piece native coverage roll and real medical path as the reference follow-through.
    static bool TryCounterpart(Contact contact, List<MedicalEvent>? events)
    {
        var victim = contact.World.Characters.Cold(contact.Victim.Slot);
        var ctx = contact.Context;
        var medical = contact.Medical;
        if (!contact.WasAlive || !ctx.Options.AdvancedRagdolls || ctx.Options.Dismemberment < Dismemberment.Rare || !ctx.HasStats || ctx.ExemptFromWear
            || medical.Dead || !medical.HasStatsObject || victim?.Fighter is not { IsAnimal: false } fighter || events is null
            || (uint)contact.Part >= (uint)medical.Parts.Count || medical.Parts[contact.Part].Template.LimbSlot is not (>= 0 and <= 3 and var limb)
            || !events.Any(e => e.Kind == MedicalEventKind.PartSevered && e.Part == contact.Part)) return false;
        var counterpart = medical.LimbPart(limb ^ 1);
        ulong counter = CombatRolls.BlowCounter(contact.Attacker.Slot, contact.Sequence, contact.Blow, 0);
        float roll = Rng.Float(Rng.Hash(contact.World.Seed, Rng.Key(contact.Victim) ^ 0x504149524544UL, purpose, counter));
        if (counterpart is not { Limb: LimbState.Original or LimbState.Crushed, Template.Severance: true }
            || counterpart.Template.Type != medical.Parts[contact.Part].Template.Type
            || !PairedLimbPolicy.ShouldFollowThrough(contact.Brutality, contact.Before!.Mechanism, contact.Direction, contact.Damage.Cut, counterpart.MaxHp, roll)) return false;
        int other = -1;
        for (int i = 0; i < medical.Parts.Count; i++)
            if (ReferenceEquals(medical.Parts[i], counterpart)) { other = i; break; }
        if (other < 0 || events.Any(e => e.Kind == MedicalEventKind.PartSevered && e.Part == other)) return false;
        var armour = fighter.Armour;
        Span<bool> covering = armour.Length <= 32 ? stackalloc bool[armour.Length] : new bool[armour.Length];
        for (int i = 0; i < armour.Length; i++)
            covering[i] = ArmourStack.Covers(armour[i].Data, counterpart.Template.StringId,
                CombatRolls.Float(contact.World.Seed, Rng.Key(contact.Victim), CombatRoll.Cover,
                    CombatRolls.BlowCounter(contact.Attacker.Slot, contact.Sequence, contact.Blow, i)));
        var effect = ArmourStack.Stack(armour, covering, contact.Packet.ArmourPenetration);
        var damage = HitResolver.Resolve(contact.Packet, effect, contact.Toughness, ctx.Options.GlobalDamageMultiplier, contact.Constants);
        float needed = counterpart.Flesh - counterpart.Stun + counterpart.Rig + counterpart.MaxHp;
        if (!float.IsFinite(needed) || !float.IsFinite(damage.Cut) || damage.Cut <= MathF.Max(0, needed)) return false;
        bool organicBefore = victim.Race is { } beforeRace && !counterpart.IsRobotic(beforeRace);
        contact.ApplyingCounterpart = true;
        try { medical.ApplyHit(other, damage, ctx, events); }
        finally { contact.ApplyingCounterpart = false; }
        if (victim.Race is { } race) WeaponBloodState.For(contact.World)?.Deposit(contact.ActualWeapon, race, damage, true, organicBefore, CombatWoundMechanism.Slash);
        return events.Any(e => e.Kind == MedicalEventKind.PartSevered && e.Part == other);
    }
}
