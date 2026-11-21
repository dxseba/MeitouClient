using System.Reflection;
using HarmonyLib;
using Meitou.Data.Gameplay.Construction;
using Meitou.Game.Building;
using Meitou.Game.Host.Input;
using Meitou.Game.Ui;
using Meitou.Modding;

namespace Meitou.BuildingSearchBar;

/// <summary>
/// Port of Building Search Bar (workshop 3786979230, GPLv3; rebuilt from its description): while the build window is open, the "Search buildings"
/// key action starts typing a query; the build menu then lists only the unlocked buildings whose name, group, category or string id contains it.
/// Typing is taken before the game's keys (a prefix on <c>PlayerInterface.UiKey</c>); the menu is filtered by postfixes on
/// <c>BuildMenu.Categories</c> and <c>BuildMenu.Groups</c>.
/// </summary>
public sealed class BuildingSearchBarMod : IMod
{
    public void Initialize(ModContext context)
    {
        context.AddKeyAction("search", "Search buildings", _ => Search.Start(), Key.N);
        SearchHooks.Install(context);
        context.AddUi(new SearchLabel());
        context.Log("Building Search Bar initialized");
    }
}

/// <summary>The query, typed on the interface thread and read there by the menu.</summary>
static class Search
{
    static volatile string s_query = "";

    public static string Query => s_query;

    /// <summary>Whether keys go to the query now.</summary>
    public static volatile bool Typing;

    /// <summary>Whether the build window is open (polled each frame).</summary>
    public static volatile bool BuildOpen;

    public static void Start()
    {
        if (BuildOpen) Typing = true;
    }

    public static void Clear()
    {
        s_query = "";
        Typing = false;
    }

    /// <summary>A key while typing: letters, digits, space and dash add, Backspace removes, Enter ends, Escape clears; true when taken.</summary>
    public static bool Key(Key key, bool shift)
    {
        switch (key)
        {
            case Game.Host.Input.Key.Escape:
                Clear();
                return true;
            case Game.Host.Input.Key.Enter:
                Typing = false;
                return true;
            case Game.Host.Input.Key.Backspace:
                if (s_query.Length > 0) s_query = s_query[..^1];
                return true;
        }

        if (CharOf(key) is { } c && s_query.Length < 40) s_query += c;
        return true;
    }

    static char? CharOf(Key key)
    {
        string name = key.ToString();
        if (name.Length == 1 && char.IsAsciiLetter(name[0])) return char.ToLowerInvariant(name[0]);
        if (name.Length == 2 && name[0] == 'D' && char.IsAsciiDigit(name[1])) return name[1];
        return key switch { Game.Host.Input.Key.Space => ' ', Game.Host.Input.Key.Minus => '-', _ => null };
    }

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
        Patch(context, AccessTools.Method(player, "UiKey"), prefix: nameof(BeforeUiKey));
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
    static bool BeforeUiKey(Key key, bool shift, ref bool __result)
    {
        if (!Search.Typing || !Search.BuildOpen) return true;

        __result = Search.Key(key, shift);
        return false;
    }

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

/// <summary>The query line over the screen's top while the build window is open and a query is typed.</summary>
sealed class SearchLabel : IUiExtension
{
    string _style = "";

    public string Id => "dxseba.buildingsearchbar.ui";

    public void Register(UiRegistration registration)
    {
        _style = registration.RegisterStyle("label", new()
        {
            Background = new(.10f, .085f, .07f, .92f), Text = new(.91f, .86f, .74f, 1), TextPadding = new Data.Ui.UiInsets(8, 2, 8, 2),
        });
        registration.Contribute(UiSlots.Overlay, Build, order: 61);
    }

    UiNode Build(UiFrameContext frame)
    {
        if (!Search.BuildOpen || !Search.Typing && Search.Query.Length == 0) return new UiNode("bsb-hidden") { Visible = false };

        int hits = SearchHooks.Hits();
        string text = $"Search buildings: {Search.Query}{(Search.Typing ? "_" : "")}" + (hits >= 0 ? $"   ({hits} found)" : "")
            + (Search.Typing ? "   Enter: done, Esc: clear" : "");
        var root = new UiNode("bsb") { Placement = UiPlacement.Fill, Pickable = false };
        root.Children.Add(new UiNode("bsb-label", UiNodeKind.Label)
        {
            Placement = new(520, 26, Left: (frame.Viewport.Width - 520) / 2, Top: 8), Text = text, Style = _style,
        });
        return root;
    }
}
