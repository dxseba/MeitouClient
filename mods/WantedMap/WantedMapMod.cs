using System.Numerics;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.WantedMap;

/// <summary>
/// Port of Wanted Map (workshop 3813761812): select a character carrying wanted posters and the world map shows where their targets are: red on
/// the spot, orange when their squad is far away, yellow at home before they first appeared, blue as a prisoner, grey where they died.
/// </summary>
public sealed class WantedMapMod : IMod
{
    internal static readonly WantedSettings Settings = new();

    internal static volatile WantedSystem? Running;

    public void Initialize(ModContext context)
    {
        context.AddSettings(Settings);

        var stopWords = Settings.StopWords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(w => w.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        var files = new OriginalFiles(context);
        var posters = PosterIndex.Build(context.Data, files, stopWords);
        var towns = new TownPlaces(context.Population);
        context.AddSystem("wanted", _ => Running = new WantedSystem(posters, towns, Settings), SystemAnchor.End);
        context.AddUi(new WantedMarkers(new PosterBadge(files)));
        context.Log($"Wanted Map initialized: {posters.Count} posters matched");
    }
}

/// <summary>
/// The original's <c>WantedMap.ini</c> keys (<c>WantedMap.defaults.ini</c> defaults; labels and hints from its en_GB catalog).
/// </summary>
public sealed class WantedSettings
{
    /// <summary>
    /// <c>Enabled</c>, default 1.
    /// </summary>
    [Setting("Enabled", Hint = "Markers on the big map for the targets of wanted posters held by the selected characters.")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// <c>AllInventories</c>, default 0.
    /// </summary>
    [Setting("Posters of the whole squad",
        Hint = "Look for wanted posters in the inventories of all your characters. Off - only the selected characters (the navigator).")]
    public bool AllCharacters { get; set; }

    /// <summary>
    /// <c>ShowLabels</c>, default 1.
    /// </summary>
    [Setting("Names next to markers", Hint = "The target's name, and dead or imprisoned, next to its marker on the map.")]
    public bool ShowLabels { get; set; } = true;

    /// <summary>
    /// <c>TownFallback</c>, default 1.
    /// </summary>
    [Setting("Mark the home of targets not seen yet",
        Hint = "If the target's squad has not appeared in the world yet: a yellow marker on the town or camp where it lives.")]
    public bool TownFallback { get; set; } = true;

    /// <summary>
    /// <c>RememberSeen</c>, default 1 (kept in the save here, per playthrough).
    /// </summary>
    [Setting("Remember seen and killed targets",
        Hint = "Remembers which targets were already in the world and who was killed, where and when. Kept in the save.")]
    public bool RememberSeen { get; set; } = true;

    /// <summary>
    /// <c>DeadMarkerHours</c>, default 24; 0 shows no killed targets.
    /// </summary>
    [Setting("Show killed targets for, game hours",
        Hint = "How many game hours a grey marker stays where a target was killed. 0 - killed targets are not shown.", Min = 0, Max = 240, Step = 1)]
    public int DeadMarkerHours { get; set; } = 24;

    /// <summary>
    /// <c>[Match] StopWords</c>: words that do not link a reward to a name. Read when the posters are matched, at start.
    /// </summary>
    [Setting("Stop words", Hint = "Comma-separated words that do not count when a poster's reward is matched to a name. Applies at the next start.",
        MaxLength = 1024)]
    public string StopWords { get; set; } = "the,of,and,wanted,king,boss,leader,из,банды,и,король,лидер,лидеры,глава";
}

/// <summary>
/// The map markers, filtered to the posters the selected characters carry.
/// </summary>
sealed class WantedMarkers(PosterBadge badge) : IUiExtension
{
    static readonly Vector4 Red = new(.90f, .20f, .15f, 1), Orange = new(.95f, .55f, .15f, 1), Yellow = new(.95f, .85f, .20f, 1),
        Blue = new(.30f, .55f, .95f, 1), Grey = new(.60f, .60f, .60f, 1);

    /// <summary>
    /// The last non-empty selection (host thread).
    /// </summary>
    static HashSet<CharacterId> s_lastSelected = [];

    public string Id => "dxseba.wantedmap.ui";

    public void Register(UiRegistration registration)
    {
        registration.AddMapMarkers(Markers);
        foreach (var screen in ItemScreens) registration.AddScreenControls(screen, UiScreens.ItemCells, UiAnchorPlacement.Inside, badge.Build);
    }

    /// <summary>
    /// The character windows with item cells (the original badges every inventory icon).
    /// </summary>
    static readonly string[] ItemScreens = ["inventory", "bag", "loot", "loot-bag"];

    static IEnumerable<UiMapMarker> Markers(UiMapContext map)
    {
        var settings = WantedMapMod.Settings;
        if (!settings.Enabled || WantedMapMod.Running?.Markers is not { Count: > 0 } markers) return [];

        // Nothing selected keeps the markers of the last selection (the original's README).
        var selected = map.Snapshot.Characters.Where(c => c.Selected).Select(c => c.Id).ToHashSet();
        if (selected.Count > 0) s_lastSelected = selected;
        else selected = s_lastSelected;

        var shown = markers.Where(m => settings.AllCharacters || selected.Contains(m.Carrier)).DistinctBy(m => m.Target, StringComparer.Ordinal);
        return shown.Select(m => Marker(m, settings.ShowLabels)).ToList();
    }

    /// <summary>
    /// The marker; its label the name and the state word of the original's catalog (dead, imprisoned, approx., lives here).
    /// </summary>
    static UiMapMarker Marker(TargetMarker m, bool labels)
    {
        var (group, colour, word) = m.State switch
        {
            TargetState.Exact => ("Character", Red, ""),
            TargetState.Away => ("Movement", Orange, "approx."),
            TargetState.Home => ("Movement", Yellow, "lives here"),
            TargetState.Prisoner => ("Character", Blue, "imprisoned"),
            _ => ("Character", Grey, "dead"),
        };

        string label = word.Length > 0 ? $"{m.Name} ({word})" : m.Name;
        string tooltip = word.Length > 0 ? $"Wanted\t{m.Name}\nState\t{word}" : $"Wanted\t{m.Name}";
        return new(m.At, group, colour, labels ? label : null, tooltip);
    }
}
