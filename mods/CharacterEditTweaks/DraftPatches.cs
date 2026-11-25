using HarmonyLib;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Modding;

namespace Meitou.CharacterEditTweaks;

/// <summary>
/// The editor's working look is a <see cref="CharacterDraft"/>: its slider and list setters are patched to feed <see cref="ChangeTracker"/> (the
/// value before in a prefix, after in a postfix), and RESET, RANDOMISE, IMPORT, race and gender changes start the lists over, as the original's
/// hooks on <c>CharacterEditWindow</c>'s update and reset calls do. A fault is logged once; the draft works as without the mod.
/// </summary>
static class DraftPatches
{
    static ModContext? s_context;
    static int s_faulted;

    public static void Install(ModContext context)
    {
        s_context = context;
        var h = context.Harmony;
        var type = typeof(CharacterDraft);
        var control = new[] { typeof(EditorControl), typeof(int) };
        var before = new HarmonyMethod(typeof(DraftPatches), nameof(Before));
        var after = new HarmonyMethod(typeof(DraftPatches), nameof(After));
        h.Patch(AccessTools.Method(type, nameof(CharacterDraft.Set), control), before, after);
        h.Patch(AccessTools.Method(type, nameof(CharacterDraft.Step), control), before, after);

        var clear = new HarmonyMethod(typeof(DraftPatches), nameof(Cleared));
        h.Patch(AccessTools.Method(type, nameof(CharacterDraft.Reset)), postfix: clear);
        h.Patch(AccessTools.Method(type, nameof(CharacterDraft.Import)), postfix: clear);
        h.Patch(AccessTools.Method(type, nameof(CharacterDraft.SetRace)), postfix: clear);
        h.Patch(AccessTools.Method(type, nameof(CharacterDraft.SetFemale)), postfix: clear);
        h.Patch(AccessTools.Method(type, nameof(CharacterDraft.Randomise)), postfix: new HarmonyMethod(typeof(DraftPatches), nameof(Randomised)));
        h.Patch(AccessTools.Constructor(type, [typeof(Data.GameDatabase), typeof(string), typeof(FcsRecord), typeof(string), typeof(Data.GameRecord)]),
            postfix: new HarmonyMethod(typeof(DraftPatches), nameof(Made)));
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void Before(CharacterDraft __instance, EditorControl c, out int __state)
    {
        __state = int.MinValue;
        try
        {
            __state = __instance.Value(c);
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    static void After(CharacterDraft __instance, EditorControl c, int __state)
    {
        try
        {
            if (__state != int.MinValue) ChangeTracker.Changed(__instance, c, __state, __instance.Value(c));
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    static void Cleared(CharacterDraft __instance)
    {
        try
        {
            ChangeTracker.Clear(__instance);
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    static void Randomised(CharacterDraft __instance, EditorTab? tab)
    {
        try
        {
            if (tab is { } one) ChangeTracker.ClearTab(__instance, one);
            else ChangeTracker.Clear(__instance);
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    static void Made(CharacterDraft __instance)
    {
        try
        {
            ChangeTracker.NoteStart(__instance);
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }
#pragma warning restore IDE1006

    static void Fault(Exception e)
    {
        if (Interlocked.Exchange(ref s_faulted, 1) == 0) s_context?.LogError($"tracking an editor change failed, the editor goes on untracked: {e}");
    }
}

/// <summary>Puts a draft's look back (the record's setter is private to the draft).</summary>
static class DraftRecords
{
    static readonly Action<CharacterDraft, FcsRecord>? SetRecord =
        AccessTools.PropertySetter(typeof(CharacterDraft), nameof(CharacterDraft.Record)) is { } setter
            ? AccessTools.MethodDelegate<Action<CharacterDraft, FcsRecord>>(setter)
            : null;

    public static void Restore(CharacterDraft draft, FcsRecord start) => SetRecord?.Invoke(draft, CharacterDraft.Copy(start));
}
