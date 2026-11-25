using System.Collections;
using System.Reflection;
using HarmonyLib;
using Meitou.Data.Characters;
using Meitou.Simulation.Editing;

namespace Meitou.CharacterEditTweaks;

/// <summary>
/// The game's character editor members the port reaches by reflection (internal to the game): the panel
/// (<c>Meitou.Game.Editor.CharacterEditorPanel</c>), its session (shown character, tab, kind, drafts, camera orbit) and the orbit's distance. The
/// panel is the one that last bound the editor's window (<see cref="Capture"/>, from a postfix on its <c>BindEditor</c>). A member that is missing
/// leaves the features needing it off.
/// </summary>
static class EditorAccess
{
    public static readonly Type? Panel = AccessTools.TypeByName("Meitou.Game.Editor.CharacterEditorPanel");
    static readonly Type? Session = AccessTools.TypeByName("Meitou.Game.Editor.CharacterEditorPanel+EditorSession");
    static readonly Type? Orbit = AccessTools.TypeByName("Meitou.Game.Editor.EditorOrbit");

    public static readonly MethodInfo? BindEditor = Panel is null ? null : AccessTools.Method(Panel, "BindEditor");
    public static readonly MethodInfo? EditorWheel = Panel is null ? null : AccessTools.Method(Panel, "EditorWheel");
    static readonly MethodInfo? ConfirmEditor = Panel is null ? null : AccessTools.Method(Panel, "ConfirmEditor");
    static readonly FieldInfo? EditorField = Panel is null ? null : AccessTools.Field(Panel, "Editor");
    static readonly PropertyInfo? OpenProperty = Panel is null ? null : AccessTools.Property(Panel, "EditorOpen");

    static readonly PropertyInfo? CurrentProperty = Session is null ? null : AccessTools.Property(Session, "Current");
    static readonly FieldInfo? TabField = Session is null ? null : AccessTools.Field(Session, "Tab");
    static readonly PropertyInfo? KindProperty = Session is null ? null : AccessTools.Property(Session, "Kind");
    static readonly PropertyInfo? DraftsProperty = Session is null ? null : AccessTools.Property(Session, "Drafts");
    static readonly PropertyInfo? OrbitProperty = Session is null ? null : AccessTools.Property(Session, "Orbit");

    static readonly PropertyInfo? DistanceProperty = Orbit is null ? null : AccessTools.Property(Orbit, "Distance");
    static readonly PropertyInfo? HeadProperty = Orbit is null ? null : AccessTools.Property(Orbit, "Head");
    static readonly MethodInfo? SetDistanceMethod = Orbit is null ? null : AccessTools.Method(Orbit, "SetDistance");

    static readonly MethodInfo? DraftOf = Panel is null ? null : AccessTools.Method(Panel, "Draft");

    /// <summary>The panel that last bound the editor's window; null before.</summary>
    static volatile object? s_panel;

    /// <summary>Whether everything the session reads need was found.</summary>
    public static bool Ready => EditorField is not null && OpenProperty is not null && CurrentProperty is not null && TabField is not null
        && KindProperty is not null && DraftsProperty is not null && DraftOf is not null;

    /// <summary>Whether the camera's zoom can be changed.</summary>
    public static bool OrbitReady => OrbitProperty is not null && DistanceProperty is not null && HeadProperty is not null
        && SetDistanceMethod is not null;

    /// <summary>Whether CONFIRM can be pressed for the Cancel button.</summary>
    public static bool ConfirmReady => ConfirmEditor is not null;

    public static void Capture(object panel) => s_panel = panel;

    /// <summary>The open editor's session; null while it is closed.</summary>
    static object? OpenSession()
    {
        if (s_panel is not { } panel || OpenProperty?.GetValue(panel) is not true) return null;

        return EditorField?.GetValue(panel);
    }

    /// <summary>The open editor's shown tab, its kind and the shown character's draft (null for an animal); null while it is closed.</summary>
    public static (EditorTab Tab, EditKind Kind, CharacterDraft? Draft)? View()
    {
        if (OpenSession() is not { } session || TabField?.GetValue(session) is not EditorTab tab
            || KindProperty?.GetValue(session) is not EditKind kind)
        {
            return null;
        }

        var draft = DraftOf?.Invoke(s_panel, [CurrentProperty!.GetValue(session)]) as CharacterDraft;
        return (tab, kind, draft);
    }

    /// <summary>Every draft the open editor made so far; empty while it is closed.</summary>
    public static IReadOnlyList<CharacterDraft> Drafts()
    {
        if (OpenSession() is not { } session || DraftsProperty?.GetValue(session) is not IDictionary drafts) return [];

        return [.. drafts.Values.OfType<CharacterDraft>()];
    }

    /// <summary>The orbit of an editor session's camera: whether on the head, and its distance.</summary>
    public static (bool Head, float Distance)? OrbitOf(object panel)
    {
        if (EditorField?.GetValue(panel) is not { } session || OrbitProperty?.GetValue(session) is not { } orbit) return null;

        if (HeadProperty?.GetValue(orbit) is not bool head || DistanceProperty?.GetValue(orbit) is not float distance) return null;

        return (head, distance);
    }

    public static void SetDistance(object panel, float distance)
    {
        if (EditorField?.GetValue(panel) is { } session && OrbitProperty?.GetValue(session) is { } orbit)
        {
            SetDistanceMethod?.Invoke(orbit, [distance]);
        }
    }

    /// <summary>Presses CONFIRM as asked already (no question about characters never shown).</summary>
    public static void Confirm()
    {
        if (s_panel is { } panel) ConfirmEditor?.Invoke(panel, [true]);
    }
}
