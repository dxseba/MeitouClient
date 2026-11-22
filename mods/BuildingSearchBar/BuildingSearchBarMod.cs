using HarmonyLib;
using Meitou.Modding;

namespace Meitou.BuildingSearchBar;

/// <summary>
/// Port of Building Search Bar (workshop 3786979230, GPLv3): while the build window is open (also the Shift+F12 level build), a SEARCH window
/// lists the buildings on offer whose name, group, category or string id contains the query; clicking a result, or Next, chooses that building in
/// the build window's lists. The build window's own lists are not changed.
/// </summary>
public sealed class BuildingSearchBarMod : IMod
{
    public void Initialize(ModContext context)
    {
        BuildAccess.Bind();
        var bindUi = AccessTools.Method(BuildAccess.PlayerType, "BindUi") ?? throw new MissingMethodException(BuildAccess.PlayerType.FullName, "BindUi");
        context.Harmony.Patch(bindUi, postfix: new HarmonyMethod(typeof(BuildingSearchBarMod), nameof(AfterBindUi)));
        context.AddUi(new SearchWindow());
        context.Log("Building Search Bar initialized");
    }

    #pragma warning disable IDE1006 // Harmony binds __instance by name
    /// <summary>
    /// Once a frame on the interface thread: notes the interface and whether the build window is open; closing it clears the search.
    /// </summary>
    static void AfterBindUi(object __instance) => Search.Frame(__instance, BuildAccess.BuildOpen(__instance));
    #pragma warning restore IDE1006
}
