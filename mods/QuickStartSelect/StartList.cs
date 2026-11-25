using System.Globalization;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;
using Meitou.Localisation;

namespace Meitou.QuickStartSelect;

/// <summary>A faction a start sets the player's relation with, and the value.</summary>
sealed record StartRelation(string Faction, int Value);

/// <summary>A town a start may begin in: its TOWN id and shown name.</summary>
sealed record StartTown(string Id, string Name);

/// <summary>
/// One row of the list: the start's shown texts, its difficulty rank, the races of its squad's people, its characters line, towns and relations.
/// </summary>
sealed record StartInfo(NewGameStart Start, string Name, string DifficultyText, int Rank, string Style, string Description,
    IReadOnlyList<string> Races, string Characters, IReadOnlyList<StartTown> Towns, IReadOnlyList<StartRelation> Relations)
{
    /// <summary>The name as the search compares it.</summary>
    public string SearchName { get; } = Difficulty.Normalise(Name);
}

/// <summary>
/// Builds the rows from the game data as the original reads the start-offs: a start with no squad is left out; races come from each squad
/// entry's <c>leader</c> and <c>squad</c> lists (a CHARACTER without a race is "Human (random)"); animals are counted apart.
/// </summary>
static class StartList
{
    public const string HumanRandom = "Human (random)";

    public static List<StartInfo> Build(GameDatabase data, Func<string, string> tr, string[] customLevels, Action<string>? log)
    {
        var rows = new List<StartInfo>();
        foreach (var start in NewGameStart.LoadAll(data))
        {
            if (!start.Squad.Any(s => data.Find(s.Id) is not null))
            {
                log?.Invoke($"start '{start.Name}' has no squad, hidden");
                continue;
            }

            rows.Add(Row(data, tr, customLevels, start));
        }

        return rows;
    }

    static StartInfo Row(GameDatabase data, Func<string, string> tr, string[] customLevels, NewGameStart start)
    {
        string difficulty = ShownText.Record(start.Id, start.Difficulty, "difficulty");
        return new StartInfo(start, ShownText.Record(start.Id, start.Name), difficulty, Difficulty.Rank(start.Difficulty, customLevels),
            ShownText.Record(start.Id, start.Style, "style"), ShownText.Record(start.Id, start.Description, "description"),
            Races(data, tr, start), Characters(data, tr, start), Towns(data, start), Relations(data, start));
    }

    /// <summary>The races of the people of each squad entry (its own race for a CHARACTER entry), sorted.</summary>
    static IReadOnlyList<string> Races(GameDatabase data, Func<string, string> tr, NewGameStart start)
    {
        var races = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var entry in start.Squad)
        {
            if (data.Find(entry.Id) is not { } record) continue;

            if (record.Type == FcsRecordType.Character) AddRaces(data, tr, record, races);
            foreach (string list in new[] { "leader", "squad" })
            {
                foreach (var person in record.GetReferences(list))
                {
                    if (data.Find(person.TargetStringId) is { } character) AddRaces(data, tr, character, races);
                }
            }
        }

        return [.. races];
    }

    static void AddRaces(GameDatabase data, Func<string, string> tr, GameRecord character, SortedSet<string> races)
    {
        var listed = character.GetReferences("race").Select(r => data.Find(r.TargetStringId)).OfType<GameRecord>().ToList();
        if (listed.Count == 0) races.Add(tr(HumanRandom));
        foreach (var race in listed) races.Add(race.ShownName());
    }

    /// <summary>
    /// "n" or "n-m" people (leaders and squad members by their counts, at least 1 each, plus the random extras), then ", animals: k".
    /// </summary>
    static string Characters(GameDatabase data, Func<string, string> tr, NewGameStart start)
    {
        int least = 0, most = 0, animals = 0;
        foreach (var entry in start.Squad)
        {
            if (data.Find(entry.Id) is not { } record) continue;

            if (record.Type == FcsRecordType.Character)
            {
                least += Math.Max(1, entry.V0);
                most += Math.Max(1, entry.V0);
                continue;
            }

            int fixedCount = Count(data, record, "leader") + Count(data, record, "squad");
            int random = Math.Max(0, record.GetInt("num random chars"));
            least += fixedCount + random;
            most += fixedCount + Math.Max(random, record.GetInt("num random chars max"));
            animals += Count(data, record, "animals");
        }

        string people = least == most ? least.ToString(CultureInfo.InvariantCulture) : $"{least}-{most}";
        return animals > 0 ? people + tr(", animals: %d").Replace("%d", animals.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal) : people;
    }

    /// <summary>The people of a list: each found entry counts its value, at least 1.</summary>
    static int Count(GameDatabase data, GameRecord record, string list) =>
        record.GetReferences(list).Where(r => data.Find(r.TargetStringId) is not null).Sum(r => Math.Max(1, r.Values.Value0));

    static IReadOnlyList<StartTown> Towns(GameDatabase data, NewGameStart start) =>
        [.. start.Towns.Select(t => data.Find(t.Id)).OfType<GameRecord>().Select(r => new StartTown(r.StringId, r.ShownName()))];

    static IReadOnlyList<StartRelation> Relations(GameDatabase data, NewGameStart start) =>
        [.. start.FactionRelations.Where(f => data.Find(f.Id) is not null).Select(f => new StartRelation(data.Find(f.Id)!.ShownName(), f.V0))];
}
