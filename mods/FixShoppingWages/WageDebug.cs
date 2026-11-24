using System.Globalization;
using System.Text;
using Meitou.Data.Fcs;
using Meitou.Simulation;
using Meitou.Simulation.Factions;

namespace Meitou.FixShoppingWages;

/// <summary>The original's developer hotkeys (Ctrl+T, Ctrl+Shift+T, Ctrl+R, Ctrl+Shift+R).</summary>
public enum WageDebugKind
{
    /// <summary>Ctrl+T: log the character's money and its record's <c>wages</c>, <c>money min</c>, <c>money max</c>.</summary>
    Info,

    /// <summary>Ctrl+Shift+T: give the character 2000 (its squad's purse) and log before and after.</summary>
    Gift,

    /// <summary>Ctrl+R: dump the character's record (its int, float, string and bool fields).</summary>
    Record,

    /// <summary>Ctrl+Shift+R: look up Dried Meat (by name, else by string id) and log its value and average price.</summary>
    DriedMeat,
}

/// <summary>A developer hotkey's request for <paramref name="Character"/> (none: no character selected); applied while paused too.</summary>
public sealed record WageDebugRequest(WageDebugKind Kind, CharacterId Character) : SimCommand, IPausedCommand;

/// <summary>Counts and logs the daily pay of the verbose log (the original's <c>DAILYUPDATE DIFF</c> / <c>NODIFF</c> lines).</summary>
sealed class PayCount
{
    public int Npcs { get; private set; }

    public int Changed { get; private set; }

    public void Note(Action<string> log, string who, long before, long after)
    {
        Npcs++;
        if (after != before) Changed++;

        string tag = after != before ? "[DAILYUPDATE DIFF] " : "[DAILYUPDATE NODIFF] ";
        log($"{tag}{who} money={before}->{after} (delta={after - before})");
    }
}

sealed partial class WageSystem
{
    const int Gift = 2000;

    const string DriedMeatName = "Dried Meat";

    const string DriedMeatId = "46130-DriedMeat";

    /// <summary>The non-player squads' purses at the last in-game hour of the verbose log, by squad id; null before the first.</summary>
    SortedDictionary<int, long>? _purses;

