using System.Collections;
using System.Reflection;
using Meitou.Simulation;
using Meitou.Simulation.Locks;

namespace Meitou.Mercenarie;

/// <summary>The player leases (<paramref name="End"/> false) or ends the lease of the town house <paramref name="Building"/>.</summary>
public sealed record LeaseBuilding(string Building, bool End) : SimCommand;

/// <summary>The player buys out the lease of <paramref name="Building"/> (ten weeks of rent).</summary>
public sealed record BuyOutLease(string Building) : SimCommand;

/// <summary>A lease: the house, the owner it goes back to (FACTION string id), the weekly rent, the next due day and the unpaid debt.</summary>
sealed record Lease(string Building, string Owner, int Weekly)
{
    public int NextDue { get; set; }
    public int Debt { get; set; }
    public int DebtSince { get; set; } = -1;
}

/// <summary>
/// The original's real estate (<c>realEstateEnabled</c>): a town house for sale can be leased by the week (the first week paid at signing), bought
/// out for ten weeks of rent, or given back; unpaid rent becomes debt and the lease ends after a week of debt. Ownership moves through the game's
/// own purchase take-over (<c>FixtureRegistry.TakeOver</c>, reflection) without the purchase's emptying and eviction, and goes back by the
/// inverse: the property's owner and purchased flag, the fixtures' owner and the public flag (README "Facts").
/// </summary>
sealed partial class GuildSystem
{
    const int LeaseWeeks = 10, DebtDays = 7;

    static readonly FieldInfo? Properties = typeof(FixtureRegistry).GetField("_properties", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly FieldInfo? OwnedCache = typeof(FixtureRegistry).GetField("_ownedProperties", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly MethodInfo? TakeOver = typeof(FixtureRegistry).GetMethod("TakeOver", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>
    /// The weekly rent of a house for sale (null: not for lease): a tenth of the game's sale price (the original's buy-out is ten weeks).
    /// </summary>
    internal static int? WeeklyRent(World world, string building) =>
        world.Fixtures.SalePrice(building, world.Player.Faction) is long price and > 0 ? (int)Math.Max(1, price / LeaseWeeks) : null;

    void Lease(World world, LeaseBuilding lease)
    {
        if (lease.End)
        {
            if (_state.Leases.FirstOrDefault(l => l.Building == lease.Building) is { } held)
                EndLease(world, held, "Lease terminated. Existing debt remains due.");
            return;
        }

        if (_state.Leases.Any(l => l.Debt > 0))
        {
            world.Post(context.Tr("You must repay your real estate debt before acquiring another building."));
            return;
        }

        StartLease(world, lease.Building);
    }

    void StartLease(World world, string building)
    {
        if (WeeklyRent(world, building) is not { } weekly || Property(world, building) is not { } p || Destroyed(world, p))
        {
            world.Post(context.Tr("Real estate operations are unavailable. No action was completed."));
            return;
        }

        if (world.Player.Money < weekly)
        {
            world.Post(context.Tr("Insufficient funds. No payment was taken."));
            return;
        }

        string owner = FactionId(Owner(p));
        world.Player.Money -= weekly;
        TakeOver!.Invoke(world.Fixtures, [world, p]);
        _state.Leases.Add(new Lease(building, owner, weekly) { NextDue = _day + 7 });
        world.Post(context.Tr("Lease signed. The first full weekly rent was paid."));
    }

    /// <summary>Each day: rents due are paid from the player's Cats, else they become debt; a week of debt ends the lease.</summary>
    void Rents(World world)
    {
        foreach (var lease in _state.Leases.ToList())
        {
            if (_day >= lease.NextDue)
            {
                lease.NextDue += 7;
                lease.Debt += lease.Weekly;
                if (lease.DebtSince < 0) lease.DebtSince = _day;
            }

            if (lease.Debt > 0 && world.Player.Money >= lease.Debt)
            {
                world.Player.Money -= lease.Debt;
                (lease.Debt, lease.DebtSince) = (0, -1);
            }

            if (lease.Debt > 0 && _day - lease.DebtSince >= DebtDays) EndLease(world, lease, "UNPAID RENT: the lease has ended.");
        }
    }

    void BuyOut(World world, BuyOutLease buy)
    {
        if (_state.Leases.FirstOrDefault(l => l.Building == buy.Building) is not { } lease) return;

        long price = (long)lease.Weekly * LeaseWeeks + lease.Debt;
        if (world.Player.Money < price)
        {
            world.Post(context.Tr("Insufficient funds. No payment was taken."));
            return;
        }

        world.Player.Money -= price;
        _state.Leases.Remove(lease);
        world.Post(context.Tr("Building purchase") + $": {price} Cats");
    }

    /// <summary>The house goes back to its owner: the property's owner and purchased flag, its fixtures' owner, open to the public again.</summary>
    void EndLease(World world, Lease lease, string notice)
    {
        _state.Leases.Remove(lease);
        int owner = population.Relations.IndexOf(lease.Owner);
        if (Property(world, lease.Building) is { } p && owner >= 0)
        {
            Set(p, "Owner", owner);
            Set(p, "Purchased", false);
            foreach (var f in world.Fixtures.OfBuilding(lease.Building)) f.Owner = owner;

            OwnedCache?.SetValue(world.Fixtures, null);
            world.Fixtures.SetPublic(lease.Building, true);
        }

        world.Post(context.Tr(notice));
    }

    static object? Property(World world, string building) =>
        Properties?.GetValue(world.Fixtures) is IDictionary d && d.Contains(building) ? d[building] : null;

    static int Owner(object p) => p.GetType().GetProperty("Owner")?.GetValue(p) is int o ? o : -1;

    static bool Destroyed(World world, object p) =>
        p.GetType().GetProperty("Building")?.GetValue(p) is Meitou.Data.World.TownBuilding b && world.Fixtures.IsDestroyed(b);

    static void Set(object p, string name, object value) => p.GetType().GetProperty(name)?.SetValue(p, value);

    string FactionId(int f) => population.Relations.Contains(f) ? population.Relations.Factions[f].Id : "";

    /// <summary>Whether the reflection the leases use is there (a renamed member leaves real estate off, logged).</summary>
    internal static bool EstateAvailable => Properties is not null && OwnedCache is not null && TakeOver is not null;
}
