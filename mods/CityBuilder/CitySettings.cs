using System.Globalization;
using Meitou.Modding;

namespace Meitou.CityBuilder;

/// <summary>
/// The original's City.ini keys (sections Prices, Arrival, Guards, Tavern, Shops, Residents, Tidy), under Mod settings. The
/// defaults are the original's
/// shipped values, replaced by the installed original's City.ini when it is there (<see cref="ReadIni"/>), so a player's edited ini carries over.
/// </summary>
public sealed class CitySettings
{
    [Setting("Residence price (small)", Hint = "What settlers pay for a Small Shack or Storm House residence.", Min = 0, Max = 200000, Step = 500,
        SubGroup = "Prices")]
    public int HomeSmall { get; set; } = 7000;

    [Setting("Residence price (large)", Hint = "What settlers pay for any other residence.", Min = 0, Max = 200000, Step = 500, SubGroup = "Prices")]
    public int HomeLarge { get; set; } = 15000;

    [Setting("Trade house price (small)", Hint = "What a trader pays for a Storm House trade house.", Min = 0, Max = 200000, Step = 500,
        SubGroup = "Prices")]
    public int TradeSmall { get; set; } = 12000;

    [Setting("Trade house price (large)", Hint = "What a trader pays for any other trade house.", Min = 0, Max = 200000, Step = 500,
        SubGroup = "Prices")]
    public int TradeLarge { get; set; } = 20000;

    [Setting("Settlers come after (min hours)", Min = 0, Max = 240, Step = 1, SubGroup = "Arrival")]
    public int HomeHoursMin { get; set; } = 6;

    [Setting("Settlers come after (max hours)", Min = 0, Max = 240, Step = 1, SubGroup = "Arrival")]
    public int HomeHoursMax { get; set; } = 18;

    [Setting("Trader comes after (min hours)", Min = 0, Max = 240, Step = 1, SubGroup = "Arrival")]
    public int TradeHoursMin { get; set; } = 12;

    [Setting("Trader comes after (max hours)", Min = 0, Max = 240, Step = 1, SubGroup = "Arrival")]
    public int TradeHoursMax { get; set; } = 30;

    [Setting("Innkeeper comes after (min hours)", Min = 0, Max = 240, Step = 1, SubGroup = "Arrival")]
    public int InnkeeperHoursMin { get; set; } = 4;

    [Setting("Innkeeper comes after (max hours)", Min = 0, Max = 240, Step = 1, SubGroup = "Arrival")]
    public int InnkeeperHoursMax { get; set; } = 10;

    [Setting("Others come after a refusal (hours)", Min = 1, Max = 480, Step = 1, SubGroup = "Arrival")]
    public int RetryAfterRefusalHours { get; set; } = 36;

    [Setting("Scan radius", Hint = "How near one of your people must be for hired guards to take up their post (game units).", Min = 100,
        Max = 20000, Step = 100, SubGroup = "Arrival")]
    public int ScanRadius { get; set; } = 3000;

    [Setting("Guard hiring cost", Hint = "Price of hiring one guard. Posts I-IV hold 5, 10, 15 and 20 guards.", Min = 0, Max = 50000, Step = 100,
        SubGroup = "Guards")]
    public int HireCost { get; set; } = 2000;

    [Setting("Bed price per night", Hint = "What a tavern guest pays for a night.", Min = 0, Max = 5000, Step = 10, SubGroup = "Tavern")]
    public int NightPrice { get; set; } = 100;

    [Setting("Your share of the night (%)", Min = 0, Max = 100, Step = 1, SubGroup = "Tavern")]
    public int PlayerSharePercent { get; set; } = 30;

    [Setting("Shop tax per day (%)", Hint = "Each morning every open shop pays this percent of what its trader paid for the building.", Min = 0,
        Max = 100, Step = 1, SubGroup = "Shops")]
    public int TaxPercentPerDay { get; set; } = 1;

    [Setting("Residents work", Hint = "Residents work at your farms, mines, quarries and wells near their home in working hours.",
        SubGroup = "Residents")]
    public bool Work { get; set; } = true;

    [Setting("Work from (hour)", Min = 0, Max = 23, Step = 1, SubGroup = "Residents")]
    public int WorkFromHour { get; set; } = 7;

    [Setting("Work to (hour)", Min = 1, Max = 24, Step = 1, SubGroup = "Residents")]
    public int WorkToHour { get; set; } = 19;

    [Setting("Tidy the settlement", Hint = "Idle people of your squads carry what lies on the ground in your settlement into its storages.",
        SubGroup = "Tidy")]
    public bool Tidy { get; set; } = true;

    [Setting("Haulers", Hint = "How many people carry things at the same time.", Min = 1, Max = 20, Step = 1, SubGroup = "Tidy")]
    public int Haulers { get; set; } = 3;