    long _hour = long.MinValue;

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands)
        {
            if (command is WageDebugRequest request && settings.DeveloperDebug) Debug(world, request);
        }
    }

    void Debug(World world, WageDebugRequest request)
    {
        if (request.Kind == WageDebugKind.DriedMeat)
        {
            DumpDriedMeat();
            return;
        }

        string key = request.Kind switch { WageDebugKind.Info => "CTRL+T", WageDebugKind.Gift => "CTRL+SHIFT+T", _ => "CTRL+R" };
        var table = world.Characters;
        if (request.Character.IsNone || !table.TryResolveNext(request.Character, out int slot) || table.Cold(slot) is not { } cold)
        {
            log($"{key}: no character selected");
            return;
        }

        var squad = world.Squads.Find(cold.SquadId);
        if (request.Kind == WageDebugKind.Info) log($"{key}: {Describe(cold, request.Character)} money={squad?.Money ?? 0}");
        else if (request.Kind == WageDebugKind.Gift) GiveMoney(key, cold, request.Character, squad);
        else DumpRecord(key, cold.RecordId);
    }

    void GiveMoney(string key, CharacterCold cold, CharacterId id, Squad? squad)
    {
        if (squad is null)
        {
            log($"{key}: {cold.Name} has no squad purse");
            return;
        }

        long before = squad.Money;
        squad.Money += Gift;
        log($"{key}: name=\"{cold.Name}\" id=\"{id}\" before={before} gave={Gift} after={squad.Money}");
    }

    void DumpRecord(string key, string recordId)
    {
        if (data.Find(recordId) is not { } record)
        {
            log($"{key}: selected character has no GameData");
            return;
        }

        log($"{key}: name=\"{record.Name}\" type={record.Type} stringID=\"{record.StringId}\"");
        foreach (var (field, value) in record.Ints) log($"  [idata] {field} = {value}");
        foreach (var (field, value) in record.Floats) log($"  [fdata] {field} = {value.ToString(CultureInfo.InvariantCulture)}");
        foreach (var (field, value) in record.Strings) log($"  [sdata] {field} = \"{value}\"");
        foreach (var (field, value) in record.Bools) log($"  [bdata] {field} = {value}");
        log($"{key}: -- end of GameData dump --");
    }

    void DumpDriedMeat()
    {
        const string key = "CTRL+SHIFT+R";
        var byName = data.OfType(FcsRecordType.Item).Where(r => r.Name == DriedMeatName).OrderBy(r => r.StringId, StringComparer.Ordinal);
        var meat = byName.FirstOrDefault() ?? data.Find(DriedMeatId);
        if (meat is null)
        {
            log($"{key}: could not find Dried Meat by name or string id");
            return;
        }

        string value = meat.Ints.TryGetValue("value", out int v) ? v.ToString(CultureInfo.InvariantCulture) : "(no value)";
        log($"{key}: found GameData stringID=\"{meat.StringId}\" type={meat.Type} value={value} (price) average price={driedMeat}");
    }

    /// <summary>
    /// The verbose log's line for a character: name, id and its record's wage fields (-1 when missing).
    /// </summary>
    string Describe(CharacterCold cold, CharacterId id)
    {
        var record = data.Find(cold.RecordId);
        int Field(string name) => record is not null && record.Ints.TryGetValue(name, out int value) ? value : -1;

        return $"name=\"{cold.Name}\" id=\"{id}\" wages={Field("wages")} moneyMin={Field("money min")} moneyMax={Field("money max")}";
    }

    /// <summary>
    /// The original's hourly diff (its <c>updateUT</c> hook, verbose only): once an in-game hour, the non-player purses that changed since the last
    /// hour, and with <see cref="WageSettings.LimitVerboseDebugLogging"/> off the squads new or gone since.
    /// </summary>
    void HourlyDiff(World world)
    {
        long hour = (long)Math.Floor(world.GameHours);
        if (hour == _hour) return;

        _hour = hour;
        var now = Purses(world);
        if (_purses is { } last) LogDiff(world, last, now, hour);

        _purses = now;
    }

    static SortedDictionary<int, long> Purses(World world)
    {
        var purses = new SortedDictionary<int, long>();
        foreach (var squad in world.Squads.All)
        {
            if (!squad.Dead && squad.Members.Count > 0 && !SquadIsPlayer(world, squad)) purses[squad.Id] = squad.Money;
        }

        return purses;
    }

    static bool SquadIsPlayer(World world, Squad squad) =>
        world.Characters.TryResolveNext(squad.Members[0], out int slot) && world.Characters.Cold(slot) is { IsPlayer: true };

    void LogDiff(World world, SortedDictionary<int, long> last, SortedDictionary<int, long> now, long hour)
    {
        bool limit = settings.LimitVerboseDebugLogging;
        int changed = 0;
        foreach (var (id, money) in now)
        {
            bool known = last.TryGetValue(id, out long was);
            if (!known && !limit) log($"[HOURLY DIFF] NEW: {SquadName(world, id)} money={money}");
            if (!known || was == money) continue;

            changed++;
            log($"[HOURLY DIFF] {SquadName(world, id)} money={was}->{money} (delta={money - was})");
        }

        if (!limit) LogRemoved(world, last, now);
        if (changed > 0) log($"[HOURLY DIFF] {changed} NPC(s) had money changes this hour (hour={hour % 24})");
    }

    void LogRemoved(World world, SortedDictionary<int, long> last, SortedDictionary<int, long> now)
    {
        foreach (var (id, money) in last)
        {
            if (!now.ContainsKey(id)) log($"[HOURLY DIFF] REMOVED: {SquadName(world, id)} had={money}");
        }
    }

    static string SquadName(World world, int id)
    {
        var squad = world.Squads.Find(id);
        var text = new StringBuilder("name=\"");
        text.Append(squad is null ? "" : squad.Name.Length > 0 ? squad.Name : world.Characters.NameOf(squad.Leader));
        return text.Append("\" (id=\"squad ").Append(id).Append("\")").ToString();
    }
}
