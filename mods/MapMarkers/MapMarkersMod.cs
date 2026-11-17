using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.MapMarkers;

/// <summary>
/// Port of Emkej8's Map markers (MCM fork, workshop 3813852161): the player's own markers on the world map, with a type and a short label, kept
/// per save; added, selected, moved and removed with the mouse and Delete, edited in a panel on the map, shown or hidden from the map's footer.
/// </summary>
public sealed class MapMarkersMod : IMod
{
    internal static readonly MapMarkersSettings Settings = new();

    /// <summary>The running world's markers; null before a world starts. Each new game or load makes a fresh book (filled by its save).</summary>
    internal static volatile MarkerBook? Running;

    public void Initialize(ModContext context)
    {
        context.AddSettings(Settings);
        context.AddSystem("markers", _ => Running = new MarkerBook(), SystemAnchor.End);
        MapHooks.Install(context);
        context.Log("Map markers initialized");
    }
}
