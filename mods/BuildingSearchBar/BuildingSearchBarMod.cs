using System.Reflection;
using HarmonyLib;
using Meitou.Data.Gameplay.Construction;
using Meitou.Game.Building;
using Meitou.Game.Ui;
using Meitou.Modding;

namespace Meitou.BuildingSearchBar;

/// <summary>
/// Port of Building Search Bar (workshop 3786979230, GPLv3; rebuilt from its description): while the build window is open, a search window with a
/// text box shows; the build menu then lists only the unlocked buildings whose name, group, category or string id contains the query. The menu is
/// filtered by postfixes on <c>BuildMenu.Categories</c> and <c>BuildMenu.Groups</c>.
/// </summary>
public sealed class BuildingSearchBarMod : IMod
{
    public void Initialize(ModContext context)
    {
        SearchHooks.Install(context);
        context.AddUi(new SearchWindow());
        context.Log("Building Search Bar initialized");
    }
}

/// <summary>The query, set by the text box on the interface thread and read there by the menu.</summary>
static class Search
{
    /// <summary>The longest query the box takes.</summary>
    public const int MaxLength = 40;

    static volatile string s_query = "";

    public static string Query => s_query;

    /// <summary>Whether the build window is open (polled each frame).</summary>
    public static volatile bool BuildOpen;

    public static void Set(string query) => s_query = query.Length > MaxLength ? query[..MaxLength] : query;

    public static void Clear() => s_query = "";

    public static bool Matches(BuildingBuildData b)
    {
        string q = s_query.Trim();
        return q.Length == 0 || Has(b.Name, q) || Has(b.Group, q) || Has(b.Category, q) || Has(b.Id, q);
    }

    static bool Has(string text, string q) => text.Contains(q, StringComparison.OrdinalIgnoreCase);
}

static class SearchHooks
{
    static PropertyInfo? s_openScreens;
    static BuildMenu? s_menu;
    static IReadOnlySet<string>? s_unlocked;

    public static void Install(ModContext context)
    {
        var player = AccessTools.TypeByName("Meitou.Game.PlayerInterface") ?? throw new TypeLoadException("Meitou.Game.PlayerInterface");
        s_openScreens = AccessTools.Property(player, "OpenScreens") ?? throw new MissingMemberException(player.FullName, "OpenScreens");
        Patch(context, AccessTools.Method(player, "BindUi"), postfix: nameof(AfterBindUi));
        Patch(context, AccessTools.Method(typeof(BuildMenu), nameof(BuildMenu.Categories)), postfix: nameof(AfterCategories));
        Patch(context, AccessTools.Method(typeof(BuildMenu), nameof(BuildMenu.Groups)), postfix: nameof(AfterGroups));
    }

    static void Patch(ModContext context, MethodInfo? target, string? prefix = null, string? postfix = null)
    {
        if (target is null) throw new MissingMethodException(prefix ?? postfix);

        context.Harmony.Patch(target, prefix: prefix is null ? null : new HarmonyMethod(typeof(SearchHooks), prefix),
            postfix: postfix is null ? null : new HarmonyMethod(typeof(SearchHooks), postfix));
    }

    #pragma warning disable IDE1006 // Harmony binds __instance / __result by name
    /// <summary>Once a frame: notes whether the build window is open; closing it clears the query.</summary>
    static void AfterBindUi(object __instance)
    {
        bool open = s_openScreens?.GetValue(__instance) is IReadOnlyList<string> screens && screens.Contains("build");
        if (!open && Search.BuildOpen) Search.Clear();
        Search.BuildOpen = open;
    }

    static void AfterCategories(BuildMenu __instance, IReadOnlySet<string> unlocked, ref IReadOnlyList<string> __result)
    {
        (s_menu, s_unlocked) = (__instance, unlocked);
        if (Search.Query.Trim().Length == 0) return;

        var hits = __instance.Catalog.Where(b => unlocked.Contains(b.Id) && Search.Matches(b)).Select(b => b.Category)
            .ToHashSet(StringComparer.Ordinal);
        __result = [.. __result.Where(hits.Contains)];
    }

    static void AfterGroups(ref IReadOnlyList<BuildGroup> __result)
    {
        if (Search.Query.Trim().Length == 0) return;

        __result = [.. __result.Select(g => g with { Members = [.. g.Members.Where(Search.Matches)] }).Where(g => g.Members.Count > 0)];
    }
    #pragma warning restore IDE1006

    /// <summary>How many unlocked buildings match the query (for the label); -1 before the menu was first listed.</summary>
    public static int Hits() => s_menu is { } menu && s_unlocked is { } unlocked
        ? menu.Catalog.Count(b => unlocked.Contains(b.Id) && Search.Matches(b)) : -1;
}

/// <summary>
/// The search window at the screen's top while the build window is open: a caption, the query's text box and how many unlocked buildings match.
/// </summary>
sealed class SearchWindow : IUiExtension
{
    const int Width = 420, Height = 34, Pad = 4;

    string _panel = "", _caption = "", _typed = "", _clear = "";

    public string Id => "dxseba.buildingsearchbar.ui";

    public void Register(UiRegistration registration)
    {
        _panel = registration.RegisterStyle("panel", new() { Skin = "Kenshi_GenericTextBoxFlatSkin" });
        _caption = registration.RegisterStyle("caption", new() { Text = new(.91f, .86f, .74f, 1), TextPadding = new Data.Ui.UiInsets(6, 0, 4, 0) });
        _typed = registration.RegisterAction("typed", c => Search.Set(c.Text));
        _clear = registration.RegisterAction("clear", _ => Search.Clear());
        registration.Contribute(UiSlots.Overlay, Build, order: 61);
    }

    UiNode Build(UiFrameContext frame)
    {
        if (!Search.BuildOpen) return new UiNode("bsb-hidden") { Visible = false };

        var root = new UiNode("bsb") { Placement = UiPlacement.Fill, Pickable = false };
        var window = new UiNode("bsb-window")
        {
            Placement = new(Width, Height, Left: (frame.Viewport.Width - Width) / 2, Top: 8), Style = _panel,
            Flow = UiFlow.Row, Padding = Pad, Gap = Pad,
        };
        AddChildren(window);
        root.Children.Add(window);
        return root;
    }

    void AddChildren(UiNode window)
    {
        int hits = SearchHooks.Hits();
        string found = hits >= 0 && Search.Query.Trim().Length > 0 ? $"{hits} found" : "";
        float row = Height - 2 * Pad;
        window.Children.Add(new UiNode("bsb-caption", UiNodeKind.Label) { Placement = new(70, row), Text = "Search:", Style = _caption });
        window.Children.Add(new UiNode("bsb-query", UiNodeKind.TextBox)
        {
            Placement = new(200, row), Text = Search.Query, Action = _typed, MaxLength = Search.MaxLength,
        });
        window.Children.Add(new UiNode("bsb-found", UiNodeKind.Label) { Placement = new(80, row), Text = found, Style = _caption });
        window.Children.Add(new UiNode("bsb-clear", UiNodeKind.Button)
        {
            Placement = new(row, row), Text = "X", Action = _clear, Enabled = Search.Query.Length > 0,
        });
    }
}
