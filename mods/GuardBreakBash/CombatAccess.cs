using System.Reflection;
using HarmonyLib;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.GuardBreakBash;

/// <summary>
/// The combat system's non-public members this port needs, bound once by name (a nightly that renames one leaves the port off, logged): the blocked
/// blow (<c>CombatSystem.ApplyBlocked</c>, a postfix notes it) and the blow it belongs to (<c>CombatSystem.ApplyBlow</c>, a postfix rolls the noted
/// block once the blow's own steps, the defence observers' report included, are done), the stagger (<c>CombatSystem.Stagger</c>, which a landed blow uses), the
/// fighters' slots (<c>CombatSystem._write</c>, the slots Commit writes) and the effective stat (<c>CombatSystem.Eff</c>).
/// </summary>
static class CombatAccess
{
    delegate void StaggerCall(CombatSystem combat, World world, ref CombatSlot slot, int d, CharacterCold dc, bool heavy, int now, ulong counter,
        bool bolt);

    static StaggerCall? s_stagger;

    static AccessTools.FieldRef<CombatSystem, CombatSlot[]>? s_slots;

    /// <summary>The engine's effective stat (stored plus gear, times scale, injuries and gear factors), as combat reads it; null when not bound.</summary>
    internal static Func<CharacterCold, StatsEnumerated, float>? Eff { get; private set; }

    static Action<string> s_log = _ => { };

    static bool s_faulted;

    /// <summary>Binds the members and patches <c>ApplyBlocked</c>; false (logged) when one is missing.</summary>
    internal static bool Install(Harmony harmony, Action<string> log)
    {
        s_log = log;
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var type = typeof(CombatSystem);
        var blocked = type.GetMethod("ApplyBlocked", any);
        var blow = type.GetMethod("ApplyBlow", any);
        var stagger = type.GetMethod("Stagger", any);
        var slots = type.GetField("_write", any);
        var eff = type.GetMethod("Eff", any, [typeof(CharacterCold), typeof(StatsEnumerated)]);
        if (blocked is null || blow is null || stagger is null || slots is null || slots.FieldType != typeof(CombatSlot[]))
        {
            log("CombatSystem.ApplyBlocked, ApplyBlow, Stagger or _write not found: Guard Break & Bash is off (the port needs an update)");
            return false;
        }

        try
        {
            s_stagger = stagger.CreateDelegate<StaggerCall>();
            s_slots = AccessTools.FieldRefAccess<CombatSystem, CombatSlot[]>(slots);
            Eff = eff?.CreateDelegate<Func<CharacterCold, StatsEnumerated, float>>();
        }
        catch (ArgumentException e)
        {
            log($"CombatSystem.Stagger or Eff changed its parameters: Guard Break & Bash is off (the port needs an update): {e.Message}");
            return false;
        }

        harmony.Patch(blocked, postfix: new HarmonyMethod(typeof(CombatAccess), nameof(AfterBlocked)));
        harmony.Patch(blow, postfix: new HarmonyMethod(typeof(CombatAccess), nameof(AfterBlow)));
        return true;
    }

    /// <summary>The fighter's combat slot as Commit leaves it, or null.</summary>
    internal static CombatSlot? Slot(CombatSystem combat, int slot)
    {
        var slots = s_slots?.Invoke(combat);
        return slots is not null && slot >= 0 && slot < slots.Length ? slots[slot] : null;
    }

    /// <summary>Staggers the fighter in <paramref name="slot"/> as a landed blow does; false when not bound.</summary>
    internal static bool Stagger(CombatSystem combat, World world, int slot, CharacterCold cold, bool heavy, int now, ulong counter)
    {
        if (s_stagger is null || s_slots?.Invoke(combat) is not { } slots || slot < 0 || slot >= slots.Length) return false;

        s_stagger(combat, world, ref slots[slot], slot, cold, heavy, now, counter, false);
        return true;
    }

    /// <summary>Harmony postfix on <c>CombatSystem.ApplyBlocked</c> (serial, Commit): notes the block. A throw is logged once; blocks stay vanilla.</summary>
    static void AfterBlocked(World world, int a, int d, int now) => Guarded(() => GuardBreakSystem.Of(world)?.Noted(a, d, now));

    /// <summary>Harmony postfix on <c>CombatSystem.ApplyBlow</c>: rolls the block it noted.</summary>
#pragma warning disable IDE1006 // Harmony injects the instance by this name
    static void AfterBlow(CombatSystem __instance, World world) => Guarded(() => GuardBreakSystem.Of(world)?.Blocked(world, __instance));
#pragma warning restore IDE1006

    static void Guarded(Action act)
    {
        if (s_faulted) return;

        try
        {
            act();
        }
        catch (Exception e)
        {
            s_faulted = true;
            s_log($"Guard Break & Bash stopped after an error (blocks stay vanilla): {e}");
        }
    }
}
