using System.Numerics;
using HarmonyLib;
using Meitou.Modding;

namespace Meitou.QuickStartSelect;

/// <summary>
/// The pointer for the window, which screen controls do not get from the game: its place each frame (prefix on <c>PlayerInterface.ArrangeUi</c>,
/// reading the interface's <c>Mouse</c>) for hover help, and the wheel (prefix on <c>PlayerInterface.UiScroll</c>), taken when it is over the
/// window's list, description or popup. Both on the host's thread.
/// </summary>
static class PointerPatch
{
    static readonly Type? Player = AccessTools.TypeByName("Meitou.Game.PlayerInterface");
    static readonly AccessTools.FieldRef<object, Vector2>? MouseField = Player is null || AccessTools.Field(Player, "Mouse") is null ? null
        : AccessTools.FieldRefAccess<Vector2>(Player, "Mouse");

    /// <summary>The window taking the wheel; null before one registers.</summary>
    public static StartWindow? Window;

    /// <summary>The pointer as last arranged; null until known.</summary>
    public static Vector2? Mouse;

    static Action<string>? s_log;
    static bool s_logged;

    public static void Patch(ModContext context)
    {
        s_log = context.LogError;
        var arrange = Player is null ? null : AccessTools.Method(Player, "ArrangeUi");
        var scroll = Player is null ? null : AccessTools.Method(Player, "UiScroll");
        if (arrange is null || scroll is null || MouseField is null)
        {
            context.LogError("PlayerInterface.ArrangeUi/UiScroll/Mouse not found: no wheel or hover in the window");
            return;
        }

        context.Harmony.Patch(arrange, prefix: new HarmonyMethod(typeof(PointerPatch), nameof(ArrangePrefix)));
        context.Harmony.Patch(scroll, prefix: new HarmonyMethod(typeof(PointerPatch), nameof(ScrollPrefix)));
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    /// <summary>Prefix on <c>PlayerInterface.ArrangeUi</c>: keeps the pointer.</summary>
    public static void ArrangePrefix(object __instance)
    {
        try
        {
            Mouse = MouseField!(__instance);
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    /// <summary>Prefix on <c>PlayerInterface.UiScroll</c>: the window takes a wheel step over its lists (the game's handling skipped).</summary>
    public static bool ScrollPrefix(Vector2 p, float steps, ref bool __result)
    {
        try
        {
            if (Window is not { } window || !window.Wheel(p, steps)) return true;

            __result = true;
            return false;
        }
        catch (Exception e)
        {
            Fault(e);
            return true;
        }
    }
#pragma warning restore IDE1006

    static void Fault(Exception e)
    {
        if (!s_logged) s_log?.Invoke($"pointer patch failed, the window goes without wheel and hover: {e.Message}");
        s_logged = true;
    }
}
