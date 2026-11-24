using System.Numerics;
using HarmonyLib;
using Meitou.Modding;

namespace Meitou.ContainerHighlight;

/// <summary>
/// The world tint (the original's <c>enable_tint</c>, a shader colour override on the containers' models): Meitou's building outline, which draws a
/// building's meshes alone into a mask and outlines and tints it in a colour, as it does the selected and the hovered building. A Harmony postfix on
/// the internal <c>Meitou.Game.Building.BuildingSelection.OutlinedBuildings</c> (host thread, once a frame; 139 bytes of IL, not inlined) adds the
/// highlighted containers in the tint colour while the gate is open and the tint is on, after the game's own (a selected or hovered container keeps
/// its own colour). Each outlined building takes a full-screen mask of its own, so only the nearest <see cref="MaxTinted"/> are tinted. Needs the
/// game's building outline (Settings, on by default).
/// </summary>
static class ContainerOutline
{
    /// <summary>
    /// The most containers tinted at once (a port limit; the original tints every highlighted one).
    /// </summary>
    const int MaxTinted = 8;

    static ContainerScan? s_scan;
    static Action<string>? s_log;
    static bool s_failed;

    public static void Install(ModContext context, ContainerScan scan)
    {
        s_scan = scan;
        s_log = context.Log;
        var target = AccessTools.Method("Meitou.Game.Building.BuildingSelection:OutlinedBuildings", [typeof(bool)]);
        if (target is null)
        {
            context.Log("BuildingSelection.OutlinedBuildings not found: containers are not tinted");
            return;
        }

        context.Harmony.Patch(target, postfix: new HarmonyMethod(typeof(ContainerOutline), nameof(Postfix)));
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void Postfix(List<(string Key, float Floor, Vector4 Colour)> __result)
    {
        if (s_failed || s_scan is not { Open: true } scan || !ContainerHighlightMod.Settings.Tint || __result is null) return;

        try
        {
            var colour = ContainerHighlightMod.Settings.TintRgba;
            var items = scan.Current;
            for (int i = 0; i < items.Count && i < MaxTinted; i++)
            {
                var item = items[i];
                if (!__result.Exists(o => o.Key == item.Key)) __result.Add((item.Key, item.Floor, colour));
            }
        }
        catch (Exception e)
        {
            s_failed = true;
            s_log?.Invoke($"container tint failed, left off for the session: {e.Message}");
        }
    }
#pragma warning restore IDE1006
}
