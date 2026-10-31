using System.Numerics;
using Meitou.Simulation.Bodies;

namespace Meitou.Simulation.Combat;

public enum CombatEventKind { Hit, PartDown, PartSevered, KnockedOut, Died }

public enum CombatHitSource { Melee, Bolt, Execution }

/// <summary>One thing a landed hit did, for readers outside the simulation (effects, mods). Published in the hit's order; it reads no roll and changes no state.</summary>
/// <param name="Attacker"><see cref="CharacterId.None"/> when the shooter of a bolt is gone.</param>
/// <param name="Part">Index in the victim's <see cref="MedicalState.Parts"/>: the part struck, or the part the event names; -1 for an event without one.</param>
/// <param name="LimbSlot">The part's limb slot, as <see cref="CharacterTable.LimbsChanged"/>; -1 without a part.</param>
/// <param name="Direction">Horizontal direction the blow travelled, not normalised; with <paramref name="VictimYaw"/> it gives the side struck.</param>
/// <param name="CutDirection">The melee blow's direction after <see cref="HitOutcomes.Facing"/>; -1 for a bolt or an execution.</param>
public readonly record struct CombatEvent(CombatEventKind Kind, long Tick, CombatHitSource Source, CharacterId Attacker, CharacterId Victim,
    int Part, int LimbSlot, Vector3 Position, Vector2 Direction, HitDamage Damage, float VictimYaw = 0, int CutDirection = -1, bool LowStrike = false);

public sealed partial class CombatSystem
{
    /// <summary>Publishes a hit and its medical events. Serial, like the hits themselves.</summary>
    internal void PublishHit(World world, CombatHitSource source, CharacterId attacker, int victim, MedicalState medical, int part, in HitDamage damage, Vector2 direction, List<MedicalEvent> events, int cutDirection = -1, bool lowStrike = false)
    {
        foreach (var ev in events)
            if (ev.Kind == MedicalEventKind.PartSevered && medical.Parts[ev.Part].Template.LimbSlot is >= 0 and <= 3 and var limb && cutDirection >= 0)
                Wounds.SeveredLevels.Note(world.Characters.IdOf(victim), limb, cutDirection, lowStrike);
        if (world.CombatEvents is not { } queue) return;
        var id = world.Characters.IdOf(victim);
        var position = world.Characters.Previous[victim].Position;
        float yaw = world.Characters.Previous[victim].Yaw;
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

        void Enqueue(CombatEventKind kind, int eventPart, in HitDamage d) => queue.Enqueue(new CombatEvent(kind, world.Tick, source, attacker, id, eventPart,
            eventPart >= 0 ? medical.Parts[eventPart].Template.LimbSlot : -1, position, direction, d, yaw, cutDirection, lowStrike));
    }
}
