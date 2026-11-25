using System.Collections.Concurrent;
using System.Reflection;
using HarmonyLib;
using Meitou.Data.Ui;
using Meitou.Game.Rendering.Frame;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Carrying;
using Meitou.Simulation.Crime;

namespace Meitou.DirectorPanel;

/// <summary>
/// The Harmony patches on Meitou's own methods for what the mod API has no call for (README "Harmony"). All are applied in
/// <see cref="DirectorPanelMod.Initialize"/>, before the game's JIT warm-up, so no caller holds an inlined copy. The simulation's two
/// (<c>CarrySystem.Openable</c>, <c>CrimeSystem.IsStealing</c>) read only state the panel's system sets in a serial step from commands
/// (<see cref="DirectorSystem"/> hashes it); the three on the interface and renderer change display only. Each body is wrapped: a throw is logged
/// once and the game's own result stands.
/// </summary>
internal static class Patches
{
    static ModContext? s_context;
    static int s_faulted;

    /// <summary>Characters the panel opened an inventory window on (Openable answers yes for them). Copy on write; set in a serial step.</summary>
    internal static volatile HashSet<CharacterId> OpenTargets = [];

    /// <summary>"Moving items does not count as stealing" (no take out of a character is a theft). Set in a serial step.</summary>
    internal static volatile bool NoTheft;

    /// <summary>Body blood the panel draws in place of the wounds' (0 to 1 a part, all parts alike), by character; display only.</summary>
    internal static readonly ConcurrentDictionary<CharacterId, float> BodyBlood = new();

    /// <summary>The World tab's opacities: speech bubbles and pop-up texts (0 to 1); display only.</summary>
    internal static volatile float SpeechOpacity = 1, FloaterOpacity = 1;

    public static void Install(ModContext context)
    {
        s_context = context;
        Postfix(context, AccessTools.Method(typeof(CarrySystem), nameof(CarrySystem.Openable)), nameof(OpenablePostfix));
        Postfix(context, AccessTools.Method(typeof(CrimeSystem), nameof(CrimeSystem.IsStealing), [typeof(World), typeof(CharacterCold)]),
            nameof(StealingPostfix));
        Postfix(context, AccessTools.Method(AccessTools.TypeByName("Meitou.Game.GameHost"), "Drawn"), nameof(DrawnPostfix));
        Prefix(context, AccessTools.Method(AccessTools.TypeByName("Meitou.Game.Ui.SpeechBubbles"), "AddBubble"), nameof(BubblePrefix));
        var floaters = AccessTools.Method(AccessTools.TypeByName("Meitou.Game.Ui.DamageFloaters"), "AddFloaters");
        if (floaters is not null)
        {
            context.Harmony.Patch(floaters, prefix: new HarmonyMethod(typeof(Patches), nameof(FloatersPrefix)),
                postfix: new HarmonyMethod(typeof(Patches), nameof(FloatersPostfix)));
        }
        else context.LogError("DamageFloaters.AddFloaters not found: the pop-up text opacity does nothing");
    }

    static void Postfix(ModContext context, MethodInfo? method, string patch)
    {
        if (method is null) context.LogError($"a patch target for {patch} is missing: that panel feature does nothing");
        else context.Harmony.Patch(method, postfix: new HarmonyMethod(typeof(Patches), patch));
    }

    static void Prefix(ModContext context, MethodInfo? method, string patch)
    {
        if (method is null) context.LogError($"a patch target for {patch} is missing: that panel feature does nothing");
        else context.Harmony.Patch(method, prefix: new HarmonyMethod(typeof(Patches), patch));
    }

    static void Fault(Exception e)
    {
        if (Interlocked.Exchange(ref s_faulted, 1) == 0) s_context?.LogError($"a panel patch failed, the game's own behaviour stays: {e}");
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void OpenablePostfix(CharacterTable table, int slot, ref bool __result)
    {
        try
        {
            if (!__result && OpenTargets.Count > 0 && OpenTargets.Contains(table.IdOf(slot))) __result = true;
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    static void StealingPostfix(ref bool __result)
    {
        if (NoTheft) __result = false;
    }

    static void DrawnPostfix(CharacterSnapshot c, ref CharacterInstance __result)
    {
        try
        {
            if (BodyBlood.IsEmpty || !BodyBlood.TryGetValue(c.Id, out float amount)) return;

            ulong part = (ulong)Math.Clamp((int)MathF.Round(amount * 255), 0, 255);
            ulong all = 0;
            for (int k = 0; k < 8; k++) all |= part << (8 * k);
            __result = __result with { Blood = all };
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    static void BubblePrefix(ref float alpha) => alpha *= SpeechOpacity;

    static void FloatersPrefix(List<UiControl> controls, out int __state) => __state = controls.Count;

    static void FloatersPostfix(List<UiControl> controls, int __state)
    {
        try
        {
            float opacity = FloaterOpacity;
            if (opacity >= 1) return;

            for (int i = __state; i < controls.Count; i++)
            {
                if (controls[i].TextColour is { } colour) controls[i] = controls[i] with { TextColour = colour with { W = colour.W * opacity } };
            }
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }
#pragma warning restore IDE1006
}
