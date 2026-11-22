using Meitou.Data;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.SquadAutonomy;

/// <summary>
/// Port of Squad Autonomy (Au2942, workshop 3800729618, GPL-3.0): a window (the AUT button) turns a player squad into a semi-NPC squad on AI
/// packages, through the engine's autonomous player squads (<c>SetSquadAutonomy</c>, <c>ChangeAi</c>, <c>SetSquadHome</c>): AI packages with
/// priorities, work hours, and a home building. The original's own packages come with its <c>.mod</c> (a required item).
/// </summary>
public sealed class SquadAutonomyMod : IMod
{
    /// <summary>
    /// The mod's settings.
    /// </summary>
    internal static readonly AutonomySettings Settings = new();

    /// <summary>
    /// The running world's system, for the window; null before a world starts.
    /// </summary>
    internal static volatile AutonomySystem? Running;

    /// <summary>
    /// The original's Workshop id and folder name: its installed folder holds SquadAutonomy.cfg, whose <c>&lt;Packages&gt;</c> are listed too.
    /// </summary>
    static readonly string[] OriginalFolders = ["3800729618", "SquadAutonomy"];

    public void Initialize(ModContext context)
    {
        context.AddSettings(Settings);

        var configs = new List<string> { Path.Combine(context.Folder, "packages.txt") };
        configs.AddRange(OriginalConfigs(context.Folder));
        var packages = PackageList.Load(context.Records(72), configs);
        context.Log($"Squad Autonomy: {packages.Featured.Count} listed packages, {packages.All.Count} in all");

        context.AddSystem("autonomy", _ => Running = new AutonomySystem(context.Id, context.Population, context.Data, context.Enqueue),
            SystemAnchor.End);
        context.AddUi(new AutonomyWindow(context, packages));
        context.Log("Squad Autonomy initialized");
    }

    /// <summary>
    /// The installed original's SquadAutonomy.cfg beside this mod's folder (Workshop id or mod name), when there.
    /// </summary>
    static IEnumerable<string> OriginalConfigs(string folder)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(folder)) is not { } parent) return [];

        return OriginalFolders.Select(name => Path.Combine(parent, name, "SquadAutonomy.cfg")).Where(File.Exists);
    }
}

/// <summary>
/// The mod's settings.
/// </summary>
public sealed class AutonomySettings
{
    /// <summary>
    /// The original's <c>ShowOnMain</c> (default true).
    /// </summary>
    [Setting("Show button on Mainbar", Hint = "Show AUT button on the Mainbar.")]
    public bool ShowButton { get; set; } = true;
}

/// <summary>
/// An AI package as the window lists it: its record id and its name without the <c>&lt;SquadAutonomy&gt;</c> tag.
/// </summary>
sealed record PackageEntry(string Id, string Name);

/// <summary>
/// The AI_PACKAGE records: those the original's rules list (named in a config's package list, or tagged <c>&lt;SquadAutonomy&gt;</c>) and all of
/// them (this port's addition).
/// </summary>
sealed record PackageList(IReadOnlyList<PackageEntry> Featured, IReadOnlyList<PackageEntry> All)
{
    /// <summary>
    /// The name tag the original lists a package by.
    /// </summary>
    const string Tag = "<SquadAutonomy>";

    /// <summary>
    /// The lists from the merged records and the config files.
    /// </summary>
    public static PackageList Load(IEnumerable<GameRecord> records, IEnumerable<string> configs)
    {
        var named = configs.SelectMany(ConfigNames).ToHashSet(StringComparer.Ordinal);
        var list = records.ToList();

        var all = list
            .Select(Entry)
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var featured = list
            .Where(r => r.Name.EndsWith(Tag, StringComparison.Ordinal) || named.Contains(r.Name))
            .Select(Entry)
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new(featured, all);
    }

    /// <summary>
    /// The package names of a config: the original's SquadAutonomy.cfg names them quoted between <c>&lt;Packages&gt;</c> and
    /// <c>&lt;/Packages&gt;</c>; this port's packages.txt one per line (<c>#</c> comments).
    /// </summary>
    static IEnumerable<string> ConfigNames(string path)
    {
        var lines = File.ReadAllLines(path).Select(l => l.Trim()).ToList();
        int start = lines.IndexOf("<Packages>");
        int end = lines.IndexOf("</Packages>");
        if (start >= 0) lines = lines.Skip(start + 1).Take((end > start ? end : lines.Count) - start - 1).ToList();

        return lines
            .Select(l => l.Trim('"'))
            .Where(l => l.Length > 0 && !l.StartsWith('#') && !l.StartsWith('<'));
    }

    /// <summary>
    /// A record's entry.
    /// </summary>
    static PackageEntry Entry(GameRecord r) => new(r.StringId, Display(r.Name));

    /// <summary>
    /// The shown name of a package id; None for none.
    /// </summary>
    public string NameOf(string? id) => id is null ? "None" : All.FirstOrDefault(p => p.Id == id)?.Name ?? id;

    /// <summary>
    /// A package name without the tag.
    /// </summary>
    static string Display(string name) => name.EndsWith(Tag, StringComparison.Ordinal) ? name[..^Tag.Length].TrimEnd() : name;
}

/// <summary>
/// A package of a plan with its priority (the original's 0-5; a higher one is tried first).
/// </summary>
public readonly record struct PlannedPackage(string Id, int Priority);

/// <summary>
/// The squad's plan: its AI packages by priority, the rest package (this port's: run outside the work hours; null none) and the work hours
/// (0-24; start equal to end: no work time, as the original).
/// </summary>
public sealed record SquadPlanOrder(int SquadId, IReadOnlyList<PlannedPackage> Packages, string? Rest, int StartHour, int EndHour) : SimCommand;

/// <summary>
/// The squad's home becomes the building the HUD inspects (none inspected: no change), or none with <paramref name="Clear"/>.
/// </summary>
public sealed record SquadHomeOrder(int SquadId, bool Clear) : SimCommand;

/// <summary>
/// The window's Enable Autonomy switch for a squad.
/// </summary>
public sealed record AutonomySwitchOrder(int SquadId, bool On) : SimCommand;
