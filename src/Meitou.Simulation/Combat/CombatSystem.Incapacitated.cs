namespace Meitou.Simulation.Combat;

public sealed partial class CombatSystem
{
    static bool HelplessTarget(CharacterCold target, long tick) => target.Medical is { Dead: false } medical
        && (medical.Incapacitated || medical.Prone || medical.PlayingDead || target.InSomething == global::Meitou.Simulation.Locks.HeldState.Bed || CharacterFacts.GettingUp(target, tick));

    bool AllowsIncapacitatedTarget(World world, int attacker, int target, bool picked, bool allow)
    {
        var table = world.Characters;
        return picked && allow && attacker != target && table.Cold(attacker) is { } source
            && table.Cold(target) is { Medical.Dead: false, CarriedBy.IsNone: true, IsImprisoned: false } victim
            && CharacterFacts.RegardsEnemy(Relations, source.Faction, victim.Faction);
    }

    bool MayKeepAttackTarget(World world, int attacker, int target, bool picked, bool allow, bool down = false)
    {
        var victim = world.Characters.Cold(target);
        if (victim is not { Medical: { Dead: false } medical, CarriedBy.IsNone: true, IsImprisoned: false }) return false;
        bool permitted = AllowsIncapacitatedTarget(world, attacker, target, picked, allow);
        // This extension is for enemies only. Standing targets retain the ordinary explicit-assault path.
        if (picked && allow && HelplessTarget(victim, world.Tick) && !permitted) return false;
        return !down && !medical.Incapacitated || permitted;
    }

    bool GroundTarget(World world, int attacker, in CombatSlot state) => state.TargetSlot >= 0
        && AllowsIncapacitatedTarget(world, attacker, state.TargetSlot, state.Picked, state.AllowIncapacitated)
        && world.Characters.Cold(state.TargetSlot) is { } target && HelplessTarget(target, world.Tick);
}
