using Meitou.Game.Host.Input;
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

    static ModSettingsGroup? s_group;
    static ModContext? s_context;

    /// <summary>Sets a setting as the settings page does, so it is kept with the player's settings (host thread).</summary>
    internal static void Keep(string key, object value) => s_group?.Settings.First(s => s.Key == key).Set(value);

    /// <summary>A line in the mod log (the original's diagnostics).</summary>
    internal static void Log(string text) => s_context?.Log(text);

    public void Initialize(ModContext context)
    {
        s_context = context;
        s_group = context.AddSettings(Settings);
        MapState.RestoreEditor();
        context.AddSystem("markers", _ => Running = new MarkerBook(context.Log), SystemAnchor.End);
        context.AddUi(new MapUi());
        context.AddFrame(frame => MapState.Pointer = frame.Pointer);
        context.AddSaveWritten(folder => Running?.WriteFile(folder));
        var chord = KeyModifiers.Ctrl | KeyModifiers.Alt;
        context.AddKeyAction("snapshot", "Map markers: diagnostics snapshot", _ => MapDiagnostics.Snapshot("hotkey"), Binding.Of(Key.F7).With(chord));
        context.AddKeyAction("probe", "Map markers: detailed log", _ => MapDiagnostics.Toggle("hotkey"), Binding.Of(Key.F8).With(chord));
        context.Log("Map markers initialized");
    }
}
