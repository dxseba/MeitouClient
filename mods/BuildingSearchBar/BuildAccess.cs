using System.Collections;
using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Data.Gameplay.Construction;
using Meitou.Game.Building;

namespace Meitou.BuildingSearchBar;

/// <summary>
/// The build window's state through reflection (the engine's <c>BuildMode</c> is internal): its categories and groups as the window lists them, and
/// the choice the lists show, set as clicks on the lists set it. Interface thread only.
/// </summary>
static class BuildAccess
{
    /// <summary>
    /// The engine's <c>PlayerInterface</c>.
    /// </summary>
    public static readonly Type PlayerType = AccessTools.TypeByName("Meitou.Game.PlayerInterface") ?? throw new TypeLoadException("PlayerInterface");

    /// <summary>
    /// <c>PlayerInterface.Build</c>.
    /// </summary>
    static PropertyInfo s_build = null!;

    /// <summary>
    /// <c>PlayerInterface.FaithfulScreens</c> (a field).
    /// </summary>
    static FieldInfo s_screens = null!;

    /// <summary>
    /// <c>BuildMode.Unlocked</c>, <c>BuildCategories</c>, <c>FollowChoiceInLists</c> and the screens' <c>IsOpen</c>.
    /// </summary>
    static MethodInfo s_unlocked = null!, s_categories = null!, s_follow = null!, s_isOpen = null!;

    /// <summary>
    /// <c>BuildMode</c>'s services and the lists' choice.
    /// </summary>
    static FieldInfo s_services = null!, s_category = null!, s_group = null!, s_member = null!, s_discarded = null!;

    /// <summary>
    /// Resolves the members; throws when the engine no longer has one (the mod is then refused).
    /// </summary>
    public static void Bind()
    {
        s_build = Need(AccessTools.Property(PlayerType, "Build"), "Build");
        s_screens = Need(AccessTools.Field(PlayerType, "FaithfulScreens"), "FaithfulScreens");
        var mode = s_build.PropertyType;
        s_unlocked = Need(AccessTools.Method(mode, "Unlocked"), "Unlocked");
        s_categories = Need(AccessTools.Method(mode, "BuildCategories"), "BuildCategories");
        s_follow = Need(AccessTools.Method(mode, "FollowChoiceInLists"), "FollowChoiceInLists");
        s_services = Need(AccessTools.Field(mode, "Services"), "Services");
        s_category = Need(AccessTools.Field(mode, "_buildCategory"), "_buildCategory");
        s_group = Need(AccessTools.Field(mode, "_buildGroup"), "_buildGroup");
        s_member = Need(AccessTools.Field(mode, "BuildMember"), "BuildMember");
        s_discarded = Need(AccessTools.Field(mode, "BuildDiscarded"), "BuildDiscarded");
        s_isOpen = Need(AccessTools.Method(s_screens.FieldType, "IsOpen", [typeof(string)]), "IsOpen");
    }

    /// <summary>
    /// The member, else a throw naming it.
    /// </summary>
    static T Need<T>(T? member, string name) where T : MemberInfo => member ?? throw new MissingMemberException(PlayerType.Name, name);

    /// <summary>
    /// Whether the build window is open.
    /// </summary>
    public static bool BuildOpen(object player) => s_isOpen.Invoke(s_screens.GetValue(player), ["build"]) is true;

    /// <summary>
    /// The window's entries in its own order: each listed category, its groups, each group's members.
    /// </summary>
    public static List<SearchEntry> Entries(object player)
    {
        var mode = s_build.GetValue(player)!;
        if (s_services.GetValue(mode) is not BuildServices services) return [];

        var unlocked = (IReadOnlySet<string>)s_unlocked.Invoke(mode, [])!;
        var list = new List<SearchEntry>();
        foreach (var category in (IReadOnlyList<string>)s_categories.Invoke(mode, [])!)
        {
            var groups = services.Menu.Groups(category, unlocked);
            for (int g = 0; g < groups.Count; g++)
            {
                foreach (var member in groups[g].Members) list.Add(new(member, category, groups[g].Name, g));
            }
        }

        return list;
    }

    /// <summary>
    /// Chooses the entry's building in the lists, as a click on its category, its group and then its variant.
    /// </summary>
    public static void Choose(object player, SearchEntry entry)
    {
        var mode = s_build.GetValue(player)!;
        s_category.SetValue(mode, entry.Category);
        s_discarded.SetValue(mode, false);
        s_group.SetValue(mode, entry.GroupIndex);
        s_member.SetValue(mode, entry.Building);
        s_follow.Invoke(mode, [true]);
    }

    /// <summary>
    /// The build window's top-left on screen as last placed; null when it cannot be read.
    /// </summary>
    public static Vector2? BuildWindowOrigin(object player)
    {
        var screens = Traverse.Create(s_screens.GetValue(player)).Field("_screens").GetValue();
        if (screens is not IDictionary map || !map.Contains("build")) return null;

        return Traverse.Create(map["build"]).Field("Placed").GetValue() is Vector2 placed && placed != Vector2.Zero ? placed : null;
    }
}

/// <summary>
/// One building of the build window: its category, its group's name and the group's index in that category's list.
/// </summary>
sealed record SearchEntry(BuildingBuildData Building, string Category, string Group, int GroupIndex)
{
    /// <summary>
    /// The result row's text: the name, then the group in brackets when it has one other than the name.
    /// </summary>
    public string Caption => Group.Length > 0 && Group != Building.Name ? $"{Building.Name} [{Group}]" : Building.Name;

    /// <summary>
    /// Whether the query is in the name, group, category or string id, ignoring case.
    /// </summary>
    public bool Matches(string query) => Has(Building.Name, query) || Has(Group, query) || Has(Category, query) || Has(Building.Id, query);

    /// <summary>
    /// Whether <paramref name="text"/> contains the query, ignoring case.
    /// </summary>
    static bool Has(string text, string query) => text.Contains(query, StringComparison.OrdinalIgnoreCase);
}
