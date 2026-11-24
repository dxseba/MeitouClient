using Meitou.Data;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Ai;

namespace Meitou.SquadAutonomy;

/// <summary>
/// Port of Squad Autonomy (Au2942, workshop 3800729618, GPL-3.0): a window (the AUT button) turns a player squad into a semi-NPC squad on AI
/// packages, through the engine's autonomous player squads (<c>SetSquadAutonomy</c>, <c>SetSquadPackages</c>, <c>SetSquadHome</c>): AI packages
/// with priorities, work hours, a home building, and the combat switches (a chooser desire filter). The original's own packages come with its
/// <c>.mod</c> (a required item).
/// </summary>
public sealed class SquadAutonomyMod : IMod
{
    /// <summary>
    /// The mod's settings.
    /// </summary>
    internal static readonly AutonomySettings Settings = new();

    /// <summary>
    /// The registered settings, so the AUT button can keep the place it is dragged to.
    /// </summary>
    internal static Meitou.Modding.ModSettingsGroup? SettingsGroup;

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
        AddTranslations(context);
        SettingsGroup = context.AddSettings(Settings);
        AutonomyLog.Sink = context.Log;

        var configs = new List<string> { Path.Combine(context.Folder, "packages.txt") };
        configs.AddRange(OriginalConfigs(context.Folder));
        var packages = PackageList.Load(context.Records(72), configs);
        context.Log($"Squad Autonomy: {packages.Featured.Count} listed packages, {packages.All.Count} in all");

