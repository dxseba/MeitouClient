using System.Globalization;
using System.Numerics;
using Meitou.Data.Gameplay;
using Meitou.Simulation;

namespace Meitou.DynamicWasteland;

/// <summary>A nation as the panel lists it.</summary>
sealed record NationRow(string Faction, string Name, Vector4 Colour, int Towns, int Military, int Economy, int Stability, int Exhaustion,
    long Treasury, string Wars, string Allies, string Government, string Faith, string Leader);

/// <summary>A town on the political map: where, its marker group, its owner's colour and name, the tooltip and the territory's scale.</summary>
sealed record TownMark(Vector2 At, string Group, Vector4 Colour, string Owner, string Tooltip, float Territory);

/// <summary>A line with an id the panel's buttons act on.</summary>
sealed record Entry(int Id, string Text, bool Active, bool Known);

/// <summary>What the panel and the map show, published by the simulation (read on the host's thread).</summary>
sealed record DwView(IReadOnlyList<NationRow> Nations, IReadOnlyList<string> Wars, IReadOnlyList<string> Pacts, IReadOnlyList<string> News,
    IReadOnlyList<TownMark> Towns, IReadOnlyList<string> Career, string Patron, int Rank, int Merit, int Influence, bool HasCompany,
    IReadOnlyList<Entry> Missions, IReadOnlyList<Entry> People, IReadOnlyList<string> Society);

sealed partial class WorldSim
{
    DwView Publish(World world)
    {
        var nations = _state.Nations
            .Select(n => Row(world, n))
            .OrderByDescending(r => r.Towns)
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .ToList();
        var wars = _state.Wars.Select(w => WarLine(world, w)).ToList();
        var pacts = _state.Alliances.Select(p => Fmt("{0} and {1} swear mutual defence. If one is attacked, the other may join the war.",
                Name(world, p.A), Name(world, p.B)))
            .Concat(_state.Truces.Select(p => tr("Truce with ") + Name(world, p.A) + " / " + Name(world, p.B) + " " +
                Fmt("Truce ({0}d)", (int)Math.Ceiling((p.Until - world.GameHours) / 24))))
            .ToList();
        var news = _state.News.AsEnumerable().Reverse()
            .Select(n => tr("Day") + " " + ((int)(n.Hours / 24) + 1).ToString(CultureInfo.InvariantCulture) + ": " + n.Text)
            .ToList();
        var c = _state.Career;
        return new(nations, wars, pacts, news, Marks(world), CareerLines(world), c.Patron, c.Rank, c.Merit, c.Influence, c.Company >= 0,
            MissionEntries(world), PeopleEntries(world), SocietyLines(world));
    }

    NationRow Row(World world, Nation n)
    {
        var wars = string.Join(", ", _state.Wars.Where(w => w.Involves(n.Faction)).Select(w => Name(world, w.Other(n.Faction))));
        var allies = string.Join(", ", _state.Alliances.Where(p => p.A == n.Faction || p.B == n.Faction)
            .Select(p => Name(world, p.A == n.Faction ? p.B : p.A)));
        return new(n.Faction, Name(world, n.Faction), Colour(n.Faction), TownsOf(world, n.Faction).Count, n.Military, n.Economy, n.Stability,
            n.Exhaustion, n.Treasury, wars, allies, tr(n.Government), FaithName(n.Religion), _state.FindPerson(n.Leader)?.Name ?? "");
    }

    string WarLine(World world, War w)
    {
        int days = (int)((world.GameHours - w.Since) / 24);
        return Fmt("{0} at war with {1}", Name(world, w.A), Name(world, w.B)) + string.Create(CultureInfo.InvariantCulture,
            $"  ({days}d, {w.WinsA}-{w.WinsB}, +{w.TownsA}/+{w.TownsB})");
    }

    List<string> CareerLines(World world)
    {
        var c = _state.Career;
        var lines = new List<string>
        {
            c.Patron == "" ? tr("You serve no nation.") : Fmt("{0} of {1}", tr(Ranks[c.Rank].Title), Name(world, c.Patron)),
            Fmt("Merit {0}  Influence {1}", c.Merit, c.Influence),
        };
        if (c.Faith != "") lines.Add(tr("Faith") + ": " + FaithName(c.Faith));
        if (_state.Companies.FirstOrDefault(x => x.Id == c.Company) is { } company)
            lines.Add(company.Name + ": " + (company.Employer == "" ? tr("free") : Name(world, company.Employer)));
        return lines;
    }

    List<Entry> MissionEntries(World world) => _state.Missions.Select(m => new Entry(m.Id, m.Kind switch
    {
        "bounty" => Fmt("Bounty for {0} near {1}: {2} Cats", Name(world, m.Giver), TownName(world, m.Site), m.Reward),
        "tribute" => Fmt("Tribute to {0}: pay {1} Cats for its favour", Name(world, m.Giver), m.Reward),
        _ => Fmt("Famine relief for {0}: pay {1} Cats", TownName(world, m.Site), m.Reward),
    }, m.State == 1, false)).ToList();

