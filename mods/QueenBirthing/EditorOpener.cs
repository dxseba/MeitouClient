using System.Collections.Concurrent;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.QueenBirthing;

/// <summary>
/// Opens the character editor on a newborn, as the original does after a birth (its <c>activateCharacterEditMode</c> one frame later): once a
/// frame (<c>ModContext.AddFrame</c>) the head of the queue is opened as a recruit's edit (<c>ModInterface.OpenScreen</c>,
/// <c>ModScreen.CharacterEditor</c>: free, race and gender kept). The newborn reaches the interface's snapshot a frame or more after its tick, and
/// the open answers false until then, so it is retried for a while.
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
    /// Frames the head of the queue has been tried.
    /// </summary>
    static int s_attempts;

    /// <summary>
    /// Registers the per-frame open.
    /// </summary>
    public static void Install(ModContext context) => context.AddFrame(_ => Open(context.Interface));

    /// <summary>
    /// Queues <paramref name="newborn"/> for the editor (from the tick; the tick never reads it back).
    /// </summary>
    public static void Request(CharacterId newborn) => Pending.Enqueue(newborn);

    /// <summary>
    /// Tries the head of the queue; drops it once opened or after <see cref="MaxAttempts"/> frames.
    /// </summary>
    static void Open(ModInterface screens)
    {
        if (!Pending.TryPeek(out var newborn)) return;

        bool opened = screens.OpenScreen(ModScreen.CharacterEditor, ScreenTarget.Of(newborn));
        if (!opened && ++s_attempts < MaxAttempts) return;

        Pending.TryDequeue(out _);
        s_attempts = 0;
    }
}
