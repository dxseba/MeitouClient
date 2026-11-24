using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Meitou.Modding;
using Meitou.Simulation.Production;

namespace Meitou.OrganizeCraftingStations;

/// <summary>
/// Filters the overview's blueprint and craft rows: a transpiler on <c>ProductionWindows.AddResearchContent</c> and
/// <c>ProductionWindows.AddCraftContent</c> passes the sorted row list each builds (its first <c>Enumerable.ToList</c> of the row type) through
/// <see cref="Research"/> / <see cref="Crafts"/>, so the matching rows are laid out from the top without gaps, scrolled and hovered as the
/// engine's own. Display only: nothing reaches the simulation.
/// </summary>
static class ListFilter
{
    const string Production = "Meitou.Game.Ui.ProductionWindows";

    /// <summary>
    /// The query as typed and normalized (<see cref="SearchText"/>).
    /// </summary>
    public static string Typed { get; private set; } = "";

    static string s_query = "";

    /// <summary>
    /// Whether the filter applies (the mod enabled and its controls on).
    /// </summary>
    public static Func<bool> Active = () => false;

    /// <summary>
    /// The last counts of each tab: rows shown and rows listed.
    /// </summary>
    public static (int Visible, int Total) ResearchCount, CraftCount;

    static ModContext? s_context;
    static int s_faulted;
    static bool s_inserted;

    /// <summary>
    /// Sets the query.
    /// </summary>
    public static void SetQuery(string typed)
    {
        Typed = typed;
        s_query = SearchText.Normalize(typed);
    }

    /// <summary>
    /// Patches both tabs; false (logged) when either method or its row list is not found, leaving that tab unfiltered.
    /// </summary>
    public static bool Install(ModContext context)
    {
        s_context = context;
        bool research = Patch(context, "AddResearchContent", nameof(ResearchTranspiler));
        bool crafts = Patch(context, "AddCraftContent", nameof(CraftTranspiler));
        return research && crafts;
    }

    static bool Patch(ModContext context, string method, string transpiler)
    {
        var type = typeof(Game.Ui.UiHost).Assembly.GetType(Production);
        var target = type is null ? null : AccessTools.Method(type, method);
        if (target is null)
        {
            context.LogError($"{Production}.{method} not found: that tab is not filtered");
            return false;
        }

        s_inserted = false;
        context.Harmony.Patch(target, transpiler: new HarmonyMethod(typeof(ListFilter), transpiler));
        if (!s_inserted) context.LogError($"{Production}.{method}: its row list was not found, that tab is not filtered");

        return s_inserted;
    }

    static IEnumerable<CodeInstruction> ResearchTranspiler(IEnumerable<CodeInstruction> code) =>
        AfterFirstToList(code, typeof(ResearchOption), nameof(Research));

    static IEnumerable<CodeInstruction> CraftTranspiler(IEnumerable<CodeInstruction> code) =>
        AfterFirstToList(code, typeof(CraftOptionView), nameof(Crafts));

    /// <summary>
    /// <paramref name="code"/> with a call of <paramref name="filter"/> after its first <c>Enumerable.ToList&lt;row&gt;</c>.
    /// </summary>
    static IEnumerable<CodeInstruction> AfterFirstToList(IEnumerable<CodeInstruction> code, Type row, string filter)
    {
        var call = AccessTools.Method(typeof(ListFilter), filter);
        foreach (var instruction in code)
        {
            yield return instruction;
            if (s_inserted || !IsToList(instruction, row)) continue;

            s_inserted = true;
            yield return new CodeInstruction(OpCodes.Call, call);
        }
    }

    static bool IsToList(CodeInstruction instruction, Type row) =>
        instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo { Name: "ToList", IsGenericMethod: true } m
        && m.DeclaringType == typeof(Enumerable) && m.GetGenericArguments()[0] == row;

    /// <summary>
    /// The research tab's rows that match.
    /// </summary>
    public static List<ResearchOption> Research(List<ResearchOption> rows)
    {
        var shown = Filter(rows, r => r.Name);
        ResearchCount = (shown.Count, rows.Count);
        return shown;
    }

    /// <summary>
    /// The craft tab's rows that match.
    /// </summary>
    public static List<CraftOptionView> Crafts(List<CraftOptionView> rows)
    {
        var shown = Filter(rows, r => r.Name);
        CraftCount = (shown.Count, rows.Count);
        return shown;
    }

    /// <summary>
    /// The rows whose name matches, in their order; all of them while the filter is off, the query empty, or after a fault (logged once).
    /// </summary>
    static List<T> Filter<T>(List<T> rows, Func<T, string> name)
    {
        try
        {
            if (rows is null || s_query.Length == 0 || !Active()) return rows!;

            return rows.FindAll(r => SearchText.Matches(name(r), s_query));
        }
        catch (Exception e)
        {
            if (Interlocked.Exchange(ref s_faulted, 1) == 0) s_context?.LogError($"filtering the rows failed, all rows shown: {e}");
            return rows;
        }
    }
}
