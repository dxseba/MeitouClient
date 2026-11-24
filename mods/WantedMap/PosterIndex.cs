using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Simulation.Population;

namespace Meitou.WantedMap;

/// <summary>
/// A poster's targets: CHARACTER and SQUAD_TEMPLATE string ids, and the name a marker shows for each.
/// </summary>
sealed record PosterTargets(IReadOnlyList<string> Characters, IReadOnlyList<string> Squads);

/// <summary>
/// The link from poster to targets, built once from the merged data as the original's description lists it: the override pairs, then the name
/// after the colon, then the bounty holders the description names, then the reward the description states. Also each target's home towns
/// (for the "lives here" marker) and the names to show.
/// </summary>
sealed partial class PosterIndex
{
    /// <summary>
    /// The item function of a book (<c>item function</c> 13, the posters among them; <b>Verified</b> original DLL (decompiled)).
    /// </summary>
    const int BookFunction = 13;

    /// <summary>
    /// The squad template lists that hold its members (original DLL strings).
    /// </summary>
    static readonly string[] SquadLists = ["leader", "squad", "squad2", "slaves"];

    /// <summary>
    /// The TOWN lists whose names hold one of these name the town's squads (original DLL strings).
    /// </summary>
    static readonly string[] TownListWords = ["squad", "resident", "spawn"];