    [Setting("Reach", Hint = "How far beyond the settlement's radius things still count as lying in it (game units, 10 = 1 m).", Min = 0,
        Max = 5000, Step = 10, SubGroup = "Tidy")]
    public int Reach { get; set; } = 300;

    [Setting("Junk", Hint = "More junk to leave on the ground: item string ids, separated by commas.", SubGroup = "Tidy")]
    public string Junk { get; set; } = "";

    /// <summary>
    /// Sets the defaults from <paramref name="path"/> (the original's City.ini); a key missing or not a number keeps the shipped default.
    /// </summary>
    public void ReadIni(string path)
    {
        var values = Ini.Read(path);
        int Get(string section, string key, int fallback) =>
            values.TryGetValue((section, key), out var text) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
                ? v : fallback;

        HomeSmall = Get("Prices", "HomeSmall", HomeSmall);
        HomeLarge = Get("Prices", "HomeLarge", HomeLarge);
        TradeSmall = Get("Prices", "TradeSmall", TradeSmall);
        TradeLarge = Get("Prices", "TradeLarge", TradeLarge);
        HomeHoursMin = Get("Arrival", "HomeHoursMin", HomeHoursMin);
        HomeHoursMax = Get("Arrival", "HomeHoursMax", HomeHoursMax);
        TradeHoursMin = Get("Arrival", "TradeHoursMin", TradeHoursMin);
        TradeHoursMax = Get("Arrival", "TradeHoursMax", TradeHoursMax);
        InnkeeperHoursMin = Get("Arrival", "InnkeeperHoursMin", InnkeeperHoursMin);
        InnkeeperHoursMax = Get("Arrival", "InnkeeperHoursMax", InnkeeperHoursMax);
        RetryAfterRefusalHours = Get("Arrival", "RetryAfterRefusalHours", RetryAfterRefusalHours);
        HireCost = Get("Guards", "HireCost", HireCost);
        NightPrice = Get("Tavern", "NightPrice", NightPrice);
        PlayerSharePercent = Get("Tavern", "PlayerSharePercent", PlayerSharePercent);
        TaxPercentPerDay = Get("Shops", "TaxPercentPerDay", TaxPercentPerDay);
        ScanRadius = Get("Arrival", "ScanRadius", ScanRadius);
        Work = Get("Residents", "Work", Work ? 1 : 0) != 0;
        WorkFromHour = Get("Residents", "WorkFromHour", WorkFromHour);
        WorkToHour = Get("Residents", "WorkToHour", WorkToHour);
        Tidy = Get("Tidy", "Enabled", Tidy ? 1 : 0) != 0;
        Haulers = Get("Tidy", "Haulers", Haulers);
        Reach = Get("Tidy", "Reach", Reach);
        if (values.TryGetValue(("Tidy", "Junk"), out var junk)) Junk = junk;
    }

    /// <summary>
    /// The values the simulation reads, copied in its serial step.
    /// </summary>
    internal CityRules Rules() => new(HomeSmall, HomeLarge, TradeSmall, TradeLarge, (HomeHoursMin, HomeHoursMax), (TradeHoursMin, TradeHoursMax),
        (InnkeeperHoursMin, InnkeeperHoursMax), RetryAfterRefusalHours, HireCost, NightPrice, PlayerSharePercent, TaxPercentPerDay, ScanRadius,
        Work, WorkFromHour, WorkToHour, Tidy, Math.Max(1, Haulers), Reach,
        [.. Junk.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)]);
}

/// <summary>
/// A copy of the settings for one tick.
/// </summary>
internal sealed record CityRules(int HomeSmall, int HomeLarge, int TradeSmall, int TradeLarge, (int Min, int Max) HomeHours,
    (int Min, int Max) TradeHours, (int Min, int Max) InnkeeperHours, int RetryHours, int HireCost, int NightPrice, int SharePercent,
    int TaxPercent, int ScanRadius, bool Work, int WorkFrom, int WorkTo, bool Tidy, int Haulers, int Reach, IReadOnlyList<string> Junk);

/// <summary>
/// A minimal Windows ini reader: <c>[section]</c>, <c>key=value</c>, <c>;</c> comments; keys and sections case-insensitive.
/// </summary>
static class Ini
{
    public static Dictionary<(string, string), string> Read(string path)
    {
        var values = new Dictionary<(string, string), string>(new KeyComparer());
        string section = "";
        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#') continue;

            if (line[0] == '[' && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                continue;
            }

            int eq = line.IndexOf('=');
            if (eq > 0) values[(section, line[..eq].Trim())] = line[(eq + 1)..].Trim();
        }

        return values;
    }

    sealed class KeyComparer : IEqualityComparer<(string, string)>
    {
        public bool Equals((string, string) a, (string, string) b) =>
            string.Equals(a.Item1, b.Item1, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Item2, b.Item2, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string, string) k) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(k.Item1), StringComparer.OrdinalIgnoreCase.GetHashCode(k.Item2));
    }
}
