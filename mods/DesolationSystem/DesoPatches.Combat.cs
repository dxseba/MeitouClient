using HarmonyLib;
using Meitou.Data.Gameplay.Combat;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.DesolationSystem;

/// <summary>
/// The combat patches: weapon reach (Heavy Onslaught, Polearm Supremacy, Universal Mastery) through the callers of <c>Fighter.WeaponLength</c>;
/// Shadow Step's faster dodges through <c>CombatSystem.StartDefence</c>'s call of <c>PlayRate</c>; Phantom Movement's cleared defence timers after
/// <c>CombatSystem.ApplyBlocked</c>.
/// </summary>
static partial class DesoPatches
{
    static Func<CombatSystem, World, int, CharacterCold, CombatTechnique, bool, float>? s_playRate;
    static AccessTools.FieldRef<CombatSystem, CombatSlot[]>? s_write;

    static void InstallCombat(ModContext context)
    {
        var length = AccessTools.PropertyGetter(typeof(Fighter), nameof(Fighter.WeaponLength));
        Calls.Swap(context, length, Method(nameof(WeaponLength)),
            AccessTools.Method(typeof(CombatSystem), "ZoneOf"), AccessTools.Method(typeof(CombatSystem), "StartAttack"),
            AccessTools.Method(typeof(CombatSystem), nameof(CombatSystem.Move)), AccessTools.Method(typeof(PursuitSystem), "Chase"),
            AccessTools.Method(typeof(PursuitSystem), "SetBand"));

        var play = AccessTools.Method(typeof(CombatSystem), "PlayRate");
        if (play is not null) s_playRate = AccessTools.MethodDelegate<Func<CombatSystem, World, int, CharacterCold, CombatTechnique, bool, float>>(play);
        Calls.Swap(context, play, Method(nameof(PlayRate)), AccessTools.Method(typeof(CombatSystem), "StartDefence"));

        if (AccessTools.Field(typeof(CombatSystem), "_write") is not null)
        {
            s_write = AccessTools.FieldRefAccess<CombatSystem, CombatSlot[]>("_write");
            Postfix(typeof(CombatSystem), "ApplyBlocked", nameof(BlockedPostfix));
        }
        else context.LogError("CombatSystem._write not found: Phantom Movement is off");
    }

    /// <summary>The weapon length a fighter's reach is worked out from, times its reach factor.</summary>
    static float WeaponLength(Fighter fighter)
    {
        float length = fighter.WeaponLength;
        try
        {
            return length * TraitTable.ReachOf(fighter);
        }
        catch (Exception e)
        {
            Fault(e);
            return length;
        }
    }

    /// <summary>Shadow Step: a dodge technique plays faster, its recovery cut by the perk's share.</summary>
    static float PlayRate(CombatSystem combat, World world, int slot, CharacterCold cold, CombatTechnique tech, bool attack)
    {
        float rate = s_playRate!(combat, world, slot, cold, tech, attack);
        try
        {
            float faster = TraitTable.Of(cold).DodgeRate;
            if (!attack && tech.IsDodge && faster > 1) rate *= faster;
        }
        catch (Exception e)
        {
            Fault(e);
        }

        return rate;
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    /// <summary>Phantom Movement: after a successful block the defender may start its next defence at once (defence, recheck and stagger timers cleared).</summary>
    static void BlockedPostfix(CombatSystem __instance, int d, CharacterCold dc, int now)
    {
        try
        {
            if (!TraitTable.Of(dc).Phantom || s_write is null) return;

            var slots = s_write(__instance);
            if ((uint)d >= (uint)slots.Length) return;

            ref var slot = ref slots[d];
            slot.DefendReadyTick = Math.Min(slot.DefendReadyTick, now);
            slot.ReactCheckTick = Math.Min(slot.ReactCheckTick, now);
            slot.StunUntil = Math.Min(slot.StunUntil, now);
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }
#pragma warning restore IDE1006
}
