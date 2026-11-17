using System.Numerics;
using HarmonyLib;
using Meitou.Data.Ui;
using Meitou.Game.Host.Input;
using Meitou.Game.Ui;
using Meitou.Modding;

namespace Meitou.MapMarkers;

/// <summary>
/// The Harmony patches: a postfix on <c>WorldMap.AddMapContent</c> draws the overlay, one on <c>WorldMap.UpdateMap</c> sees the map close, a
/// prefix on <c>PlayerInterface.MouseDown</c> watches the map presses (and routes presses on the mod's widgets, which the map would take as a pan,
/// to the screen host), one on <c>PlayerInterface.UiKey</c> takes Delete, and one on <c>ScreenRouter.FaithfulAction</c> takes the widgets' events.
/// </summary>
static class MapHooks
{
    static bool s_mapWasShown;

    public static void Install(ModContext context)
    {
        Interface.Resolve();
        var harmony = context.Harmony;

        Patch(harmony, AccessTools.Method(Interface.WorldMap, "AddMapContent"), postfix: nameof(AfterAddMapContent));
        Patch(harmony, AccessTools.Method(Interface.WorldMap, "UpdateMap"), postfix: nameof(AfterUpdateMap));
        Patch(harmony, AccessTools.Method(Interface.Player, "MouseDown"), prefix: nameof(BeforeMouseDown));
        Patch(harmony, AccessTools.Method(Interface.Player, "UiKey"), prefix: nameof(BeforeUiKey));
        Patch(harmony, AccessTools.Method(Interface.Router, "FaithfulAction"), prefix: nameof(BeforeFaithfulAction));
    }

    static void Patch(Harmony harmony, System.Reflection.MethodInfo? target, string? prefix = null, string? postfix = null)
    {
        if (target is null) throw new MissingMethodException("Map markers: a patched method is missing");

        var before = prefix is null ? null : new HarmonyMethod(typeof(MapHooks), prefix);
        var after = postfix is null ? null : new HarmonyMethod(typeof(MapHooks), postfix);
        harmony.Patch(target, prefix: before, postfix: after);
    }

    #pragma warning disable IDE1006 // Harmony binds __instance / __result by name

    static void AfterAddMapContent(object __instance, string id, List<UiControl> controls)
    {
        if (id != MapOverlay.Screen) return;

        var player = Interface.PlayerOfMap(__instance);
        var screens = Interface.Screens(player);
        // The map fits its image only into a viewport with room (it gives the image its clip then).
        var image = controls.Find(c => c.Name == "MapImage");
        if (!MapMarkersMod.Settings.Enabled || image is not { Clip: { Width: > 0, Height: > 0 } })
        {
            Leave(screens);
            return;
        }

        if (!MapState.Active) MapState.Hidden(screens);

        MapState.TrackTyping(screens);
        var layout = MapOverlay.Measure(image, Interface.UiScale(player));
        MapState.Layout = layout;
        MapOverlay.Add(controls, layout, screens, Interface.Mouse(player));
    }

    /// <summary>The overview shows another tab, or the mod is off: no map to click on, and the label box lets go.</summary>
    static void Leave(MyGuiScreenHost screens)
    {
        MapState.Layout = null;
        MapState.StopTyping(screens);
    }

    static void AfterUpdateMap(object __instance)
    {
        bool shown = Interface.MapShown(__instance);
        if (s_mapWasShown && !shown) MapState.MapClosed(Interface.Screens(Interface.PlayerOfMap(__instance)));

        s_mapWasShown = shown;
    }

    static bool BeforeMouseDown(object __instance, MouseButton button, Vector2 p, ref bool __result)
    {
        if (!MapMarkersMod.Settings.Enabled || MapState.Layout is not { } layout) return true;

        var screens = Interface.Screens(__instance);
        var hit = screens.HitTest(p);
        if (hit is { Screen: MapOverlay.Screen } own && own.Control.Name.StartsWith(MapOverlay.Prefix, StringComparison.Ordinal))
        {
            __result = PressOwn(__instance, screens, button, p);
            return false;
        }

        if (OverMap(screens, hit, layout, p)) MapState.Press(screens, button, p, layout);

        return true;
    }

    #pragma warning restore IDE1006

    /// <summary>A press on the editor or the footer button: the left one goes to the screen host (a button's event, the label box's focus).</summary>
    static bool PressOwn(object player, MyGuiScreenHost screens, MouseButton button, Vector2 p)
    {
        Interface.SetMouse(player, p);
        if (button == MouseButton.Left) screens.PointerDown(p);

        return true;
    }

    /// <summary>Over the map image or one of the map's markers, on the open overview, with no modal window up.</summary>
    static bool OverMap(MyGuiScreenHost screens, (string Screen, UiControl Control)? hit, MapLayout layout, Vector2 p)
    {
        if (!layout.Viewport.Contains(p.X, p.Y) || screens.HasModal || hit is not { Screen: MapOverlay.Screen } over) return false;

        string name = over.Control.Name;
        return name == "MapImage" || name.StartsWith("map-", StringComparison.Ordinal);
    }

    #pragma warning disable IDE1006 // Harmony binds __instance / __result by name

    static bool BeforeUiKey(object __instance, Key key, ref bool __result)
    {
        if (key != Key.Delete || !MapState.DeleteKey(Interface.Screens(__instance))) return true;

        __result = true;
        return false;
    }

    static bool BeforeFaithfulAction(object __instance, MyGuiEvent e)
    {
        if (e.Screen != MapOverlay.Screen || !e.Widget.StartsWith(MapOverlay.Prefix, StringComparison.Ordinal)) return true;

        MapState.Action(Interface.Screens(Interface.PlayerOfRouter(__instance)), e);
        return false;
    }

    #pragma warning restore IDE1006
}
