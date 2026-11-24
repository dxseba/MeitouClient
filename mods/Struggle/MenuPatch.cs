using System.Reflection;
using HarmonyLib;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.Struggle;

/// <summary>
/// The carried character's own Struggle row. The game gives a body that someone outside the player's faction carries an empty menu and asks the
/// mods' rows for it neither (docs/modding.md "Right-click menu rows"), so a Harmony postfix on <c>Meitou.Game.Ui.ContextMenuWindow.Verbs</c> adds
/// the row to that empty list. A rename of the method leaves the row on the carrier's menu only (logged).
/// </summary>
static class MenuPatch
{
    static StruggleUi? s_ui;

    static Action<string> s_log = _ => { };

    static bool s_faulted;

    internal static void Install(ModContext context, StruggleUi ui)
    {
        s_ui = ui;
        s_log = context.Log;
        var type = typeof(Game.Ui.UiRegistration).Assembly.GetType("Meitou.Game.Ui.ContextMenuWindow");
        var verbs = type?.GetMethod("Verbs", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, [typeof(CharacterSnapshot)]);
        if (verbs is null || verbs.ReturnType != typeof(List<(string, Action)>))
        {
            context.Log("ContextMenuWindow.Verbs not found: the Struggle row shows on the carrier's menu only (the port needs an update)");
            return;
        }

        context.Harmony.Patch(verbs, postfix: new HarmonyMethod(typeof(MenuPatch), nameof(AfterVerbs)));
    }

    /// <summary>Harmony postfix: a carried character's empty menu gets its Struggle row. A throw is logged once and the menu stays the game's.</summary>
#pragma warning disable IDE1006 // Harmony injects the result by this name
    static void AfterVerbs(CharacterSnapshot target, List<(string Caption, Action Give)> __result)
#pragma warning restore IDE1006
    {
        if (s_faulted || s_ui is not { HasSelection: true } ui || __result.Count > 0 || target.CarriedBy.IsNone) return;

        try
        {
            if (ui.Row(target.Id) is { } row) __result.Add((row.Caption, row.Choose));
        }
        catch (Exception e)
        {
            s_faulted = true;
            s_log($"Struggle's menu row stopped after an error: {e}");
        }
    }
}
