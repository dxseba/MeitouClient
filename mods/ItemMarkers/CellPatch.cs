using HarmonyLib;
using Meitou.Data.Fcs;
using Meitou.Data.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.ItemMarkers;

/// <summary>
/// Puts the marks on item icons. The original adds them once to each <c>InventoryIcon</c> as the game makes it (inventory, bags, limbs, loot,
/// trader, containers, equipment) and its grade marks lie beneath the picture. Meitou's screen-control API builds per item cell only for character
/// windows and draws over the picture, so a Harmony postfix on the internal <c>Meitou.Game.Ui.ItemCells.AddItemCell</c> (every item cell of every
/// grid, the held item's too; host thread, each time a screen is arranged) inserts the grade marks right after the cell's background (under its
/// icon) and the badges after its controls. A failure is logged once and leaves the marks off for the session.
/// </summary>
static class CellPatch
{
    const int LoggedItems = 200;

    static MarkerSettings s_settings = new();
    static CellFacts? s_facts;
    static Grades? s_grades;
    static MarkLayout? s_layout;
    static Func<bool> s_shown = () => true;
    static Action<string>? s_log;
    static bool s_failed;
    static int s_logged;
    static readonly HashSet<string> LoggedRecords = new(StringComparer.Ordinal);

    /// <summary>The snapshot on screen and the interface scale, set once a frame (<see cref="ItemMarkersMod"/>).</summary>
    public static volatile WorldSnapshot? Snapshot;
    public static float UiScale = 1;

    public static void Install(ModContext context, MarkerSettings settings, CellFacts facts, Grades grades, MarkPictures pictures, Func<bool> shown)
    {
        (s_settings, s_facts, s_grades, s_shown, s_log) = (settings, facts, grades, shown, context.Log);
        s_layout = new MarkLayout(settings, pictures);
        var target = AccessTools.Method("Meitou.Game.Ui.ItemCells:AddItemCell");
        if (target is null)
        {
            context.Log("ItemCells.AddItemCell not found: no marks");
            return;
        }

        context.Harmony.Patch(target, postfix: new HarmonyMethod(typeof(CellPatch), nameof(Postfix)));
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void Postfix(List<UiControl> controls, string name, UiRect area, object? tip, UiRect? clip)
    {
        if (s_failed || tip is null || controls is null || !s_settings.Enabled || !s_shown()) return;

        try
        {
            Mark(controls, name, area, tip, clip);
        }
        catch (Exception e)
        {
            s_failed = true;
            s_log?.Invoke($"marking failed, marks left off for the session: {e.Message}");
        }
    }
#pragma warning restore IDE1006

    static void Mark(List<UiControl> controls, string name, UiRect area, object tip, UiRect? clip)
    {
        var snapshot = Snapshot;
        var item = s_facts!.Of(tip, snapshot);
        var grade = s_grades!.Of(item);
        bool eye = s_settings.Blueprint && s_facts.LearnedBlueprint(item, snapshot) || s_settings.MapsKnown && s_facts.KnownMap(item, snapshot);

        var layout = s_layout!;
        layout.Build(name, area, clip, UiScale, item, grade, eye);
        if (layout.Under.Count > 0) controls.InsertRange(UnderAt(controls, name), layout.Under);
        controls.AddRange(layout.Over);

        if (s_settings.Debug) LogItem(item, grade, layout.Under.Count + layout.Over.Count);
    }

    /// <summary>Right after the cell's background (under its icon, charge bar and text), among the controls the cell just added.</summary>
    static int UnderAt(List<UiControl> controls, string name)
    {
        string background = name + "-background";
        for (int i = controls.Count - 1; i >= Math.Max(0, controls.Count - 8); i--)
        {
            if (controls[i].Name == background) return i + 1;
        }

        return controls.Count;
    }

    /// <summary>The first <see cref="LoggedItems"/> marked records: kind, level, grade, stolen, uniform and the number of marks.</summary>
    static void LogItem(in ItemFacts item, ItemGrade? grade, int marks)
    {
        if (marks == 0 || s_logged >= LoggedItems || !LoggedRecords.Add(item.Record)) return;

        s_logged++;
        FcsRecordType? type = s_grades?.TypeOf(item.Record);
        s_log?.Invoke($"'{item.Record}' type {(int?)type ?? -1} level {item.Level} -> grade {grade?.Rank ?? -1}, stolen {(item.Stolen ? 1 : 0)}, "
            + $"uniform {(item.Uniform ? 1 : 0)}, marks {marks}");
    }
}
