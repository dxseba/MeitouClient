using Meitou.Simulation;

namespace Meitou.CityBuilder;

/// <summary>One building as the window shows it.</summary>
internal sealed record PlaceView(string Site, string Title, CityKind Kind, PlaceState State, long Price, int Shop, double HoursLeft,
    string People, int Guards, int Capacity, int Beds, int Seats, long Income, int LastGuests, int Pending);

/// <summary>
/// What the settlement window shows: the buildings in site order, and a counter that grows with each new offer (the window opens on it).
/// </summary>
internal sealed record CityView(IReadOnlyList<PlaceView> Places, int OfferSerial)
{
    public static readonly CityView Empty = new([], 0);

    public PlaceView? Find(string? site) => site is null ? null : Places.FirstOrDefault(p => p.Site == site);
}

internal sealed partial class CitySystem
{
    /// <summary>Counts the offers made this session, so the window opens on a new one.</summary>
    int _offerSerial;

    void Publish(World world)
    {
        var list = new List<PlaceView>();
        foreach (var place in _places.Values)
        {
            if (_catalog.Find(place.Record) is not { } b) continue;

            var squads = SquadsOf(world, place.Site, null);
            string people = string.Join(", ", squads.SelectMany(s => s.Members).Select(world.Characters.NameOf).Where(n => n.Length > 0));
            int guards = b.Kind == CityKind.GuardPost ? squads.Sum(s => Living(world, s)) : 0;
            list.Add(new PlaceView(place.Site, Title(b), b.Kind, place.State, place.Price, place.Shop, Math.Max(0, place.ArriveAt - world.GameHours),
                people, guards, b.GuardCapacity, b.Tavern.Beds, b.Tavern.Seats, place.Income, place.LastGuests, place.PendingGuards));
        }

        View = new CityView(list, _offerSerial);
    }

    string Title(CityBuilding b) => CityNames.Title(b, _tr);
}
