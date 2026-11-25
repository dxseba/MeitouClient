using Meitou.Simulation;

namespace Meitou.CityBuilder;

/// <summary>Where a CITY building stands in its life.</summary>
internal enum PlaceState
{
    /// <summary>Furnished, waiting for its settlers, trader or innkeeper.</summary>
    Waiting,

    /// <summary>Buyers came and offer to buy it; the player sells or refuses.</summary>
    Offer,

    /// <summary>Sold: lived in, the shop open, or (a guard post, a tavern) running.</summary>
    Open,
}

/// <summary>
/// One CITY building the mod looks after, by its site id (kept across saves). Saved and hashed.
/// </summary>
internal sealed class CityPlace
{
    public required string Site;
    public required string Record;
    public PlaceState State;

    /// <summary>Game hours at which the buyers (or the innkeeper) come.</summary>
    public double ArriveAt;

    /// <summary>The offer, then what was paid.</summary>
    public long Price;

    /// <summary>A trade house's shop (an offset of <see cref="CityCatalog.ShopKinds"/>).</summary>
    public int Shop;

    /// <summary>The residents' template drawn for a residence.</summary>
    public string Template = "";

    /// <summary>The last game day a shop paid its tax or a tavern its night; -1 none.</summary>
    public int LastPaidDay = -1;

    /// <summary>What it has paid the player so far (tax or share).</summary>
    public long Income;

    /// <summary>A tavern's guests of the last night paid.</summary>
    public int LastGuests;

    /// <summary>The furniture was placed.</summary>
    public bool Furnished;

    /// <summary>The site ids of the furniture and signs the mod placed (taken down when a shop changes kind).</summary>
    public List<string> Pieces = [];

    /// <summary>Guards hired and paid who wait to take up the post until one of the player's people is near it.</summary>
    public int PendingGuards;

    /// <summary>The game day the tavern's guests came for the night; -1 none.</summary>
    public int GuestDay = -1;

    public void Hash(ref StateHasher h)
    {
        h.Add(Rng.StableHash(Site));
        h.Add(Rng.StableHash(Record));
        h.Add((int)State);
        h.Add(ArriveAt);
        h.Add(Price);
        h.Add(Shop);
        h.Add(Rng.StableHash(Template));
        h.Add(LastPaidDay);
        h.Add(Income);
        h.Add(LastGuests);
        h.Add(Furnished);
        h.Add(Pieces.Count);
        foreach (string piece in Pieces) h.Add(Rng.StableHash(piece));
        h.Add(PendingGuards);
        h.Add(GuestDay);
    }
}

/// <summary>Sell the building <paramref name="Site"/> to the buyers who offer for it.</summary>
public sealed record SellPlaceOrder(string Site) : SimCommand, IPausedCommand;

/// <summary>Refuse the offer for <paramref name="Site"/>: others come later.</summary>
public sealed record RefusePlaceOrder(string Site) : SimCommand, IPausedCommand;

/// <summary>Hire <paramref name="Count"/> guards for the guard post <paramref name="Site"/> (0: up to full).</summary>
public sealed record HireGuardsOrder(string Site, int Count) : SimCommand, IPausedCommand;

/// <summary>Make the trade house <paramref name="Site"/> shop <paramref name="Shop"/> (an offset of <see cref="CityCatalog.ShopKinds"/>).</summary>
public sealed record ChooseShopOrder(string Site, int Shop) : SimCommand, IPausedCommand;
