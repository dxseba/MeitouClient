using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Gameplay;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Movement;

namespace Meitou.ArcaneMagic;

/// <summary>
/// The Focus's clips (README "Facts"): its aim pose is the original's <c>AM_ArcaneAim_Xbow</c> in place of the crossbow's <c>aimH</c> (the
/// original's "aimH transient mutation"), and after an Earth Spike, Whirlwind or Arcane Bomb its recovery (Meitou's reload pose, the Focus's
/// <c>AM_ArcaneCast_Xbow</c>) is that spell's cast clip (<c>AM_earth_spike_cast_v001</c>, <c>AM_whirlwind_cast_v001</c>,
/// <c>AM_bomb_cast_v001</c>) over the same time. Harmony: a prefix on <c>Meitou.Simulation.Movement.AnimationSystem.Update</c> changing only the
/// character's combat view, in its own partition; a clip its skeleton lacks is not swapped in. A failure logs once and leaves the vanilla clips.
/// </summary>
static class FocusAnimation
{
    sealed class Clips(AnimationLibrary library)
    {
        public readonly int AimH = library.IndexOf(AnimationLibrary.RangedAimRecord);
        public readonly int Aim = library.IndexOfRecord("22-ArcaneMagic.mod");
        public readonly int Recover = library.IndexOfRecord("21-ArcaneMagic.mod");
        public readonly int Spike = library.IndexOfRecord("245-ArcaneMagic.mod");
        public readonly int Whirlwind = library.IndexOfRecord("247-ArcaneMagic.mod");
        public readonly int Bomb = library.IndexOfRecord("255-ArcaneMagic.mod");
    }

    static readonly ConditionalWeakTable<AnimationLibrary, Clips> ClipTable = [];
    static Func<AnimationSystem, CharacterAnimation, CharacterCold, int, bool>? s_plays;
    static ModContext? s_context;
    static int s_failed;

    public static void Patch(ModContext context)
    {
        s_context = context;
        const string type = "Meitou.Simulation.Movement.AnimationSystem";
        var update = AccessTools.Method(typeof(AnimationSystem), nameof(AnimationSystem.Update));
        var plays = AccessTools.Method($"{type}:Plays");
        if (update is null || plays is null)
        {
            context.Log($"{type}.Update or Plays not found: the Focus plays the crossbow's clips");
            return;
        }

        s_plays = AccessTools.MethodDelegate<Func<AnimationSystem, CharacterAnimation, CharacterCold, int, bool>>(plays);
        context.Harmony.Patch(update, prefix: new HarmonyMethod(typeof(FocusAnimation), nameof(UpdatePrefix)));
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void UpdatePrefix(AnimationSystem __instance, CharacterAnimation a, CharacterCold cold, ref CombatView? combatView)
    {
        if (s_failed != 0 || combatView is not { Technique: >= 0 } view || !ArcaneSystem.HoldsFocus(cold)) return;

        try
        {
            combatView = Swapped(__instance, a, cold, view);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            if (Interlocked.Exchange(ref s_failed, 1) == 0) s_context?.LogError($"Focus animation step failed ({e.GetType().Name}: {e.Message})");
        }
    }
#pragma warning restore IDE1006

    static CombatView Swapped(AnimationSystem system, CharacterAnimation a, CharacterCold cold, CombatView view)
    {
        var clips = ClipTable.GetValue(system.Library, static l => new Clips(l));
        int clip = -1;
        if (view.Technique == clips.AimH) clip = clips.Aim;
        else if (view.Technique == clips.Recover && ArcaneSystem.Running?.LastCast(cold) is { } spell)
            clip = spell switch
            {
                Spell.EarthSpike => clips.Spike,
                Spell.Whirlwind => clips.Whirlwind,
                Spell.Bomb => clips.Bomb,
                _ => -1,
            };

        return clip >= 0 && s_plays!(system, a, cold, clip) ? view with { Technique = clip } : view;
    }
}
