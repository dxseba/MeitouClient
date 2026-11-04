using System.Numerics;

namespace Meitou.Simulation.Combat;

public sealed partial class CombatSystem
{
    /// <summary>The push a downing blow gives the ragdoll (world units/s): this much per point of damage, within these bounds, and the lift with it (engine choice).</summary>
    const float PushPerDamage = 0.4f, PushMin = 10, PushMax = 50, PushLift = 0.2f;

    /// <summary>
    /// Advanced ragdolls: a hit on an existing unconscious body gives its struck part one physical impulse; a new knockout or death leaves <see cref="CharacterCold.Blow"/>, its initial speed:
    /// the victim's own motion of the last tick plus a push along the blow's <paramref name="direction"/> (X/Z) sized by the <paramref name="damage"/>.
    /// </summary>
    void HitRagdoll(World world, int victim, CharacterCold vc, int part, Vector2 direction, float damage, bool wasIncapacitated, bool downed, Vector3? sourcePoint = null)
    {
        if (!options.Gore)
        {
            if (downed) Knockdown(world, victim, vc, direction, damage);
            return;
        }
        if (!wasIncapacitated && !downed) return;
        if (wasIncapacitated && damage > 0 && (uint)part < (uint)vc.Medical!.Parts.Count && options.AdvancedRagdolls
            && world.SystemOf<Ragdolls.RagdollSystem>() is { } ragdolls)
        {
            var id = world.Characters.IdOf(victim);
            var template = vc.Medical.Parts[part].Template;
            if (sourcePoint is { } from && ragdolls.TryPartPosition(id, template, out var site))
                direction = new(site.X - from.X, site.Z - from.Z);
            if (ragdolls.QueueHit(world, id, template, HitPush(direction, damage))) return;
        }
        Knockdown(world, victim, vc, direction, damage);
    }

    static Vector3 HitPush(Vector2 direction, float damage)
    {
        if (!float.IsFinite(damage) || !float.IsFinite(direction.LengthSquared()) || direction.LengthSquared() <= 1e-6f) return Vector3.Zero;
        direction = Vector2.Normalize(direction);
        return new Vector3(direction.X, PushLift, direction.Y) * Math.Clamp(damage * PushPerDamage, PushMin, PushMax);
    }

    void Knockdown(World world, int victim, CharacterCold vc, Vector2 direction, float damage)
    {
        if (vc.Medical is not { Incapacitated: true }) return;
        if (!options.AdvancedRagdolls) { vc.Knockdown = Vector3.Zero; return; }
        var motion = world.Characters.Previous[victim].Velocity;
        var push = HitPush(direction, damage);
        vc.Blow = (motion + push, push);
        vc.Knockdown = motion + push;
    }
}