        context.AddSystem("autonomy", _ => Running = new AutonomySystem(context.Id, context.Population, context.Data, context.Enqueue,
            text => context.Tr(text)), SystemAnchor.End);
        context.AddUi(new AutonomyWindow(context, packages));
        context.Log("Squad Autonomy initialized");
    }

    /// <summary>
    /// The installed original's gettext catalogues (<c>locale/&lt;lang&gt;/LC_MESSAGES/SquadAutonomy.mo</c>: de, en_GB, es, fr, ja and more),
    /// for the window, the settings and the messages; the window's texts are the catalogues' msgids.
    /// </summary>
    static void AddTranslations(ModContext context)
    {
        if (OriginalFolder(context.Folder) is { } original) context.AddTranslations("SquadAutonomy", original);
        else context.Log("Squad Autonomy: the original mod (Workshop 3800729618) is not beside this one; its translations are not used");
    }

    /// <summary>
    /// The installed original's folder beside this mod's (Workshop id or mod name), when there.
    /// </summary>
    static string? OriginalFolder(string folder)
    {
        string own = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (Path.GetDirectoryName(own) is not { } parent) return null;

        return OriginalFolders
            .Select(name => Path.Combine(parent, name))
            .FirstOrDefault(path => Directory.Exists(path) && !string.Equals(path, own, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The installed original's SquadAutonomy.cfg beside this mod's folder (Workshop id or mod name), when there.
    /// </summary>
    static IEnumerable<string> OriginalConfigs(string folder) =>
        OriginalFolder(folder) is { } original && Path.Combine(original, "SquadAutonomy.cfg") is var cfg && File.Exists(cfg) ? [cfg] : [];
}

/// <summary>
/// The mod's settings: the original's options (its <c>SquadAutonomy.cfg</c> <c>&lt;Options&gt;</c>, defaults as shipped), labelled with the
/// msgids of its catalogues so they show in the game's language; the button's place (<c>BtnLeft</c>, <c>BtnTop</c>) has no row in the original's
/// settings window and keeps Meitou's English labels.
/// </summary>
public sealed class AutonomySettings
{
    /// <summary>
    /// The original's <c>ShowOnMain</c> (default true).
    /// </summary>
    [Setting("Show button on Mainbar", Hint = "Show AUT button on the Mainbar")]
    public bool ShowButton { get; set; } = true;

    /// <summary>
    /// The original's <c>ShowInSquad</c> (default true): an AUT button in each squad's box of the squad management screen.
    /// </summary>
    [Setting("Show button in Squad screen", Hint = "Show AUT button in the Squad Management screen")]
    public bool ShowInSquad { get; set; } = true;

    /// <summary>
    /// The original's <c>LockPosition</c> (default true): off, the AUT button can be dragged.
    /// </summary>
    [Setting("Lock button position", Hint = "LockPosition: off, drag the AUT button where you want it.", SubGroup = "Button")]
    public bool LockPosition { get; set; } = true;

    /// <summary>
    /// The original's <c>BtnLeft</c> (0.69): the button's left edge as a share of the screen width.
    /// </summary>
    [Setting("Button left", Hint = "BtnLeft: the AUT button's left edge, a share of the screen width.", Min = 0, Max = 1, Step = 0.001,
        SubGroup = "Button")]
    public float BtnLeft { get; set; } = 0.69f;

    /// <summary>
    /// The original's <c>BtnTop</c> (0.788): the button's top edge as a share of the screen height.
    /// </summary>
    [Setting("Button top", Hint = "BtnTop: the AUT button's top edge, a share of the screen height.", Min = 0, Max = 1, Step = 0.001,
        SubGroup = "Button")]
    public float BtnTop { get; set; } = 0.788f;

    /// <summary>
    /// The original's <c>BtnWidth</c> (1.5): the button's width in per cent of the screen width.
    /// </summary>
    [Setting("Button Width", Hint = "Set the button width relative to the screen width", Min = 0.5, Max = 20, Step = 0.1, SubGroup = "Button")]
    public float BtnWidth { get; set; } = 1.5f;

    /// <summary>
    /// The original's <c>BtnHeight</c> (3.58): the button's height in per cent of the screen height.
    /// </summary>
    [Setting("Button Height", Hint = "Set the button height relative to the screen height", Min = 0.5, Max = 20, Step = 0.01, SubGroup = "Button")]
    public float BtnHeight { get; set; } = 3.58f;

    /// <summary>
    /// The original's <c>BtnFontSize</c> (12): the caption's font height in pixels.
    /// </summary>
    [Setting("Button Font Size", Hint = "Set the button font size", Min = 6, Max = 40, SubGroup = "Button")]
    public int BtnFontSize { get; set; } = 12;

    /// <summary>
    /// The original's <c>EnableLogging</c> (default false).
    /// </summary>
    [Setting("Enable logging", Hint = "EnableLogging: write what the mod does to the mod log.", SubGroup = "Logging")]
    public bool EnableLogging { get; set; }

    /// <summary>
    /// The original's <c>LogSeverity</c> (None, Error, Warning, Info, Debug; default None).
    /// </summary>
    [Setting("Log Severity", Hint = "Select log severity level. Higher severity includes all lower ones.", SubGroup = "Logging")]
    public LogSeverity LogSeverity { get; set; }

    /// <summary>
    /// The original's <c>LogSpam</c> (default false).
    /// </summary>
    [Setting("Allow Spam", Hint = "Allow logging spam.", SubGroup = "Logging")]
    public bool LogSpam { get; set; }
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
/// The squad's plan: its AI packages by priority, the rest package (this port's: run outside the work hours; null none), the work hours
/// (0-24; start equal to end: no work time, as the original), the combat switches, the rest rules and the labour priorities (null: the
/// original's defaults) and the gate rules (<c>SquadConduct.GateInside</c> Stay Inside Gate, <c>CloseDoors</c> Close Gate).
/// </summary>
public sealed record SquadPlanOrder(int SquadId, IReadOnlyList<PlannedPackage> Packages, string? Rest, int StartHour, int EndHour,
    CombatSwitches Switches = CombatSwitches.Default, RestRules? Resting = null, LabourPriorities? Labouring = null,
    SquadConduct Gate = SquadConduct.None) : SimCommand;

/// <summary>
/// The squad's home (or with <paramref name="Work"/> its work building) becomes the building the HUD inspects (none inspected: no change), or
/// both none with <paramref name="Clear"/>.
/// </summary>
public sealed record SquadHomeOrder(int SquadId, bool Clear, bool Work = false) : SimCommand;

/// <summary>
/// The window's Enable Autonomy switch for a squad.
/// </summary>
public sealed record AutonomySwitchOrder(int SquadId, bool On) : SimCommand;
