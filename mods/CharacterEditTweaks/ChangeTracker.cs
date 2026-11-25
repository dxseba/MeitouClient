using System.Globalization;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;

namespace Meitou.CharacterEditTweaks;

/// <summary>One tracked slider or list change: the control, its value before and after, the tab it is on and when it was last merged.</summary>
sealed class Change(EditorControl control, EditorTab tab, int old, int now, long time)
{
    public EditorControl Control { get; } = control;
    public EditorTab Tab { get; } = tab;
    public int Old { get; set; } = old;
    public int New { get; set; } = now;
    public long Time { get; set; } = time;

    /// <summary>True while it sits on the undo list (undoing it sets <see cref="Old"/>); false on the redo list.</summary>
    public bool IsUndo { get; set; } = true;

    /// <summary>The list's line, as the original's: <c>[name] old >> new</c>, a list's choices counted from 1.</summary>
    public string Text(Func<string, string> tr)
    {
        int shift = Control.IsPicker ? 1 : 0;
        string name = tr(Name(Control));
        return string.Create(CultureInfo.InvariantCulture, $"[{name}] {Old + shift} >> {New + shift}");
    }

    static string Name(EditorControl c) => c.Kind switch
    {
        EditorControlKind.Head => "Face",
        EditorControlKind.HairStyle => "Hair",
        EditorControlKind.Beard => "Beard",
        EditorControlKind.HairHue => "Hair Colour",
        EditorControlKind.HairSaturation => "Hair Saturation",
        EditorControlKind.HairBrightness => "Hair Brightness",
        _ => c.Label,
    };
}

/// <summary>
/// The undo and redo lists of the shown character's slider and list changes (BODY, FACE and HAIR tabs), as the original keeps them: at most 128
/// each, newest first; a change of the control on top within 3 s merges into it, and one that puts it back where the entry started removes the
/// entry; a change drops the redo entries of its control. They start over when another character is edited and on RESET, RANDOMISE ALL, IMPORT,
/// a race or gender change; RANDOMISE drops the entries of its tab. Host thread only (the editor's).
/// </summary>
static class ChangeTracker
{
    const int MaxEntries = 128;
    const long MergeMilliseconds = 3000;

    static readonly List<Change> UndoList = [];
    static readonly List<Change> RedoList = [];

    /// <summary>The draft the lists belong to.</summary>
    static CharacterDraft? s_draft;

    /// <summary>The look of each draft when it was made, for Cancel.</summary>
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CharacterDraft, FcsRecord> Starts = new();

    /// <summary>Set while the port itself moves a control (an undo), so the change is not tracked again.</summary>
    [ThreadStatic] static bool s_applying;

    public static IReadOnlyList<Change> Undo => UndoList;
    public static IReadOnlyList<Change> Redo => RedoList;

    public static bool Applying => s_applying;

    public static void NoteStart(CharacterDraft draft) => Starts.AddOrUpdate(draft, CharacterDraft.Copy(draft.Record));

    public static FcsRecord? StartOf(CharacterDraft draft) => Starts.TryGetValue(draft, out var start) ? start : null;

    /// <summary>A control of <paramref name="draft"/> went from <paramref name="old"/> to <paramref name="now"/>.</summary>
    public static void Changed(CharacterDraft draft, EditorControl control, int old, int now)
    {
        if (s_applying || old == now) return;

        Own(draft);
        RedoList.RemoveAll(c => c.Control == control);
        long time = Environment.TickCount64;
        if (UndoList.Count > 0 && UndoList[0].Control == control)
        {
            var top = UndoList[0];
            if (top.Old == now)
            {
                UndoList.RemoveAt(0);
                return;
            }

            if (time - top.Time < MergeMilliseconds)
            {
                (top.New, top.Time) = (now, time);
                return;
            }
        }

        Push(UndoList, new Change(control, TabOf(draft, control), old, now, time));
    }

    /// <summary>Both lists start over (RESET, RANDOMISE ALL, IMPORT, race or gender).</summary>
    public static void Clear(CharacterDraft draft)
    {
        if (s_draft != draft) return;

        UndoList.Clear();
        RedoList.Clear();
    }

    /// <summary>RANDOMISE of a tab: that tab's entries go.</summary>
    public static void ClearTab(CharacterDraft draft, EditorTab tab)
    {
        if (s_draft != draft) return;

        UndoList.RemoveAll(c => c.Tab == tab);
        RedoList.RemoveAll(c => c.Tab == tab);
    }

    /// <summary>Undoes the newest undo entry (or redoes the newest redo entry), moving it to the other list.</summary>
    public static void Step(CharacterDraft draft, bool redo)
    {
        Own(draft);
        var from = redo ? RedoList : UndoList;
        var to = redo ? UndoList : RedoList;
        if (from.Count == 0) return;

        var top = from[0];
        from.RemoveAt(0);
        Apply(draft, top.Control, top.IsUndo ? top.Old : top.New);
        top.IsUndo = !top.IsUndo;
        top.Time = 0;
        Push(to, top);
    }

    /// <summary>Sets a control without tracking it.</summary>
    public static void Apply(CharacterDraft draft, EditorControl control, int value)
    {
        s_applying = true;
        try
        {
            draft.Set(control, value);
        }
        finally
        {
            s_applying = false;
        }
    }

    /// <summary>The lists follow the edited character: another draft starts them over.</summary>
    static void Own(CharacterDraft draft)
    {
        if (s_draft == draft) return;

        s_draft = draft;
        UndoList.Clear();
        RedoList.Clear();
    }

    static void Push(List<Change> list, Change change)
    {
        list.Insert(0, change);
        if (list.Count > MaxEntries) list.RemoveAt(list.Count - 1);
    }

    static EditorTab TabOf(CharacterDraft draft, EditorControl control)
    {
        foreach (var tab in Enum.GetValues<EditorTab>())
        {
            if (draft.Controls(tab).Contains(control)) return tab;
        }

        return EditorTab.Body;
    }
}
