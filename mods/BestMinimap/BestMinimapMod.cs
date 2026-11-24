using Meitou.Game.Host.Input;
using Meitou.Modding;

namespace Meitou.BestMinimap;

/// <summary>
/// Port of Best Minimap (QoL) (Workshop 3824087152): a minimap in the corner of the screen on the game's own map picture, with the characters
/// nearby, the discovered places, attack alerts, the Map-markers and WantedMap marks, the camera arrow and reach; a right click sends the
/// selected, a double click moves the camera.
/// </summary>
public sealed class BestMinimapMod : IMod
{
    static ModSettingsGroup? s_group;
    static ModContext? s_context;
    static MapPicture? s_picture;

    public void Initialize(ModContext context)
    {
        s_context = context;
        AddOriginalTranslations(context);
        s_group = context.AddSettings(Minimap.Settings);
        string cache = CacheFolder(context);
        Sprites.Init(cache);
        s_picture = new MapPicture(cache, context.Log);
        HostAccess.Log = context.Log;
        Alerts.Log = context.Log;
        ImagePatch.Install(context);
        WireInput(context);
        context.AddFrame(Frame);
        context.AddCombatHitFeed(Alerts.Hits);
        context.AddUi(new MinimapUi(s_picture, new OtherMods(context), context.Tr));
        context.Log("Best Minimap initialized");
    }

    static void WireInput(ModContext context)
    {
        MinimapInput.Keep = Keep;
        MinimapInput.Debug = text => { if (Minimap.Settings.Debug) context.Log(text); };
        MinimapInput.LastFrameEnqueue = context.Enqueue;
        MinimapInput.Tick = () => context.Tick;
        var ctrl = KeyModifiers.Ctrl;
        context.AddKeyAction("toggle", "Show / hide key", MinimapInput.Toggle, Binding.Of(Key.M).With(ctrl));
        s_zoomIn = context.AddKeyAction("zoom-in", "Zoom in key", _ => MinimapInput.Zoom(Minimap.ZoomIn), Binding.Of(Key.Equal));
        s_zoomOut = context.AddKeyAction("zoom-out", "Zoom out key", _ => MinimapInput.Zoom(Minimap.ZoomOut), Binding.Of(Key.Minus));
        context.AddKeyAction("bigger", "Bigger key", _ => MinimapInput.Resize(Minimap.SizeStep), Binding.Of(Key.Equal).With(ctrl));
        context.AddKeyAction("smaller", "Smaller key", _ => MinimapInput.Resize(-Minimap.SizeStep), Binding.Of(Key.Minus).With(ctrl));
    }

    static ModKeyAction? s_zoomIn, s_zoomOut;
    static bool s_pictureAsked;

    static void Frame(ModFrame frame)
    {
        Minimap.Frame(frame);
        if (s_zoomIn is { } zoomIn && s_zoomOut is { } zoomOut && Minimap.Shown) MinimapInput.Repeat(zoomIn, zoomOut);
        if (!s_pictureAsked && HostAccess.Ready) AskPicture();
        if (s_context is { } context) Alerts.Say(frame, context.Tr);
    }

    /// <summary>Starts reading the map picture once the host is known (a missing file leaves the background alone, logged).</summary>
    static void AskPicture()
    {
        s_pictureAsked = true;
        if (HostAccess.FindAsset("GUI_Map.dds") is { } source) s_picture?.Start(source, Minimap.Settings.MaxSourceSize, Minimap.Settings.SwapRedBlue);
        else s_context?.Log("could not read the map picture 'GUI_Map.dds': not found; the minimap shows no terrain");
    }

    /// <summary>Sets a setting as the settings page does, so it is kept with the player's settings (host thread).</summary>
    static void Keep(string key, object value) => s_group?.Settings.First(s => s.Key == key).Set(value);

    /// <summary>The port's cache (the scaled map picture and its white shapes), in the user's local application data.</summary>
    static string CacheFolder(ModContext context)
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (root.Length == 0) root = Path.GetTempPath();
        string folder = Path.Combine(root, "Meitou", "mod-cache", context.Id);
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>
    /// The original's gettext catalogues (<c>locale/&lt;language&gt;/LC_MESSAGES/best_minimap.mo</c>: en_GB, ru_RU, zh_CN), read from the installed
    /// original (Workshop item 3824087152, the port's required item; its folder beside this one, else the code-less mod "BestMinimap").
    /// </summary>
    static void AddOriginalTranslations(ModContext context)
    {
        if (OriginalFolder.Find(context) is { } folder) context.AddTranslations("best_minimap", folder);
    }
}
