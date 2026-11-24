using Meitou.Modding;

namespace Meitou.BuildingSearchBar;

/// <summary>
/// Port of Building Search Bar (workshop 3786979230, GPLv3): while the build window is open (also the Shift+F12 level build), a SEARCH window
/// lists the buildings on offer whose name, group, category or string id contains the query; clicking a result, or Next, chooses that building in
/// the build window's lists (<see cref="ModContext.Interface"/>). The build window's own lists are not changed.
/// </summary>
public sealed class BuildingSearchBarMod : IMod
{
    public void Initialize(ModContext context)
    {
        // "SEARCH" and "Search" in the game's language, from the installed original's search_bar catalogues.
        if (OriginalFolder.Find(context) is { } original) context.AddTranslations("search_bar", original);

        Search.Bind(context.Interface);
        context.AddFrame(_ => Search.Frame());
        context.AddUi(new SearchWindow());
        context.Log("Building Search Bar initialized");
    }
}
