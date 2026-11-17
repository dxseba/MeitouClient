using System.Reflection;
using HarmonyLib;
using Meitou.Game.Host;
using Meitou.Game.Host.Input;
using Meitou.Modding;

namespace Meitou.JobManagement;

/// <summary>
/// The interface hooks: a postfix on the private <c>PlayerInterface.BindUi</c> (run once per frame on the interface's thread) hands the window its
/// session, and a prefix on <c>GameHotkeys.Key</c> takes Ctrl+J before the game's Rescue binding sees the J.
/// </summary>
static class Hooks
{
    static FieldInfo? s_sessionField;
    static FieldInfo? s_ctrlField;
    static FieldInfo? s_hotkeysPlayer;

    public static void Install(ModContext context)
    {
        var player = AccessTools.TypeByName("Meitou.Game.PlayerInterface") ?? throw new TypeLoadException("Meitou.Game.PlayerInterface");
        s_sessionField = AccessTools.Field(player, "Session") ?? throw new MissingFieldException(player.FullName, "Session");
        s_ctrlField = AccessTools.Field(player, "CtrlHeld") ?? throw new MissingFieldException(player.FullName, "CtrlHeld");
        var bind = AccessTools.Method(player, "BindUi") ?? throw new MissingMethodException(player.FullName, "BindUi");
        context.Harmony.Patch(bind, postfix: new HarmonyMethod(typeof(Hooks), nameof(Bound)));

        var hotkeys = AccessTools.TypeByName("Meitou.Game.Ui.GameHotkeys") ?? throw new TypeLoadException("Meitou.Game.Ui.GameHotkeys");
        s_hotkeysPlayer = AccessTools.GetDeclaredFields(hotkeys).FirstOrDefault(f => f.FieldType == player)
            ?? throw new MissingFieldException(hotkeys.FullName, "player");
        var key = AccessTools.Method(hotkeys, "Key", [typeof(Key), typeof(bool)]) ?? throw new MissingMethodException(hotkeys.FullName, "Key");
        context.Harmony.Patch(key, prefix: new HarmonyMethod(typeof(Hooks), nameof(BeforeKey)));
    }

    #pragma warning disable IDE1006 // Harmony binds __instance by name
    static void Bound(object __instance)
    {
        if (s_sessionField?.GetValue(__instance) is WorldSession session) JobManagementMod.Window.Frame(session);
    }

    /// <summary>Ctrl+J toggles the window instead of reaching the game's bindings (J is the Rescue key's second binding).</summary>
    static bool BeforeKey(object __instance, Key key)
    {
        var settings = JobManagementMod.Settings;
        if (key != Key.J || !settings.Enabled || !settings.OpenKey) return true;

        var player = s_hotkeysPlayer?.GetValue(__instance);
        if (player is null || s_ctrlField?.GetValue(player) is not true) return true;

        JobManagementMod.Window.Toggle();
        return false;
    }
    #pragma warning restore IDE1006
}
