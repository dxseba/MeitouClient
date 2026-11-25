using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Gameplay.Combat;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.DirectControl;

/// <summary>
/// Harmony on the combat system's swing start (<c>CombatSystem.StartAttack</c>, the AI's attack decision): the controlled character starts a
/// swing only when <see cref="ControlSystem.MaySwing"/> lets it, and the start uses its press up. In the Action RPG scheme the technique chooser
/// (<c>TechniqueChooser.ChooseAttack</c>) is asked again for a technique of the chosen direction; with none, no swing starts. Both run in the
/// combat act phase on the swinging character's thread. A fault is logged once and the game's behaviour kept.
/// </summary>
static class AttackGate
{
    static readonly ConditionalWeakTable<World, ControlSystem> Systems = new();

    static Action<string>? s_log;
    static int s_faulted;

    /// <summary>
    /// The direction the swing being chosen on this thread must have (0: any), set by the start's prefix.
    /// </summary>
    [ThreadStatic] static int s_direction;

    [ThreadStatic] static bool s_rechoosing;

    public static void Install(ModContext context)
    {
        s_log = context.LogError;
        var start = AccessTools.Method(typeof(CombatSystem), "StartAttack");
        var choose = AccessTools.Method(typeof(TechniqueChooser), nameof(TechniqueChooser.ChooseAttack));
        if (start is null || choose is null)
        {
            context.LogError("CombatSystem.StartAttack or TechniqueChooser.ChooseAttack not found: manual combat is off");
            return;
        }

        context.Harmony.Patch(start, prefix: new HarmonyMethod(typeof(AttackGate), nameof(StartPrefix)),
            postfix: new HarmonyMethod(typeof(AttackGate), nameof(StartPostfix)));
        context.Harmony.Patch(choose, postfix: new HarmonyMethod(typeof(AttackGate), nameof(ChoosePostfix)));
    }

    public static ControlSystem? Of(World world) =>
        Systems.TryGetValue(world, out var system) ? system : Find(world);

    static ControlSystem? Find(World world)
    {
        var system = world.Systems.OfType<ControlSystem>().FirstOrDefault();
        if (system is not null) Systems.AddOrUpdate(world, system);

        return system;
    }

    static bool StartPrefix(World world, int i)
    {
        try
        {
            s_direction = 0;
            if (Of(world) is not { } system) return true;
            if (!system.MaySwing(world, i)) return false;

            s_direction = system.SwingDirection(world, i);
            return true;
        }
        catch (Exception e)
        {
            Fault(e);
            return true;
        }
    }

    static void StartPostfix(World world, int i, ref CombatSlot s, int now)
    {
        try
        {
            s_direction = 0;
            if (s.AttackTech >= 0 && s.AttackStart == now) Of(world)?.Swung(world, i);
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    /// <summary>
    /// A pick of another direction is chosen again with spread rolls until one of the wanted direction comes; none: no technique.
    /// </summary>
#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void ChoosePostfix(IReadOnlyList<CombatTechnique> all, in AttackQuery q, ref float maxReach, ref int __result)
    {
        try
        {
            int want = s_direction;
            if (want == 0 || s_rechoosing || __result < 0 || all[__result].AttackDirection1 == want) return;

            __result = Rechoose(all, q, want, ref maxReach);
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

#pragma warning restore IDE1006

    static int Rechoose(IReadOnlyList<CombatTechnique> all, in AttackQuery q, int want, ref float maxReach)
    {
        const int rolls = 16;
        s_rechoosing = true;
        try
        {
            for (int k = 0; k < rolls; k++)
            {
                int pick = TechniqueChooser.ChooseAttack(all, q, (k + 0.5f) / rolls, out float reach);
                if (pick >= 0 && all[pick].AttackDirection1 == want)
                {
                    maxReach = reach;
                    return pick;
                }
            }

            return -1;
        }
        finally
        {
            s_rechoosing = false;
        }
    }

    static void Fault(Exception e)
    {
        if (Interlocked.Exchange(ref s_faulted, 1) == 0) s_log?.Invoke($"attack gate failed, the game's own swing kept: {e.Message}");
    }
}
