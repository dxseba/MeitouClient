using System.Globalization;
using Meitou.Localisation;

namespace Meitou.CityBuilder;

/// <summary>
/// The CITY buildings', templates' and factions' names in the game's language, as City.dll renames City.mod's records at start for Russian (and
/// its zh_CN.txt for Chinese): a building's name is its kind, level and people, then its own name ("Guard Post II (Swamp): Swamp Dome"); its
/// description the kind's text with the capacity, beds and seats put in, then its people's line; the residents', guards', shops' and innkeeper's
/// templates and the two factions by their English names. Display only (a Shown text source); in English nothing changes.
/// </summary>
internal sealed class CityNames(CityCatalog catalog, Func<string, string> tr)
{
    const string Home = "A home for independent settlers. Some time after it is built, 1-3 people move in and offer to buy it. Residents do not "
        + "obey you: they live their own lives in your settlement.";

    const string Trade = "A trade house. After it is built, a trader (weapons, armour, prosthetics, food and more) moves in and offers to buy the "
        + "building, then trades with you like in any town.";

    const string Post = "A guard post for up to {a} guards. Hire them in the Settlement window (the CTY button on the bottom panel or Ctrl+Shift+H). "
        + "They man the turrets, guard the gates, patrol the settlement and stand by its houses, never harm your people and never leave its walls.";

    const string Inn = "A tavern with {a} beds and {b} seats. An innkeeper runs it; residents, guests and travellers stay the night for 100 cats, "
        + "and 30% of it is yours.";

    public string? Text(ShownTextContext c)
    {
        if (c.Kind != ShownTextKind.Record || !c.Key.EndsWith("-City.mod", StringComparison.Ordinal)) return null;

        string? shown = catalog.Find(c.Key) is { } b
            ? c.Field == "name" ? Title(b, tr) : string.Equals(c.Field, "description", StringComparison.OrdinalIgnoreCase) ? Description(b) : null
            : c.Field == "name" ? tr(c.Text) : null;
        return shown is null || shown == c.Text ? null : shown;
    }

    string Description(CityBuilding b)
    {
        string text = b.Kind switch
        {
            CityKind.Residence => tr(Home),
            CityKind.TradeHouse => tr(Trade),
            CityKind.GuardPost => tr(Post).Replace("{a}", b.GuardCapacity.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal),
            _ => tr(Inn).Replace("{a}", b.Tavern.Beds.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{b}", b.Tavern.Seats.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal),
        };
        return text + b.People switch
        {
            CityPeople.Hive => tr(" Only the Hive lives and works here: worker drones at home, princes behind the counter, soldier drones on guard."),
            CityPeople.Skeletons => tr(" Only skeletons live and work here: residents, traders and guards are all robots."),
            CityPeople.Swamp => tr(" Only people live and work here."),
            _ => "",
        };
    }

    /// <summary>The original's building title: kind, level, people, then the building's own name.</summary>
    public static string Title(CityBuilding b, Func<string, string> tr)
    {
        string level = b.Level switch { 1 => "I", 2 => "II", 3 => "III", 4 => "IV", _ => "" };
        string kind = b.Kind switch
        {
            CityKind.Residence => tr("Residence"),
            CityKind.TradeHouse => tr("Trade House"),
            CityKind.GuardPost => tr("Guard Post ") + level,
            _ => tr("Tavern ") + level,
        };
        string people = b.People switch
        {
            CityPeople.Hive => tr(" (Hive)"),
            CityPeople.Skeletons => tr(" (Skeletons)"),
            CityPeople.Swamp => tr(" (Swamp)"),
            _ => "",
        };
        return kind + people + ": " + tr(b.Name);
    }
}
