using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Gameplay;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Movement;

namespace Meitou.BowMod;

/// <summary>
/// The original plugin's clip swaps for a character with a bow in hand (README "Facts"), on the ANIMATION records of the original's data, whose
/// clips Meitou reads from the original's <c>bowmod_male.skeleton</c> / <c>bowmod_female.skeleton</c> (its ANIMATION_FILE, linked to every race):
/// <list type="bullet">
/// <item>standing (speed under 0.1, not down), the crossbow aim (<c>aimH</c>) is <c>bowmod_aim_hold</c> while drawing and <c>bowmod_release</c>
/// for the shot; moving, neither the aim nor a <c>bowmod_</c> clip (the bow's reload) plays;</item>
/// <item>moving faster than 0.1, <c>run lower</c> is <c>bowmod_sprint_lower</c>, and <c>bowmod_sprint_upper</c> takes the upper body at the
/// lower clip's weight (both synch to the gait phase).</item>
/// </list>
/// Harmony: a prefix on <c>Meitou.Simulation.Movement.AnimationSystem.Update</c> (the combat view's clip) and a postfix on
/// <c>Meitou.Simulation.Movement.AnimationSystem.PickGait</c> (the movement clips). Both run in the character's own partition and change only that
/// character's wanted clips. A clip the character's skeleton lacks is not swapped in. A failure logs once and leaves the vanilla clips.
/// </summary>
static class BowAnimation
{
    /// <summary>The original's speed threshold between standing and moving (units per second).</summary>
    const float Still = 0.1f;

    /// <summary>The library indices of the clips the swaps use (-1 when the data lacks one).</summary>
    sealed class Clips(AnimationLibrary library)
    {
        public readonly int AimH = library.IndexOf(AnimationLibrary.RangedAimRecord);
        public readonly int AimHold = library.IndexOf("bowmod_aim_hold");
        public readonly int Release = library.IndexOf("bowmod_release");
        public readonly int RunLower = library.IndexOf("run lower");
        public readonly int SprintLower = library.IndexOf("bowmod_sprint_lower");
        public readonly int SprintUpper = library.IndexOf("bowmod_sprint_upper");
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
        var gait = AccessTools.Method($"{type}:PickGait");
        var plays = AccessTools.Method($"{type}:Plays");
        if (update is null || gait is null || plays is null)
        {
            context.LogError($"{type}.Update, PickGait or Plays not found: bows play the crossbow clips");
            return;
        }

        s_plays = AccessTools.MethodDelegate<Func<AnimationSystem, CharacterAnimation, CharacterCold, int, bool>>(plays);
        context.Harmony.Patch(update, prefix: new HarmonyMethod(typeof(BowAnimation), nameof(UpdatePrefix)));
        context.Harmony.Patch(gait, postfix: new HarmonyMethod(typeof(BowAnimation), nameof(GaitPostfix)));
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void UpdatePrefix(AnimationSystem __instance, CharacterAnimation a, CharacterCold cold, float speed, ref CombatView? combatView)
    {
        try
        {
            if (combatView is { Technique: >= 0 } view && BowInHand(cold)) combatView = AimView(__instance, a, cold, speed, view);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Failed(e);
        }
    }

    static void GaitPostfix(AnimationSystem __instance, CharacterAnimation a, CharacterCold cold, float speed, List<(int Index, float Weight)> lower,
        ref int legs)
    {
        try
        {
            if (MathF.Abs(speed) > Still && BowInHand(cold)) Sprint(__instance, a, cold, lower, legs);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Failed(e);
        }
    }
#pragma warning restore IDE1006

    /// <summary>A character whose drawn weapon is its crossbow, and that crossbow a bow.</summary>
    static bool BowInHand(CharacterCold cold) =>
        cold.DrawnWeapon != WeaponKinds.None && cold.DrawnWeapon == HandHold.OfCategory((int)Data.Gameplay.Combat.WeaponCategory.Bow).Weapon
        && BowSkill.HoldsBow(cold);

    /// <summary>
    /// The aim pose for a bow: standing, the aim's first half (Meitou's drawing, <c>RangedClip</c>) is the aim hold through its length and the second
    /// half (the shot) the release; moving, no aim or bow clip at all.
    /// </summary>
    static CombatView AimView(AnimationSystem system, CharacterAnimation a, CharacterCold cold, float speed, CombatView view)
    {
        var clips = ClipTable.GetValue(system.Library, static l => new Clips(l));
        bool aim = view.Technique == clips.AimH;
        bool bowClip = system.Library.Definitions[view.Technique].Name.StartsWith("bowmod_", StringComparison.Ordinal);
        if (!aim && !bowClip) return view;

        bool standing = MathF.Abs(speed) < Still && cold.Medical is not { Incapacitated: true };
        if (!standing) return view with { Technique = -1, Progress = 0 };
        if (!aim) return view;

        bool drawing = view.Progress <= 0.5f;
        int clip = drawing ? clips.AimHold : clips.Release;
        if (clip < 0 || !Plays(system, a, cold, clip)) return view;

        return view with { Technique = clip, Progress = drawing ? view.Progress * 2 : (view.Progress - 0.5f) * 2 };
    }

    /// <summary>
    /// The split sprint: <c>run lower</c> among the legs' clips becomes <c>bowmod_sprint_lower</c>, and <c>bowmod_sprint_upper</c> takes its share
    /// of the upper body from the other upper clips.
    /// </summary>
    static void Sprint(AnimationSystem system, CharacterAnimation a, CharacterCold cold, List<(int Index, float Weight)> lower, int legs)
    {
        var clips = ClipTable.GetValue(system.Library, static l => new Clips(l));
        if (clips.RunLower < 0 || clips.SprintLower < 0 || !Plays(system, a, cold, clips.SprintLower)) return;

        float share = 0;
        for (int i = 0; i < legs && i < lower.Count; i++)
        {
            if (lower[i].Index == clips.RunLower) lower[i] = (clips.SprintLower, lower[i].Weight);
            if (lower[i].Index == clips.SprintLower) share += lower[i].Weight;
        }

        if (share <= 0 || clips.SprintUpper < 0 || !Plays(system, a, cold, clips.SprintUpper)) return;

        share = MathF.Min(share, 1);
        var definitions = system.Library.Definitions;
        for (int i = legs; i < lower.Count; i++)
            if (definitions[lower[i].Index].Area == AnimationArea.Upper) lower[i] = (lower[i].Index, lower[i].Weight * (1 - share));

        lower.Add((clips.SprintUpper, share));
    }

    static bool Plays(AnimationSystem system, CharacterAnimation a, CharacterCold cold, int clip) => s_plays?.Invoke(system, a, cold, clip) ?? false;

    static void Failed(Exception e)
    {
        if (Interlocked.Exchange(ref s_failed, 1) == 0) s_context?.LogError($"bow animation step failed ({e.GetType().Name}: {e.Message})");
    }
}
