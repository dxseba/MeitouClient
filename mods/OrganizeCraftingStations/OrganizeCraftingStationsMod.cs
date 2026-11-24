using Meitou.Modding;

namespace Meitou.OrganizeCraftingStations;

/// <summary>
/// Port of Organize the Crafting Stations (MCM fork, workshop 3816121254, of Emkej8's original 3688602277; GPLv3): a search panel on the overview's
/// Research and Crafting tabs that filters the blueprint and recipe rows by name, the matches laid out from the top without gaps
/// (<see cref="ListFilter"/>), with a "shown / listed" counter, a clear button and a draggable, remembered place (<see cref="SearchBar"/>).
/// </summary>
public sealed class OrganizeCraftingStationsMod : IMod
{
    public void Initialize(ModContext context)
    {
        // The settings' labels and hints and the "Search items..." hint in the game's language, from the installed fork's catalogues.
        if (OriginalFolder.Find(context) is { } original) context.AddTranslations("organize_the_crafting_stations", original);

        var settings = new CraftingSettings();
        var group = context.AddSettings(settings);
        var bar = new SearchBar(context, settings, group);
        ListFilter.Active = () => settings.Enabled && bar.ControlsOn;
        if (!ListFilter.Install(context)) context.LogError("the search panel shows, but some rows are not filtered");

        context.AddFrame(_ => bar.Frame());
        context.AddUi(bar);
        context.Log("Organize the Crafting Stations initialized");
    }
}
