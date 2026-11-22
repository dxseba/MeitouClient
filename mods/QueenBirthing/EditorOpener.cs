using System.Collections.Concurrent;
using System.Reflection;
using HarmonyLib;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Editing;

namespace Meitou.QueenBirthing;

/// <summary>
/// Opens the character editor on a newborn, as the original does after a birth (its <c>activateCharacterEditMode</c> one frame later): a postfix
/// on the private <c>PlayerInterface.BindUi</c> (once a frame on the interface's thread) opens the game's editor as for a recruit's edit
/// (<c>EditKind.Recruit</c>: free, race and gender kept). The newborn reaches the interface's snapshot a frame or more after its tick, so the
/// open is retried for a while.
/// </summary>
static class EditorOpener
{
    /// <summary>
    /// Frames an open is retried before the newborn is given up (about two seconds).
    /// </summary>
    const int MaxAttempts = 120;

    /// <summary>
    /// Newborns waiting for the editor, handed over by the tick.
    /// </summary>
    static readonly ConcurrentQueue<CharacterId> Pending = new();

    /// <summary>
    /// The interface's <c>CharacterEditor</c> property.
    /// </summary>
    static PropertyInfo? s_editorProperty;

    /// <summary>
    /// The editor panel's open call.
    /// </summary>
    static MethodInfo? s_openMethod;

    /// <summary>
    /// Frames the head of the queue has been tried.
    /// </summary>
    static int s_attempts;

    /// <summary>
    /// Patches the interface; logs and leaves the editor closed when the game's members are not found.
    /// </summary>
    public static void Install(ModContext context)
    {
        var type = AccessTools.TypeByName("Meitou.Game.PlayerInterface");
        s_editorProperty = type is null ? null : AccessTools.Property(type, "CharacterEditor");
        s_openMethod = s_editorProperty is null ? null : AccessTools.Method(s_editorProperty.PropertyType, "OpenEditor");
        if (type is null || s_openMethod is null)
        {
            context.Log("Queen Birthing: the character editor was not found; newborns are not edited");
            return;
        }

        var bind = AccessTools.Method(type, "BindUi") ?? throw new MissingMethodException(type.FullName, "BindUi");
        context.Harmony.Patch(bind, postfix: new HarmonyMethod(typeof(EditorOpener), nameof(Bound)));
    }

    /// <summary>
    /// Queues <paramref name="newborn"/> for the editor (from the tick; the tick never reads it back).
    /// </summary>
    public static void Request(CharacterId newborn) => Pending.Enqueue(newborn);

    #pragma warning disable IDE1006 // Harmony binds __instance by name
    static void Bound(object __instance)
    #pragma warning restore IDE1006
    {
        if (!Pending.TryPeek(out var newborn)) return;
        if (s_editorProperty?.GetValue(__instance) is not { } editor) return;

        object?[] arguments = [EditKind.Recruit, new[] { newborn }, default(CharacterId), false];
        bool opened = s_openMethod!.Invoke(editor, arguments) is true;
        if (!opened && ++s_attempts < MaxAttempts) return;

        Pending.TryDequeue(out _);
        s_attempts = 0;
    }
}
