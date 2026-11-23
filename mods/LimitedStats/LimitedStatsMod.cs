using System.Globalization;
using Meitou.Content;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.LimitedStats;

/// <summary>
/// Port of the original's talents and caps (this folder's README): every character gets a talent (Normal, Lazy, Loser, Diligent, Genius,
/// Superhuman, or an ini entry's) setting XP rates and cap breaks; each stat's cap follows the race's XP multiplier for it; XP stops at the cap and
/// stats above it are pulled back. The original's <c>LimitTalent.ini</c> and <c>StatGUI.ini</c> are read from this folder, else from the
/// installed original mod (Workshop item 3807762285, folder "Limit Talent"); none of their values are shipped.
/// </summary>
public sealed class LimitedStatsMod : IMod
{
    const string OriginalItem = "3807762285";
    const string OriginalName = "Limit Talent";
    const int LoggedTalents = 30;

    public void Initialize(ModContext context)
    {
        var settings = new LimitedStatsSettings();
        var config = TalentConfig.Load(FindFile(context, "LimitTalent.ini"), settings, context.Log);
        var gui = GuiConfig.Load(FindFile(context, "StatGUI.ini"), context.Log);
        context.AddSettings(settings);

        int logged = 0;
        void Log(string line)
        {
            if (settings.WriteLog && Interlocked.Increment(ref logged) <= LoggedTalents) context.Log(line);
        }

        var builder = new TalentBuilder(config, context.Data, Log);
        var board = new CapBoard();
        var purpose = context.RngPurpose;
        context.AddSystem("cap", _ => NewSystem(settings, config, builder, board, purpose), SystemAnchor.End);
        context.AddUi(new CapPanelLines(settings, board));
        context.AddUi(new StatsWindowOverrides(board, gui));
        context.Log("Limited Stats initialized");
    }

    /// <summary>The cap system for a new world; the last world's entries are forgotten.</summary>
    static StatCapSystem NewSystem(LimitedStatsSettings settings, TalentConfig config, TalentBuilder builder, CapBoard board,
        RngPurpose purpose)
    {
        board.Clear();
        return new StatCapSystem(settings, config, builder, board, purpose);
    }

    /// <summary><paramref name="name"/> in this mod's folder, else in the original mod's (its Workshop folder beside this one, or found by name).</summary>
    static string? FindFile(ModContext context, string name)
    {
        foreach (string folder in Folders(context))
        {
            string path = Path.Combine(folder, name);
            if (File.Exists(path)) return path;
        }

        context.Log($"{name} not found (the original mod {OriginalName}, Workshop {OriginalItem}, is not installed): built-in defaults");
        return null;
    }

    static IEnumerable<string> Folders(ModContext context)
    {
        yield return context.Folder;

        if (Path.GetDirectoryName(Path.GetFullPath(context.Folder)) is { } parent) yield return Path.Combine(parent, OriginalItem);

        if (OriginalFolder(context) is { } found) yield return found;
    }

    static string? OriginalFolder(ModContext context)
    {
        try
        {
            return GameInstall.Locate() is { } install ? ModList.Discover(install).FirstOrDefault(m => m.Name == OriginalName)?.Folder : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            context.Log($"mod folders not read: {e.Message}");
            return null;
        }
    }
}

/// <summary>
/// Port option: the cap in the HUD's character panel (<see cref="UiRegistration.AddCharacterPanelLines"/>) for a player character: a "Stat cap"
/// line with the lowest and highest cap (its tooltip names the talent and the stats at their cap) and, with Panel lines at 2, an "At cap" count.
/// </summary>
sealed class CapPanelLines(LimitedStatsSettings settings, CapBoard board) : IUiExtension
{
    public string Id => "dxseba.limitedstats.panel";

    public void Register(UiRegistration registration) => registration.AddCharacterPanelLines(Lines);

    IEnumerable<UiCharacterLine> Lines(UiCharacterContext context)
    {
        if (settings.PanelLines <= 0 || board.Entry(context.Id) is not { IsPlayer: true } entry) return [];

        var caps = StatNames.Stats.Where(s => entry.Caps[(int)s] >= 0).ToArray();
        if (caps.Length == 0) return [];

        var atCap = caps.Where(s => context.Character.AllSkills.ElementAtOrDefault((int)s) >= entry.Caps[(int)s] - 0.0001f).ToArray();
        string names = atCap.Length == 0 ? "none" : string.Join(", ", atCap.Select(StatNames.NameOf));
        var cap = new UiCharacterLine("Stat cap", Range(caps.Min(s => entry.Caps[(int)s]), caps.Max(s => entry.Caps[(int)s])),
            Tooltip: $"Talent: {entry.Type}\nAt cap: {names}");
        if (settings.PanelLines < 2) return [cap];

        string count = atCap.Length == 0 ? "none" : atCap.Length.ToString(CultureInfo.InvariantCulture);
        return [cap, new UiCharacterLine("At cap", count, Tooltip: names)];
    }

    static string Range(float low, float high) => low == high
        ? low.ToString("0.#", CultureInfo.InvariantCulture)
        : string.Create(CultureInfo.InvariantCulture, $"{low:0.#}-{high:0.#}");
}
