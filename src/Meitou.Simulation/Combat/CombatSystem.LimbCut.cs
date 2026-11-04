using System.Numerics;
using Meitou.Simulation.Bodies;

namespace Meitou.Simulation.Combat;

public sealed partial class CombatSystem
{
    internal bool DebugLimbCut(World world, CharacterId victimId, CharacterId attackerId, string limbs, int direction = 2, ulong? seed = null, bool prewounded = false)
    {
        var table = world.Characters;
        if (!options.Gore || !table.TryResolveNext(victimId, out int slot)
            || table.Cold(slot) is not { Medical: { Dead: false } medical, Race: not null, Fighter.IsAnimal: false, Stats: not null } victim) return false;
        int[] selected = limbs switch { "left-arm" => [0], "right-arm" => [1], "left-leg" => [2], "right-leg" => [3], "arms" => [0, 1], "legs" => [2, 3], _ => [] };
        if (selected.Length == 0) return false;
        var ctx = MakeContext(world, slot, victim);
        var events = new List<MedicalEvent>();
        int first = -1;
        HitDamage damage = default;
        foreach (int limb in selected)
        {
            int part = -1;
            for (int i = 0; i < medical.Parts.Count; i++) if (medical.Parts[i].Template.LimbSlot == limb) { part = i; break; }
            if (part < 0 || !medical.Parts[part].Template.Severance
                || medical.Parts[part].Limb is not (LimbState.Original or LimbState.Crushed)) continue;
            damage = new(prewounded ? MathF.Min(20, medical.Parts[part].MaxHp * .2f) : MathF.Max(300, medical.Parts[part].MaxHp * 3), 0, 0);
            if (prewounded) medical.Parts[part].Flesh = -medical.Parts[part].MaxHp + damage.Cut * .5f;
            medical.ApplyHit(part, damage, ctx, events);
            if (first < 0) first = part;
        }
        if (first < 0 || !events.Any(e => e.Kind == MedicalEventKind.PartSevered)) return false;
        foreach (var ev in events)
            if (ev.Kind == MedicalEventKind.PartSevered) table.LimbsChanged.Enqueue((slot, medical.Parts[ev.Part].Template.LimbSlot));
        var from = table.TryResolveNext(attackerId, out int attacker) && attacker != slot ? attackerId : CharacterId.None;
        var travel = from.IsNone ? Vector2.UnitX : new Vector2(table.Next[slot].Position.X - table.Next[from.Slot].Position.X,
            table.Next[slot].Position.Z - table.Next[from.Slot].Position.Z);
        Knockdown(world, slot, victim, travel, damage.Cut);
        TaskStart.Stand(table, slot);
        table.Next[slot].Velocity = Vector3.Zero;
        PublishHit(world, CombatHitSource.Melee, from, slot, medical, first, damage, travel, events, direction,
            lowStrike: direction == 9, mechanism: CombatWoundMechanism.Slash, attackerBrutality: 100, limbCutSeed: seed);
        return true;
    }
}
