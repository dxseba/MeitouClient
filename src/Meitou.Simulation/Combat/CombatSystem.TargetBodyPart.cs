using System.Numerics;
using Meitou.Simulation.Bodies;

namespace Meitou.Simulation.Combat;

public sealed partial class CombatSystem
{
    sealed record AttackFocus(bool AllowIncapacitated, string? TargetBodyPart);

    static int BodyPartIndex(CharacterCold victim, string id)
    {
        if (victim.Medical is not { } medical) return -1;
        for (int i = 0; i < medical.Parts.Count; i++)
            if (medical.Parts[i] is { Limb: not LimbState.Stump } part && part.Template.StringId == id) return i;
        return -1;
    }

    bool ValidBodyPartTarget(World world, int attacker, int target, bool picked, bool allow, string? part) => part is null
        || AllowsIncapacitatedTarget(world, attacker, target, picked, allow)
        && world.Characters.Cold(target) is { } victim && HelplessTarget(victim, world.Tick) && BodyPartIndex(victim, part) >= 0;

    bool ValidBodyPartTarget(World world, int attacker, in CombatSlot state) =>
        ValidBodyPartTarget(world, attacker, state.TargetSlot, state.Picked, state.AllowIncapacitated, state.TargetBodyPart);

    internal Vector3 TargetPoint(World world, in CombatSlot state, Vector3 fallback)
    {
        if (!options.Gore || state.TargetBodyPart is not { } id || world.Characters.Cold(state.TargetSlot) is not { } victim) return fallback;
        int part = BodyPartIndex(victim, id);
        return part >= 0 && world.SystemOf<Ragdolls.RagdollSystem>() is { } ragdolls
            && ragdolls.TryPartPosition(new(state.TargetSlot, state.TargetGeneration), victim.Medical!.Parts[part].Template, out var position) ? position : fallback;
    }

    internal static float PartTargetGap(Vector3 from, Vector3 aim, CharacterCold attacker) =>
        MathF.Max(0, Vector3.Distance(from, aim) - 1.5f * CombatTuning.Footprint(attacker) * (attacker.Medical?.ScaleK ?? 1));

    bool StrikeBodyPart(World world, int attacker, in CombatSlot state, Zone zone, int blow, EffectBuffer effects)
    {
        if (state.TargetBodyPart is null) return false;
        if (!ValidBodyPartTarget(world, attacker, state)) return true;
        var point = TargetPoint(world, state, world.Characters.Previous[state.TargetSlot].Position);
        var delta = new Vector2(point.X, point.Z) - zone.Centre;
        float along = MathF.Max(0, MathF.Abs(Vector2.Dot(delta, zone.Forward)) - zone.Deep);
        float across = MathF.Max(0, MathF.Abs(delta.X * zone.Forward.Y - delta.Y * zone.Forward.X) - zone.Half);
        // A small contact radius belongs to the chosen anatomy site, rather than the owner's whole-body footprint.
        if (MathF.Abs(point.Y - zone.Y) <= zone.High && along * along + across * across <= 1)
            effects.Emit(attacker, state.TargetSlot, BlowEffect, state.AttackSeq, default, blow);
        return true;
    }
}