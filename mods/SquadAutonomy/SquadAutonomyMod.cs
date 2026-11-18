using Meitou.Data;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.SquadAutonomy;

/// <summary>
/// Port of Squad Autonomy (Au2942, workshop 3800729618, GPL-3.0): a window (the AUT button) turns a player squad into a semi-NPC squad on AI
/// packages, through the engine's autonomous player squads (<c>SetSquadAutonomy</c>, <c>ChangeAi</c>, <c>SetSquadHome</c>): a work package, an
/// optional rest package outside the work hours, and a home building.
/// </summary>
public sealed class SquadAutonomyMod : IMod
{
    internal static readonly AutonomySettings Settings = new();

    /// <summary>The running world's system, for the window; null before a world starts.</summary>
    internal static volatile AutonomySystem? Running;

    public void Initialize(ModContext context)
    {
        context.AddSettings(Settings);

        var packages = PackageList.Load(context.Records(72), Path.Combine(context.Folder, "packages.txt"));
        context.Log($"Squad Autonomy: {packages.Featured.Count} listed packages, {packages.All.Count} in all");
        context.AddSystem("autonomy", _ => Running = new AutonomySystem(context.Id, context.Population, context.Data, context.Enqueue), SystemAnchor.End);
        context.AddUi(new AutonomyWindow(context, packages));
        context.Log("Squad Autonomy initialized");
    }
}

public sealed class AutonomySettings
{
    [Setting("Show AUT button", Hint = "The AUT button on the top bar opens the Squad Autonomy window.")]
    public bool ShowButton { get; set; } = true;
}

/// <summary>An AI package as the window lists it: its record id and its name without the <c>&lt;SquadAutonomy&gt;</c> tag.</summary>
sealed record PackageEntry(string Id, string Name);

/// <summary>The AI_PACKAGE records: those the original's rules list (named in packages.txt, or tagged <c>&lt;SquadAutonomy&gt;</c>) and all of them.</summary>
sealed record PackageList(IReadOnlyList<PackageEntry> Featured, IReadOnlyList<PackageEntry> All)
{
    const string Tag = "<SquadAutonomy>";

    public static PackageList Load(IEnumerable<GameRecord> records, string configPath)
    {
        var named = File.Exists(configPath)
            ? File.ReadAllLines(configPath).Select(l => l.Trim().Trim('"')).Where(l => l.Length > 0 && !l.StartsWith('#') && !l.StartsWith('<'))
                .ToHashSet(StringComparer.Ordinal)
            : [];

        var all = records.Select(r => new PackageEntry(r.StringId, Display(r.Name))).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var featured = records.Where(r => r.Name.EndsWith(Tag, StringComparison.Ordinal) || named.Contains(r.Name))
            .Select(r => new PackageEntry(r.StringId, Display(r.Name))).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        return new(featured, all);
    }

    public string NameOf(string? id) => id is null ? "None" : All.FirstOrDefault(p => p.Id == id)?.Name ?? id;

    static string Display(string name) => name.EndsWith(Tag, StringComparison.Ordinal) ? name[..^Tag.Length].TrimEnd() : name;
}

/// <summary>The squad's plan: its work package, the rest package and the work hours (rest outside them); null packages: none.</summary>
public sealed record SquadPlanOrder(int SquadId, string? Work, string? Rest, int StartHour, int EndHour) : SimCommand;

/// <summary>The squad's home becomes the building the HUD inspects (none inspected: no change), or none with <paramref name="Clear"/>.</summary>
public sealed record SquadHomeOrder(int SquadId, bool Clear) : SimCommand;