    readonly Dictionary<string, PosterTargets> _posters = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);
    readonly Dictionary<string, List<string>> _homes = new(StringComparer.Ordinal);
    readonly HashSet<string> _targets = new(StringComparer.Ordinal);

    public PosterTargets? Of(string itemRecord) => _posters.GetValueOrDefault(itemRecord);

    public string NameOf(string target) => _names.GetValueOrDefault(target, target);

    /// <summary>
    /// The TOWN ids where <paramref name="target"/> (a character or a squad template) lives.
    /// </summary>
    public IReadOnlyList<string> HomesOf(string target) => _homes.GetValueOrDefault(target) ?? [];

    public int Count => _posters.Count;

    /// <summary>
    /// Every poster and its targets, by ITEM id.
    /// </summary>
    public IEnumerable<(string Poster, PosterTargets Targets)> All =>
        _posters.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => (p.Key, p.Value));

    /// <summary>
    /// Whether some poster names <paramref name="record"/>.
    /// </summary>
    public bool IsTarget(string record) => _targets.Contains(record);

    public static PosterIndex Build(GameDatabase data, OriginalFiles files, IReadOnlyCollection<string> stopWords)
    {
        var index = new PosterIndex();
        var characters = data.OfType(FcsRecordType.Character).OrderBy(r => r.StringId, StringComparer.Ordinal).ToList();
        var squads = data.OfType(FcsRecordType.SquadTemplate).OrderBy(r => r.StringId, StringComparer.Ordinal).ToList();
        foreach (var r in characters.Concat(squads)) index._names[r.StringId] = r.Name;

        var overrides = ReadOverrides(files.Find("WantedMap_overrides.txt"));
        var books = data.OfType(FcsRecordType.Item).Where(r => r.GetInt("item function") == BookFunction)
            .OrderBy(r => r.StringId, StringComparer.Ordinal).ToList();
        var matcher = new Matcher(characters, squads, stopWords);
        var prefixes = matcher.PosterPrefixes(books);
        foreach (var book in books)
        {
            if (overrides.TryGetValue(book.StringId, out var ids)) index._posters[book.StringId] = Split(ids, data);
            else if (Split(book.Name) is { } title && prefixes.Contains(title.Prefix) && matcher.Match(book, title.Name) is { } found)
                index._posters[book.StringId] = found;
        }

        foreach (var targets in index._posters.Values) index._targets.UnionWith(targets.Characters.Concat(targets.Squads));
        index.AddHomes(data, squads);
        return index;
    }

    /// <summary>
    /// A title's prefix and name around its first colon (<c>:</c> or the full-width <c>：</c>); null without one.
    /// </summary>
    internal static (string Prefix, string Name)? Split(string title)
    {
        int colon = title.IndexOfAny([':', '：']);
        if (colon <= 0) return null;

        string name = title[(colon + 1)..].Trim();
        return name.Length > 0 ? (title[..colon].Trim(), name) : null;
    }

    static PosterTargets Split(IReadOnlyList<string> ids, GameDatabase data)
    {
        var characters = ids.Where(id => data.Find(id)?.Type == FcsRecordType.Character).ToList();
        var squads = ids.Where(id => data.Find(id)?.Type == FcsRecordType.SquadTemplate).ToList();
        return new(characters, squads);
    }

    /// <summary>
    /// <c>WantedMap_overrides.txt</c> (this mod's folder, else the installed original's; null: neither has one): <c>poster id | target ids</c>,
    /// comma-separated, <c>#</c> comments.
    /// </summary>
    static Dictionary<string, IReadOnlyList<string>> ReadOverrides(string? path)
    {
        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (path is null) return map;

        foreach (var raw in File.ReadLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#' || line.Split('|') is not [var poster, var targets]) continue;

            map[poster.Trim()] = targets.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        return map;
    }

    /// <summary>
    /// The towns whose squad lists hold a squad template, and through it each of the template's members.
    /// </summary>
    void AddHomes(GameDatabase data, IReadOnlyList<GameRecord> squads)
    {
        foreach (var town in data.OfType(FcsRecordType.Town).OrderBy(r => r.StringId, StringComparer.Ordinal))
        {
            foreach (var list in town.ReferenceLists.Where(l => TownListWords.Any(w => l.Contains(w, StringComparison.Ordinal))))
                foreach (var squad in town.GetReferences(list)) Home(squad.TargetStringId, town.StringId);
        }

        foreach (var squad in squads)
        {
            if (!_homes.TryGetValue(squad.StringId, out var towns)) continue;

            foreach (var list in SquadLists)
                foreach (var member in squad.GetReferences(list))
                    foreach (var town in towns) Home(member.TargetStringId, town);
        }
    }

    void Home(string target, string town)
    {
        if (!_homes.TryGetValue(target, out var towns)) _homes[target] = towns = [];
        if (!towns.Contains(town)) towns.Add(town);
    }

    /// <summary>
    /// The matching rules over the data's characters and squad templates.
    /// </summary>
    sealed partial class Matcher(IReadOnlyList<GameRecord> characters, IReadOnlyList<GameRecord> squads, IReadOnlyCollection<string> stopWords)
    {
        readonly ILookup<string, GameRecord> _characters = characters.ToLookup(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase);
        readonly ILookup<string, GameRecord> _squads = squads.ToLookup(s => s.Name.Trim(), StringComparer.OrdinalIgnoreCase);
        readonly List<GameRecord> _wanted = characters.Where(c => c.GetInt("bounty amount") > 0).ToList();

        /// <summary>
        /// The title prefixes of posters: those that at least two books share with a name after the colon that is exactly a character's.
        /// </summary>
        public HashSet<string> PosterPrefixes(IEnumerable<GameRecord> books)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var book in books)
            {
                if (Split(book.Name) is not { } title || !_characters.Contains(title.Name)) continue;

                counts[title.Prefix] = counts.GetValueOrDefault(title.Prefix) + 1;
            }

            return counts.Where(c => c.Value >= 2).Select(c => c.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The name after the colon; else the bounty holders the description names; else those whose bounty the description states and who share a
        /// word of the name.
        /// </summary>
        public PosterTargets? Match(GameRecord book, string name)
        {
            var byName = new PosterTargets(Ids(_characters[name]), Ids(_squads[name]));
            if (byName.Characters.Count + byName.Squads.Count > 0) return byName;

            string description = book.GetString("description");
            if (description.Length == 0) return null;

            var named = _wanted.Where(c => Mentions(description, c.Name)).ToList();
            if (named.Count > 0) return new(Ids(named), []);

            var rewards = Rewards(description);
            var words = Words(name);
            var paid = _wanted.Where(c => rewards.Contains(c.GetInt("bounty amount")) && Words(c.Name).Overlaps(words)).ToList();
            return paid.Count > 0 ? new(Ids(paid), []) : null;
        }

        static List<string> Ids(IEnumerable<GameRecord> records) => records.Select(r => r.StringId).Distinct(StringComparer.Ordinal).ToList();

        static bool Mentions(string text, string name)
        {
            string trimmed = name.Trim();
            if (trimmed.Length < 3) return false;

            var pattern = @"(?<![\p{L}\p{N}])" + Regex.Escape(trimmed) + @"(?![\p{L}\p{N}])";
            return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        /// <summary>
        /// The whole numbers in the text, digit groups joined across spaces, commas and dots ("80 000", "80,000").
        /// </summary>
        static HashSet<int> Rewards(string text)
        {
            var set = new HashSet<int>();
            foreach (Match m in Number().Matches(text))
            {
                string digits = new([.. m.Value.Where(char.IsAsciiDigit)]);
                if (int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n > 0) set.Add(n);
            }

            return set;
        }

        HashSet<string> Words(string name)
        {
            var words = WordPattern().Matches(name).Select(m => m.Value.ToLowerInvariant());
            return words.Where(w => !stopWords.Contains(w)).ToHashSet(StringComparer.Ordinal);
        }

        [GeneratedRegex(@"\d{1,3}(?:[ ,. ]\d{3})+|\d+")]
        private static partial Regex Number();

        [GeneratedRegex(@"[\p{L}\p{N}]+")]
        private static partial Regex WordPattern();
    }
}

/// <summary>
/// A town's position on the map, by TOWN id (the placed sites, and the record each placement is now).
/// </summary>
sealed class TownPlaces(PopulationData population)
{
    public Vector2? Of(string town)
    {
        foreach (var site in population.Sites)
            if (site.Record.Id == town || site.Town.Id == town) return new(site.Position.X, site.Position.Z);

        return null;
    }
}
