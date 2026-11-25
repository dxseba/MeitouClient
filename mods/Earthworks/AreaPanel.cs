using System.Globalization;
using Meitou.Game.Ui;

namespace Meitou.Earthworks;

/// <summary>
/// The selected Earthworks building's panel: its area's width and depth in metres, each a button that widens it by 7.2 m, back to 14.4 m after
/// 115.2 m (the original's limits and its 7.2 m grid). The building's own rotation, chosen when placing it, turns the area.
/// </summary>
sealed class AreaPanel : IUiExtension
{
    const float Step = 72;

    public string Id => "dxseba.earthworks.ui";

    public void Register(UiRegistration registration) => registration.AddBuildingActions(Actions);

    static IEnumerable<UiBuildingAction> Actions(UiBuildingContext context)
    {
        var building = context.Building;
        if (building.Record is not { } record || !Clearings.Kinds.ContainsKey(record) || building.SiteId is not { } site || !building.Owned) return [];

        var size = AreaSystem.Published.TryGetValue(site, out var own) ? own : AreaSystem.Clamp(EarthworksMod.Settings.DefaultSize);
        return
        [
            new UiBuildingAction($"Width: {Metres(size.X)} m", "Widen the area by 7.2 m (back to 14.4 m after 115.2 m).",
                () => context.Enqueue(new SetAreaCommand(site, Next(size.X), size.Y) { Tick = context.Tick })),
            new UiBuildingAction($"Depth: {Metres(size.Y)} m", "Deepen the area by 7.2 m (back to 14.4 m after 115.2 m).",
                () => context.Enqueue(new SetAreaCommand(site, size.X, Next(size.Y)) { Tick = context.Tick })),
        ];
    }

    static float Next(float side) => side + Step > EarthworksSettings.Most + .5f ? EarthworksSettings.Least : side + Step;

    static string Metres(float units) => (units / 10).ToString("0.0", CultureInfo.InvariantCulture);
}
