using System.Collections;
using System.Reflection;
using HarmonyLib;
using Meitou.Data.Gameplay;
using Meitou.Game.Ui;

namespace Meitou.QuickStartSelect;

/// <summary>
/// The game's start window, reached by reflection (its members are internal to the game): the front-end screens object, captured each time the
/// window binds (Harmony postfix on <c>FrontendScreens.BindNewGame</c>); its start list, chosen index and click handler; the player interface's
/// advanced options and confirmation box; the session's player. A missing member leaves that part off (logged once by the mod).
/// </summary>
static class GameAccess
{
    static readonly Type? Frontend = AccessTools.TypeByName("Meitou.Game.Title.FrontendScreens");
    static readonly Type? Player = AccessTools.TypeByName("Meitou.Game.PlayerInterface");
    static readonly Type? Options = AccessTools.TypeByName("Meitou.Game.Title.AdvancedOptions");
    static readonly Type? Settings = AccessTools.TypeByName("Meitou.Game.Title.NewGameSettings");

    public static readonly MethodInfo? BindNewGame = Frontend is null ? null : AccessTools.Method(Frontend, "BindNewGame");
    static readonly MethodInfo? NewGameAction = Frontend is null ? null : AccessTools.Method(Frontend, "NewGameAction");
    static readonly FieldInfo? Starts = Frontend is null ? null : AccessTools.Field(Frontend, "_starts");
    static readonly FieldInfo? StartIndex = Frontend is null ? null : AccessTools.Field(Frontend, "_startIndex");
    static readonly FieldInfo? FrontendPlayer = Frontend is null ? null
        : AccessTools.GetDeclaredFields(Frontend).FirstOrDefault(f => f.FieldType == Player);

    static readonly PropertyInfo? AdvancedOptions = Player is null ? null : AccessTools.Property(Player, "AdvancedOptions");
    static readonly MethodInfo? Confirm = Player is null ? null : AccessTools.Method(Player, "Confirm");
    static readonly FieldInfo? Session = Player is null ? null : AccessTools.Field(Player, "Session");
    static readonly PropertyInfo? NewGameOptions = Options is null ? null : AccessTools.Property(Options, "NewGameOptions");
    static readonly FieldInfo? HelpCatalog = Options is null ? null : AccessTools.Field(Options, "AdvancedHelpCatalog");

    /// <summary>The front-end screens object last seen binding the start window (host thread).</summary>
    public static object? Screens;

    /// <summary>Whether the start window's members were all found.</summary>
    public static bool StartReady => BindNewGame is not null && NewGameAction is not null && Starts is not null && StartIndex is not null;

    /// <summary>Whether the advanced options' members were all found.</summary>
    public static bool OptionsReady => FrontendPlayer is not null && AdvancedOptions is not null && NewGameOptions is not null && Settings is not null
        && OptionTable.Ready;

    /// <summary>Harmony postfix on <c>FrontendScreens.BindNewGame</c>: keeps the instance.</summary>
#pragma warning disable IDE1006 // Harmony's injected parameter name
    public static void Captured(object __instance) => Screens = __instance;
#pragma warning restore IDE1006

    /// <summary>Chooses the start <paramref name="id"/> in the game's window; false when the window does not list it.</summary>
    public static bool Choose(string id)
    {
        if (Screens is not { } screens || Starts?.GetValue(screens) is not IEnumerable starts) return false;

        int index = starts.Cast<NewGameStart>().ToList().FindIndex(s => s.Id == id);
        if (index < 0) return false;

        StartIndex!.SetValue(screens, index);
        return true;
    }

    /// <summary>Presses one of the game window's own buttons (BeginButton, CancelButton) through its click handler.</summary>
    public static void Press(string widget)
    {
        if (Screens is { } screens) NewGameAction?.Invoke(screens, [new MyGuiEvent("new-game", widget, widget)]);
    }

    /// <summary>Whether a game is running (the game's Begin then asks itself).</summary>
    public static bool InPlay()
    {
        if (PlayerOf() is not { } player || Session?.GetValue(player) is not { } session) return false;

        var world = AccessTools.Property(session.GetType(), "World")?.GetValue(session);
        var state = world is null ? null : AccessTools.Property(world.GetType(), "Player")?.GetValue(world);
        return state is not null && AccessTools.Property(state.GetType(), "Exists")?.GetValue(state) is true;
    }

    /// <summary>The game's confirmation box; false when it could not be opened.</summary>
    public static bool Ask(string title, string text, Action yes)
    {
        if (PlayerOf() is not { } player || Confirm is null) return false;

        Confirm.Invoke(player, [title, text, yes, "YES", null, "CANCEL"]);
        return true;
    }

    /// <summary>The advanced options object, or null.</summary>
    static object? OptionsOf() => PlayerOf() is { } player ? AdvancedOptions?.GetValue(player) : null;

    static object? PlayerOf() => Screens is { } screens ? FrontendPlayer?.GetValue(screens) : null;

    /// <summary>The new game's settings record, or null.</summary>
    public static object? CurrentSettings() => OptionsOf() is { } options ? NewGameOptions?.GetValue(options) : null;

    /// <summary>Replaces the new game's settings record.</summary>
    public static void SetSettings(object settings)
    {
        if (OptionsOf() is { } options) NewGameOptions?.SetValue(options, settings);
    }

    /// <summary>The help text of help line <paramref name="line"/> (the game's own catalogue); empty without it.</summary>
    public static string Help(int line)
    {
        if (line < 0 || OptionsOf() is not { } options || HelpCatalog?.GetValue(options) is not Meitou.Data.Ui.NewGameHelpCatalog catalog) return "";

        return catalog.TextAt(line);
    }
}
