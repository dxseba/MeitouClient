using Meitou.Simulation.Bodies;

namespace Meitou.Simulation.Combat;

public sealed partial class CombatSystem
{
    /// <summary>One swing may reach the matching opposite limb. Its own armour must still permit a real severing cut.</summary>
    internal bool TrySeverCounterpart<TCover>(CharacterCold victim, int part, in DamagePacket packet, in HitDamage primary,
        in MedicalContext ctx, TCover cover, float toughness, bool aliveBeforeHit, float brutality,
        int direction, CombatWoundMechanism mechanism, float roll, List<MedicalEvent> events) where TCover : struct, ICoverRoll
    {
        if (!aliveBeforeHit || !ctx.Options.AdvancedRagdolls || ctx.Options.Dismemberment < Dismemberment.Rare || !ctx.HasStats || ctx.ExemptFromWear
            || victim is not { Medical: { Dead: false, HasStatsObject: true } medical, Fighter: { IsAnimal: false } fighter }
            || (uint)part >= (uint)medical.Parts.Count || medical.Parts[part].Template.LimbSlot is not (>= 0 and <= 3 and var slot)
            || !events.Any(e => e.Kind == MedicalEventKind.PartSevered && e.Part == part)) return false;
        var counterpart = medical.LimbPart(slot ^ 1);
        if (counterpart is not { Limb: LimbState.Original or LimbState.Crushed, Template.Severance: true }
            || counterpart.Template.Type != medical.Parts[part].Template.Type
            || !PairedLimbPolicy.ShouldFollowThrough(brutality, mechanism, direction, primary.Cut, counterpart.MaxHp, roll)) return false;
        int other = -1;
        for (int i = 0; i < medical.Parts.Count; i++)
            if (ReferenceEquals(medical.Parts[i], counterpart)) { other = i; break; }
        if (other < 0 || events.Any(e => e.Kind == MedicalEventKind.PartSevered && e.Part == other)) return false;

        var armour = fighter.Armour;
        Span<bool> covering = armour.Length <= 32 ? stackalloc bool[armour.Length] : new bool[armour.Length];
        for (int i = 0; i < armour.Length; i++) covering[i] = ArmourStack.Covers(armour[i].Data, counterpart.Template.StringId, cover.Roll(i));
        var effect = ArmourStack.Stack(armour, covering, packet.ArmourPenetration);
        var damage = HitResolver.Resolve(packet, effect, toughness, ctx.Options.GlobalDamageMultiplier, combat);
        float needed = counterpart.Flesh - counterpart.Stun + counterpart.Rig + counterpart.MaxHp;
        // Blunt/stun alone cannot extend the blade through a second limb. Failed armour/health checks leave it untouched.
        if (!float.IsFinite(needed) || !float.IsFinite(damage.Cut) || damage.Cut <= MathF.Max(0, needed)) return false;
        medical.ApplyHit(other, damage, ctx, events);
        return events.Any(e => e.Kind == MedicalEventKind.PartSevered && e.Part == other);
    }
}
