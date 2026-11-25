using System.Globalization;
using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Population;

namespace Meitou.CityBuilder;

/// <summary>
/// The tavern nights and the morning's money (City.dll's day pass, <b>Verified</b> by the decompile). From 19:00 an open tavern's guests come once a
/// night: a number drawn in [0.4 x beds, beds + 0.99], at least 1 and at most the beds, in groups of 1 to 3 of the Tavern Guests template (340,
/// package "City tavern guest": the bar, then bed from 22 to 7). From 7:00, before 19:00, each tavern pays the night once a day: the guests still
/// within 800 of it when it saw them, else (and for each night missed, up to seven) a draw in [0.3 x beds, 0.75 x beds + 0.99]; the share is
/// guests x price x percent / 100, and the guests leave. Each open shop pays its tax once a day from 7:00. The sums go into the player's money with
/// one notice each.
/// </summary>
internal sealed partial class CitySystem
{
    /// <summary>The Tavern Guests squad template.</summary>
    const string GuestTemplate = "340-City.mod";

    /// <summary>How near a guest must stand to the tavern in the morning to count (City.dll: 800).</summary>
    const float GuestReach = 800;

    void PayMorning(World world)
    {
        double hour = HourOfDay(world.GameHours);
        if (hour < Morning) return;

        int day = Day(world.GameHours);
        if (hour < Evening) PayTaverns(world, day);
        PayTaxes(world, day);
    }

    #region Guests

    /// <summary>The evening: each open tavern's guests come, once a night.</summary>
    void TavernNights(World world)
    {
        if (HourOfDay(world.GameHours) < Evening || world.Systems.OfType<PopulationSystem>().FirstOrDefault() is not { } population) return;

        int day = Day(world.GameHours);
        foreach (var place in _places.Values)
        {
            if (place.State != PlaceState.Open || place.GuestDay == day) continue;
            if (_catalog.Find(place.Record) is not { Kind: CityKind.Tavern } building || world.Buildings.Find(place.Site) is not { } site) continue;

            place.GuestDay = day;
            int beds = building.Tavern.Beds;
            float low = beds * 0.4f;
            int count = Math.Clamp((int)(low + Rng.Float(Roll(world)) * (beds + 0.99f - low)), 1, beds);
            SpawnGuests(world, population, place, site.Position, count);
        }
    }

    void SpawnGuests(World world, PopulationSystem population, CityPlace place, Vector3 at, int count)
    {
        int faction = _population.FactionIndex("200-City.mod");
        int home = NearestSite(at);
        if (faction < 0 || home < 0) return;

        var centre = new Vector2(at.X, at.Z);
        while (count > 0)
        {
            int group = Math.Min(count, Rng.Int(Roll(world), 3) + 1);
            int id = population.SpawnPlatoon(world, faction, GuestTemplate, home, centre, 100, 400, (long)group * _rules.NightPrice, group);
            if (id < 0 || world.Platoons.Find(id) is not { } platoon) return;

            platoon.ModTags[_modId] = place.Site + "|guest";
            PopulationSystem.HoldPlatoon(world, id, _modId, true);
            PopulationSystem.SendPlatoon(world, id, _modId, centre);
            count -= group;
        }
    }

    int NearestSite(Vector3 at)
    {
        int best = -1;
        float bestDistance = float.MaxValue;
        foreach (var site in _population.Sites)
        {
            float d = Vector2.DistanceSquared(new(site.Position.X, site.Position.Z), new(at.X, at.Z));
            if (d < bestDistance) (best, bestDistance) = (site.Index, d);
        }

        return best;
    }

    /// <summary>The tavern's guest platoons, in id order.</summary>
    List<Platoon> GuestsOf(World world, string site) =>
        [.. world.Platoons.All.Where(p => p.ModTags.TryGetValue(_modId, out var t) && t == site + "|guest").OrderBy(p => p.Id)];

    /// <summary>The guests of the tavern's platoons that are loaded, alive and within <see cref="GuestReach"/>; null when none is loaded.</summary>
    int? CountGuests(World world, List<Platoon> platoons, Vector3 at)
    {
        int? counted = null;
        foreach (var platoon in platoons)
        {
            if (world.Squads.Find(platoon.SquadId) is not { } squad) continue;

            counted ??= 0;
            foreach (var member in squad.Members)
            {
                if (!world.Characters.TryResolveNext(member, out int slot) || !world.Characters.Next[slot].Alive) continue;
                if (Vector3.Distance(world.Characters.Next[slot].Position, at) < GuestReach) counted++;
            }
        }

        return counted;
    }

