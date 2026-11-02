using System.Numerics;
using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Wounds;

namespace Meitou.Simulation.Combat;

public sealed partial class CombatSystem
{
    /// <summary>A deterministic debug command: ordinary delivered damage once, the shared bisection policy with roll zero, and normal hit/death publication.</summary>
    internal bool DebugBisect(World world, CharacterId victimId, CharacterId attackerId, ulong? visualSeed = null, int cutDirection = 2,
        TorsoBisectionStyle style = TorsoBisectionStyle.Auto)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(victimId, out int victimSlot)
            || table.Cold(victimSlot) is not { Medical: { Dead: false } medical, Race: { } race, Fighter: { } fighter, Stats: not null } victim
            || victim.TorsoBisection is not null) return false;
        int chest = -1;
        for (int i = 0; i < medical.Parts.Count; i++)
            if (medical.Parts[i].Template.StringId == ArmourData.ChestPartId) { chest = i; break; }
        if (chest < 0) return false;

        bool LivingSource(int slot) => slot != victimSlot && (uint)slot < (uint)table.HighWater
            && table.Next[slot].Alive && table.Cold(slot)?.Medical is { Dead: false };
        int attackerSlot = table.TryResolveNext(attackerId, out int requested) && LivingSource(requested) ? requested : -1;
        if (attackerSlot < 0)
            foreach (var selected in world.Player.Selection)
                if (table.TryResolveNext(selected, out int slot) && LivingSource(slot)) { attackerSlot = slot; break; }
        var position = table.Next[victimSlot].Position;
        if (attackerSlot < 0)
        {
            float nearest = float.MaxValue;
            for (int i = 0; i < table.HighWater; i++)
                if (LivingSource(i) && Vector3.DistanceSquared(position, table.Next[i].Position) is var distance && distance < nearest)
                    (attackerSlot, nearest) = (i, distance);
        }
        float yaw = table.Next[victimSlot].Yaw;
        var travel = attackerSlot >= 0 ? new Vector2(position.X - table.Next[attackerSlot].Position.X, position.Z - table.Next[attackerSlot].Position.Z)
            : -new Vector2(MathF.Sin(yaw), MathF.Cos(yaw));
        if (!float.IsFinite(travel.X) || !float.IsFinite(travel.Y) || travel.LengthSquared() < 1e-8f)
            travel = -new Vector2(MathF.Sin(yaw), MathF.Cos(yaw));
        // Lengthwise previews need a downward blow; an oblique torso cut retains the requested side/down axis.
        int techniqueDirection = style is TorsoBisectionStyle.Vertical or TorsoBisectionStyle.Diagonal ? 1 : cutDirection;
        int direction = attackerSlot >= 0 ? HitOutcomes.Facing(techniqueDirection, HitOutcomes.Behind(table.Next[attackerSlot].Position, position, yaw)) : techniqueDirection;
        float cut = MathF.Max(200, medical.Parts[chest].MaxHp * 3);
        var input = new TorsoBisectionInput(CombatHitSource.Melee, CombatWoundMechanism.Slash, medical.Parts[chest].Template,
            direction, cut, medical.Parts[chest].MaxHp, Robot: race.IsRobot, Animal: fighter.IsAnimal) { Brutality = 100, Lethal = true };
        if (!TorsoBisectionPolicy.SupportsStyle(direction, style) || !TorsoBisectionPolicy.ShouldBisect(input, 0)) return false;
        var events = new List<MedicalEvent>();
        var damage = new HitDamage(cut, 0, 0);
        var sourceFighter = attackerSlot >= 0 ? table.Cold(attackerSlot)?.Fighter : null;
        var strikingItem = sourceFighter is { IsAnimal: false, Unarmed: false } ? sourceFighter.Weapon?.SourceItem : null;
        bool organicBeforeHit = !medical.Parts[chest].IsRobotic(race);
        medical.ApplyHit(chest, damage, MakeContext(world, victimSlot, victim), events);
        DepositWeaponBlood(strikingItem, race, damage, true, organicBeforeHit, CombatWoundMechanism.Slash);
        var source = attackerSlot >= 0 ? table.IdOf(attackerSlot) : CharacterId.None;
        ulong variation = visualSeed ?? TorsoBisectionPolicy.VisualSeedFor(world.Seed, victimId, source, (ulong)world.Tick);
        if (!TryBisect(victim, chest, damage, CombatWoundMechanism.Slash, true, 0, direction, events, variation, travel, yaw, style, brutality: 100)) return false;
        Knockdown(world, victimSlot, victim, travel, cut);
        TaskStart.Stand(table, victimSlot);
        table.Next[victimSlot].Velocity = Vector3.Zero;
        PublishHit(world, CombatHitSource.Melee, source,
            victimSlot, medical, chest, damage, travel, events, direction, mechanism: CombatWoundMechanism.Slash, bodyBisected: true, attackerBrutality: 100);
        return true;
    }

    /// <summary>Commits a qualifying melee cleave after its ordinary damage; an existing cut and a pre-hit corpse are never cut again.</summary>
    internal static bool TryBisect(CharacterCold victim, int part, in HitDamage damage, CombatWoundMechanism mechanism,
        bool aliveBeforeHit, float roll, int cutDirection, List<MedicalEvent> events, ulong visualSeed = 0, Vector2 travel = default, float victimYaw = 0,
        TorsoBisectionStyle style = TorsoBisectionStyle.Auto, float brutality = 0)
    {
        if (victim is not { Medical: { } medical, Race: { } race, Fighter: { } fighter }
            || (uint)part >= (uint)medical.Parts.Count) return false;
        var struck = medical.Parts[part];
        var input = new TorsoBisectionInput(CombatHitSource.Melee, mechanism, struck.Template, cutDirection, damage.Cut,
            struck.MaxHp, aliveBeforeHit, victim.TorsoBisection is not null, race.IsRobot, fighter.IsAnimal)
            { Brutality = brutality, Lethal = medical.Dead };
        if (!TorsoBisectionPolicy.ShouldBisect(input, roll)) return false;
        var state = TorsoBisectionPolicy.StateForDirection(cutDirection, visualSeed, travel, victimYaw, style);
        if (!state.IsValid) return false;
        victim.TorsoBisection = state;
        medical.KillFromBisection(events);
        return true;
    }
}
