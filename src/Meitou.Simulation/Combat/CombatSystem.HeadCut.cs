using System.Numerics;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Wounds;

namespace Meitou.Simulation.Combat;

public sealed partial class CombatSystem
{
    /// <summary>A forced visual fixture: one normal lethal head hit, without training or changing the attacker's actual skill.</summary>
    internal bool DebugHeadCut(World world, CharacterId victimId, CharacterId attackerId, string style, int direction = 1, ulong? visualSeed = null)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(victimId, out int slot)
            || table.Cold(slot) is not { Medical: { Dead: false } medical, Race: not null, Fighter: { IsAnimal: false }, Stats: not null } victim
            || victim.HeadCut is not null) return false;
        int head = -1;
        for (int i = 0; i < medical.Parts.Count; i++)
            if (medical.Parts[i].Template.Type == BodyPartType.Head) { head = i; break; }
        if (head < 0) return false;

        var source = table.TryResolveNext(attackerId, out int attacker) && attacker != slot ? attackerId : CharacterId.None;
        var position = table.Next[slot].Position;
        float yaw = table.Next[slot].Yaw;
        var travel = source.IsNone ? -new Vector2(MathF.Sin(yaw), MathF.Cos(yaw))
            : new Vector2(position.X - table.Next[source.Slot].Position.X, position.Z - table.Next[source.Slot].Position.Z);
        ulong seed = visualSeed ?? HeadCutPolicy.VisualSeedFor(world.Seed, victimId, source, (ulong)world.Tick);
        if (HeadCutPolicy.Preview(style, direction, seed, travel, yaw) is not { IsValid: true } cut) return false;
        var ctx = MakeContext(world, slot, victim) with
        {
            AllowDecapitation = cut.Mode != HeadCutMode.AttachedSplit,
            HeadCutCandidate = cut,
        };
        var struck = medical.Parts[head];
        if (!ctx.Options.Decapitation || ctx.Options.Dismemberment < Dismemberment.Rare || !ctx.HasStats || !medical.HasStatsObject
            || !struck.Template.Severance || struck.Limb is not (LimbState.Original or LimbState.Crushed)) return false;

        var events = new List<MedicalEvent>();
        var damage = new HitDamage(MathF.Max(300, struck.MaxHp * 3), 0, 0);
        medical.ApplyHit(head, damage, ctx, events);
        if (!events.Any(e => e.Kind == MedicalEventKind.HeadCut)) return false;
        victim.HeadCut = cut;
        if (medical.Decapitated) table.LimbsChanged.Enqueue((slot, -1));
        Knockdown(world, slot, victim, travel, damage.Cut);
        TaskStart.Stand(table, slot);
        table.Next[slot].Velocity = Vector3.Zero;
        PublishHit(world, CombatHitSource.Melee, source, slot, medical, head, damage, travel, events, direction,
            mechanism: CombatWoundMechanism.Slash, attackerBrutality: 100);
        return true;
    }
}
