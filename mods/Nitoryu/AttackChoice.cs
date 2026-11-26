using HarmonyLib;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.Nitoryu;

/// <summary>
/// The attack-choice filter (the original's <c>chooseAttack</c> hook): a dual fighter's technique comes from the private pool of the original's
/// moves by its rank (README "The ranks", <see cref="MovePicker"/>). Harmony: a prefix and a postfix on
/// <c>Meitou.Simulation.Combat.CombatSystem.StartAttack</c> (who is choosing, and whether the pick became a swing) and a postfix on
/// <c>Meitou.Simulation.Combat.TechniqueChooser.ChooseAttack</c> (the pick). The choice runs in the fighter's own partition of the combat act
/// phase; the context is per thread and the state written is the fighter's own. A failure logs once and keeps the game's pick.
/// </summary>
static class AttackChoice
{
    /// <summary>Who is choosing on this thread, and the pick the postfix made for it.</summary>
    sealed class Choosing
    {
        public World? World;
        public NitoryuSystem? System;
        public int Slot;
        public ulong Key;
        public int Seq;
        public int Now;
        public MovePick Pick;
        public World? AskWorld;
        public int AskSlot;
        public int AskTarget;
    }

    [ThreadStatic]
    static Choosing? s_choosing;

    static ModContext? s_context;
    static int s_failed;

    public static void Patch(ModContext context)
    {
        s_context = context;
        var start = AccessTools.Method(typeof(CombatSystem), "StartAttack");
        var choose = AccessTools.Method(typeof(TechniqueChooser), nameof(TechniqueChooser.ChooseAttack));
        if (start is null || choose is null)
        {
            context.LogError("CombatSystem.StartAttack or TechniqueChooser.ChooseAttack not found: dual fighters swing as with one blade");
            return;
        }

        context.Harmony.Patch(start, prefix: new HarmonyMethod(typeof(AttackChoice), nameof(StartPrefix)),
            postfix: new HarmonyMethod(typeof(AttackChoice), nameof(StartPostfix)));
        context.Harmony.Patch(choose, postfix: new HarmonyMethod(typeof(AttackChoice), nameof(ChoosePostfix)));
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void StartPrefix(World world, int i, ref CombatSlot s, ulong key, int now)
    {
        var c = s_choosing ??= new Choosing();
        c.World = null;
        (c.AskWorld, c.AskSlot, c.AskTarget) = (world, i, s.TargetSlot);
        try
        {
            if (NitoryuSystem.Of(world) is not { } system || !system.State.IsDual(i)) return;

            (c.World, c.System, c.Slot, c.Key, c.Seq, c.Now, c.Pick) = (world, system, i, key, s.AttackSeq, now, default);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Failed(e);
        }
    }

    static void StartPostfix(ref CombatSlot s)
    {
        if (s_choosing is { } asked) asked.AskWorld = null;
        if (s_choosing is not { World: { } world, System: { } system } c) return;

        c.World = null;
        try
        {
            bool started = s.AttackSeq == c.Seq + 1;
            if (started) MovePicker.Commit(system, world, c.Slot, c.Pick, s.AttackSeq, s.AttackTech, c.Now);

            int tech = s.AttackTech;
            if (c.Pick.Move is { } move)
                Diag.Log("attack-start", 20, () => $"slot {c.Slot}: {move.Tech.Name} {(started ? $"started as technique {tech}" : "did not start")}");
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Failed(e);
        }
    }

    static void ChoosePostfix(in AttackQuery q, ref float maxReach, ref int __result)
    {
        if (s_choosing is not { World: { } world, System: { } system } c) return;

        try
        {
            var picker = new MovePicker(system, world, c.Slot, c.Key, c.Seq, c.Now);
            int vanilla = __result;
            c.Pick = picker.Choose(q, __result, ref maxReach);
            if (c.Pick.Move is { } move) __result = move.Index;

            if (world.Characters.Cold(c.Slot) is { IsPlayer: true } cold)
            {
                var chosen = c.Pick.Move;
                Diag.Log("attack", 20, () => $"{cold.Name}: game picked {vanilla}, dual pick {(chosen is null ? "none" : $"{chosen.Tech.Name} ({chosen.Kind})")}");
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Failed(e);
        }
    }
#pragma warning restore IDE1006

    /// <summary>
    /// The pressure points to add to <paramref name="cold"/>'s skill in this thread's initiative ask: the asker's own pressure on its target less
    /// the target's on it; 0 for anyone else or outside an attack start.
    /// </summary>
    public static float PressureShift(CharacterCold cold)
    {
        if (s_choosing is not { AskWorld: { } world } c || !ReferenceEquals(world.Characters.Cold(c.AskSlot), cold)) return 0;

        return NitoryuSystem.Of(world)?.PressureShift(world, c.AskSlot, c.AskTarget) ?? 0;
    }

    static void Failed(Exception e)
    {
        if (Interlocked.Exchange(ref s_failed, 1) == 0) s_context?.LogError($"dual attack choice failed ({e.GetType().Name}: {e.Message})");
    }
}