    void SendGuestsAway(World world, List<Platoon> platoons)
    {
        if (world.Systems.OfType<PopulationSystem>().FirstOrDefault() is not { } population) return;

        foreach (var platoon in platoons)
        {
            PopulationSystem.HoldPlatoon(world, platoon.Id, _modId, false);
            if (!population.DespawnPlatoon(world, platoon.Id)) platoon.ModTags.Remove(_modId);
        }
    }

    #endregion

    void PayTaverns(World world, int day)
    {
        long share = 0;
        int guests = 0;
        foreach (var place in _places.Values)
        {
            if (place.State != PlaceState.Open || place.LastPaidDay == day) continue;
            if (_catalog.Find(place.Record) is not { Kind: CityKind.Tavern } building || world.Buildings.Find(place.Site) is not { } site) continue;

            int night = NightGuests(world, place, site.Position, building.Tavern.Beds, day);
            long paid = (long)night * _rules.NightPrice * _rules.SharePercent / 100;
            place.LastPaidDay = day;
            place.LastGuests = night;
            place.Income += paid;
            share += paid;
            guests += night;
        }

        if (share <= 0) return;

        world.Player.Money += share;
        world.Post(_tr("Taverns") + ": " + _tr("Guests last night: ") + guests.ToString(CultureInfo.InvariantCulture) + _tr(". Your share: ")
            + Cats(share) + ".");
    }

    /// <summary>
    /// The guests of the nights since the last payment: last night's counted when the tavern's guests were loaded, the rest drawn; last night's
    /// guests leave.
    /// </summary>
    int NightGuests(World world, CityPlace place, Vector3 at, int beds, int day)
    {
        var platoons = GuestsOf(world, place.Site);
        int? counted = place.GuestDay == day - 1 ? CountGuests(world, platoons, at) : null;
        SendGuestsAway(world, platoons);

        int nights = place.LastPaidDay < 0 ? 1 : Math.Min(day - place.LastPaidDay, 7);
        int night = counted ?? Guests(world, beds);
        for (int i = 1; i < nights; i++) night += Guests(world, beds);
        return night;
    }

    int Guests(World world, int beds)
    {
        float low = beds * 0.3f;
        float high = beds * 0.75f + 0.99f;
        return Math.Min(beds, (int)(low + Rng.Float(Roll(world)) * (high - low)));
    }

    /// <summary>A shop's daily tax: (int)(price x percent / 100 + 0.5) per day, one to seven days.</summary>
    void PayTaxes(World world, int day)
    {
        long tax = 0;
        int shops = 0;
        foreach (var place in _places.Values)
        {
            if (place.State != PlaceState.Open || place.LastPaidDay == day) continue;
            if (_catalog.Find(place.Record) is not { Kind: CityKind.TradeHouse }) continue;

            int days = place.LastPaidDay < 0 ? 1 : Math.Clamp(day - place.LastPaidDay, 1, 7);
            long paid = (long)(place.Price * _rules.TaxPercent / 100.0 + 0.5) * days;
            place.LastPaidDay = day;
            if (paid <= 0) continue;

            place.Income += paid;
            tax += paid;
            shops++;
        }

        if (tax <= 0) return;

        world.Player.Money += tax;
        world.Post(_tr("Shops' tax") + ": " + _tr("Shops: ") + shops.ToString(CultureInfo.InvariantCulture) + _tr(". Tax: ") + Cats(tax) + ".");
    }

    /// <summary>
    /// Residents work at the settlement's machines in the working hours (with Work on); outside them, or with it off, their squad's work building is
    /// their own home, which has no work slot, so its workers do no work (<c>SetSquadWork</c>).
    /// </summary>
    void WorkHours(World world)
    {
        double hour = HourOfDay(world.GameHours);
        bool hours = _rules.WorkFrom <= _rules.WorkTo
            ? hour >= _rules.WorkFrom && hour < _rules.WorkTo
            : hour >= _rules.WorkFrom || hour < _rules.WorkTo;
        foreach (var place in _places.Values)
        {
            if (place.State != PlaceState.Open || Tagged(world, place.Site, "home") is not { } squad) continue;

            string? work = _rules.Work && hours ? null : place.Site;
            if (squad.AssignedWork != work) _enqueue(new SetSquadWork(squad.Id, work) { Tick = world.Tick + 1 });
        }
    }
}