    List<Entry> PeopleEntries(World world) => _state.People
        .Where(p => p.Alive && p.Role is "leader" or "governor" && _state.Find(p.Nation) is not null)
        .OrderBy(p => p.Role == "leader" ? 0 : 1).ThenBy(p => p.Nation, StringComparer.Ordinal).ThenBy(p => p.Id)
        .Select(p => new Entry(p.Id, PersonLine(world, p), p.Bond > 0, p.SecretKnown))
        .ToList();

    string PersonLine(World world, Person p)
    {
        string place = p.Site >= 0 ? TownName(world, p.Site) : Name(world, p.Nation);
        string bond = p.Bond switch { 1 => ", " + tr("friend"), 2 => ", " + tr("dating"), 3 => ", " + tr("spouse"), -1 => ", " + tr("rival"), _ => "" };
        string secret = p.SecretKnown ? " - " + tr(p.Secret) : "";
        string leverage = p.Leverage ? " (" + tr("leverage") + ")" : "";
        return $"{p.Name}, {tr(p.Role)} {place} - {tr("Opinion")} {p.Opinion.ToString(CultureInfo.InvariantCulture)}{bond}{secret}{leverage}";
    }

    List<string> SocietyLines(World world)
    {
        var lines = new List<string>();
        if (_state.Crisis is { } c)
            lines.Add(tr("Crisis") + ": " + Fmt(CrisisTitle(c.Kind), TownName(world, c.Site), c.Nation == "" ? "" : Name(world, c.Nation)));
        lines.AddRange(_state.Faiths.Select(f => Fmt("{0}: {1} towns, authority {2}", f.Name, _state.Towns.Count(t => t.Religion == f.Id),
            f.Authority)));
        lines.AddRange(_state.Companies.Select(m => Fmt("{0}: {1} men, {2}", m.Name, m.Men, m.Employer == "" ? tr("free") : Name(world, m.Employer))));
        lines.Add(Fmt("Caravans on the road: {0}", _state.Caravans.Count));
        lines.AddRange(_state.Towns.Where(t => t.Food == 0 || t.Unrest >= 30).OrderByDescending(t => t.Unrest).ThenBy(t => t.Site).Take(8)
            .Select(t => Fmt("{0}: {1} people, food {2}, unrest {3}", TownName(world, t.Site), t.Population, t.Food, t.Unrest)));
        return lines;
    }

    /// <summary>Every contested town a nation holds, for the political map; its territory scales with the town's radius.</summary>
    List<TownMark> Marks(World world)
    {
        var data = Pop(world)!.Data;
        var marks = new List<TownMark>();
        for (int i = 0; i < data.Sites.Count && i < data.SiteFactions.Count; i++)
        {
            var site = data.Sites[i];
            if (!TownVersions.Contested(site.Town.Type) || data.FactionId(data.SiteFactions[i]) is not { } f || _state.Find(f) is null) continue;

            string owner = Name(world, f);
            string tip = site.Town.ShownName() + "\n" + tr("Owner") + "\t" + owner;
            if (_state.Owners.ContainsKey(i)) tip += "\n" + tr("the occupation");
            if (_state.FindTown(i) is { } t)
                tip += "\n" + tr("People") + "\t" + t.Population.ToString(CultureInfo.InvariantCulture) + "\n" + tr("Faith") + "\t" + FaithName(t.Religion);
            var wars = _state.Wars.Where(w => w.Involves(f)).Select(w => Name(world, w.Other(f))).ToList();
            if (wars.Count > 0) tip += "\n" + tr("At war with") + "\t" + string.Join(", ", wars);
            string group = site.Town.Type == TownType.Town ? "Town" : "Village";
            float territory = Math.Clamp(site.Town.Radius / 1250f, 1, 4) * (site.Town.Type == TownType.Town ? 3 : 2);
            marks.Add(new(new(site.Position.X, site.Position.Z), group, Colour(f), owner, tip, territory));
        }

        return marks;
    }

    /// <summary>A nation's colour: a hue from its string id at full saturation (port choice; the original's own table is not read).</summary>
    static Vector4 Colour(string faction)
    {
        float h = (Rng.StableHash(faction) % 360) / 60f;
        float x = 1 - Math.Abs(h % 2 - 1);
        var (r, g, b) = (int)h switch
        {
            0 => (1f, x, 0f),
            1 => (x, 1f, 0f),
            2 => (0f, 1f, x),
            3 => (0f, x, 1f),
            4 => (x, 0f, 1f),
            _ => (1f, 0f, x),
        };
        return new(0.25f + 0.75f * r, 0.25f + 0.75f * g, 0.25f + 0.75f * b, 1);
    }
}
