using System.Numerics;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.WantedMap;

/// <summary>
/// Port of Wanted Map (workshop 3813761812), rebuilt from its description: select a character carrying "WANTED" posters and the world map shows
/// where the targets are: red on the spot, orange when their squad is far away, blue as a prisoner, grey where they died (for a game day).
/// </summary>
public sealed class WantedMapMod : IMod
{
    internal static readonly WantedSettings Settings = new();

    internal static volatile WantedSystem? Running;

    public void Initialize(ModContext context)
    {
        context.AddSettings(Settings);
        context.AddSystem("wanted", _ => Running = new WantedSystem(), SystemAnchor.End);
        context.AddUi(new WantedMarkers());
        context.Log("Wanted Map initialized");
    }
}

public sealed class WantedSettings
{
    [Setting("Enable Wanted Map", Hint = "Show the targets of carried wanted posters on the world map.")]
    public bool Enabled { get; set; } = true;

    [Setting("Posters of all characters", Hint = "On: the posters of every one of your characters count, whoever is selected.")]
    public bool AllCharacters { get; set; }
}

/// <summary>The map markers, filtered to the posters the selected characters carry.</summary>
sealed class WantedMarkers : IUiExtension
{
    static readonly Vector4 Red = new(.90f, .20f, .15f, 1), Orange = new(.95f, .55f, .15f, 1), Blue = new(.30f, .55f, .95f, 1),
        Grey = new(.60f, .60f, .60f, 1);

    public string Id => "dxseba.wantedmap.ui";

    public void Register(UiRegistration registration) => registration.AddMapMarkers(Markers);

    static IEnumerable<UiMapMarker> Markers(UiMapContext map)
    {
        if (!WantedMapMod.Settings.Enabled || WantedMapMod.Running?.Markers is not { Count: > 0 } markers) return [];

        var selected = map.Snapshot.Characters.Where(c => c.Selected).Select(c => c.Id).ToHashSet();
        var shown = markers.Where(m => WantedMapMod.Settings.AllCharacters || selected.Contains(m.Carrier))
            .DistinctBy(m => m.Name, StringComparer.OrdinalIgnoreCase);
        return shown.Select(Marker).ToList();
    }

    static UiMapMarker Marker(TargetMarker m) => m.State switch
    {
        TargetState.Exact => new(m.At, "Character", Red, m.Name, $"Wanted\t{m.Name}"),
        TargetState.Away => new(m.At, "Movement", Orange, m.Name, $"Wanted\t{m.Name}\nWhere\tapproximate (squad away)"),
        TargetState.Prisoner => new(m.At, "Character", Blue, m.Name, $"Wanted\t{m.Name}\nState\tprisoner"),
        _ => new(m.At, "Character", Grey, m.Name, $"Wanted\t{m.Name}\nState\tdead"),
    };
}
