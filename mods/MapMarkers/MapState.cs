using System.Numerics;
using Meitou.Game.Host.Input;
using Meitou.Game.Ui;

namespace Meitou.MapMarkers;

/// <summary>
/// The interface's side of the markers, on the interface thread: the selection, the map's last layout, the label being typed, and what clicks,
/// keys and the editor's buttons do (the original's <c>TryAddMarkerFromMiddleClick</c>, <c>TryHandleLeftClickSelectionOrMove</c>,
/// <c>TryDeselectMarkerFromRightClick</c> and <c>TryHandleMarkerKeyDown</c>).
/// </summary>
static class MapState
{
    /// <summary>The selected marker's id (0: none).</summary>
    static int s_selected;

    /// <summary>The marker whose label box had the typing focus last frame (0: none), to commit the label when the box loses it.</summary>
    static int s_typing;

    static MapMarkersSettings Settings => MapMarkersMod.Settings;

    /// <summary>The map as drawn last frame; null while the Map tab is not showing.</summary>
    public static MapLayout? Layout { get; set; }

    /// <summary>A new marker's label box takes the typing focus when it is next drawn.</summary>
    public static bool FocusPending { get; set; }

    public static MarkerBook? Book => MapMarkersMod.Running;

    /// <summary>The selected marker, if it still exists in the running world.</summary>
    public static Marker? Selected => s_selected == 0 ? null : Book?.Find(s_selected);

    public static bool IsSelected(Marker marker) => marker.Id == s_selected;

    /// <summary>Markers draw and take clicks: the mod is on and the markers are shown.</summary>
    public static bool Active => Settings.Enabled && Settings.MarkersVisible;

    public static void Deselect()
    {
        s_selected = 0;
        FocusPending = false;
    }

    /// <summary>The topmost shown marker under <paramref name="p"/> (later markers draw over earlier ones).</summary>
    public static Marker? MarkerAt(MapLayout layout, Vector2 p, Marker[] markers)
    {
        for (int i = markers.Length - 1; i >= 0; i--)
        {
            var box = layout.Box(markers[i], IsSelected(markers[i]));
            if (layout.Shows(box) && box.Contains(p.X, p.Y)) return markers[i];
        }

        return null;
    }

    /// <summary>A press on the map (the game still pans or orders as without the mod, as the original only watches the buttons).</summary>
    public static void Press(MyGuiScreenHost screens, MouseButton button, Vector2 p, MapLayout layout)
    {
        StopTyping(screens);
        if (!Active || Book is not { } book) return;

        var hovered = MarkerAt(layout, p, book.Snapshot());
        if (button == MouseButton.Middle && hovered is null) Add(book, layout.Normalized(p));
        else if (button == MouseButton.Left && hovered is not null) s_selected = hovered.Id;
        else if (button == MouseButton.Left && Selected is { } moved) book.Edit(moved, m => m.At = layout.Normalized(p));
        else if (button == MouseButton.Right) Deselect();
    }

    static void Add(MarkerBook book, Vector2 at)
    {
        s_selected = book.Add(at, Settings.DefaultType).Id;
        FocusPending = true;
    }

    /// <summary>Delete with the map showing and no box being typed in: removes the selected marker. True when it did.</summary>
    public static bool DeleteKey(MyGuiScreenHost screens)
    {
        if (!Active || Layout is null || Selected is null || screens.HasKeyboardFocus) return false;

        DeleteSelected();
        return true;
    }

    public static void DeleteSelected()
    {
        if (Book is { } book && s_selected != 0) book.Remove(s_selected);

        s_typing = 0;
        Deselect();
    }

    /// <summary>The map closed: the label in progress is committed and, with the setting, the editor closes.</summary>
    public static void MapClosed(MyGuiScreenHost screens)
    {
        Layout = null;
        StopTyping(screens);
        if (Settings.CloseEditorOnMapClose) Deselect();
    }

    /// <summary>The markers were switched off (or the mod disabled): the label box lets go of the keyboard.</summary>
    public static void Hidden(MyGuiScreenHost screens) => StopTyping(screens);

    /// <summary>Ends typing in the label box, if it has the focus, and commits the label.</summary>
    public static void StopTyping(MyGuiScreenHost screens)
    {
        if (Typing(screens)) screens.Blur();

        TrackTyping(screens);
    }

    public static bool Typing(MyGuiScreenHost screens) =>
        screens.Focus is { } focus && focus.Screen == MapOverlay.Screen && focus.Widget == MapOverlay.LabelBox;

    /// <summary>Once a frame: notes the marker being typed for; when the box lost the focus (Tab, a click elsewhere), commits its label.</summary>
    public static void TrackTyping(MyGuiScreenHost screens)
    {
        if (Typing(screens))
        {
            s_typing = s_selected;
            return;
        }

        if (s_typing != 0 && Book?.Find(s_typing) is { } typed) SetLabel(typed, typed.Label, trimEnd: true);

        s_typing = 0;
    }

    public static void SetLabel(Marker marker, string text, bool trimEnd)
    {
        string label = MarkerKinds.Clean(text, trimEnd);
        Book?.Edit(marker, m => m.Label = label);
    }

    /// <summary>An event of one of the mod's widgets on the overview: the footer toggle or the editor's controls.</summary>
    public static void Action(MyGuiScreenHost screens, MyGuiEvent e)
    {
        if (e.Widget == MapOverlay.ToggleButton) Toggle(screens);
        else if (Selected is not { } marker) return;
        else if (e.Widget == MapOverlay.TypeButton) Book?.Edit(marker, m => m.Kind = MarkerKinds.Next(m.Kind));
        else if (e.Widget == MapOverlay.DeleteButton) DeleteSelected();
        else if (e.Widget == MapOverlay.CloseButton) Close(screens);
        else if (Swatch(e.Widget) is { } swatch) Colour(screens, marker, swatch);
        else if (e.Widget == MapOverlay.LabelBox) LabelEvent(screens, marker, e);
    }

    static void Toggle(MyGuiScreenHost screens)
    {
        Settings.MarkersVisible = !Settings.MarkersVisible;
        if (!Settings.MarkersVisible) Hidden(screens);
    }

    static int? Swatch(string widget) =>
        widget.StartsWith(MapOverlay.SwatchPrefix, StringComparison.Ordinal)
        && int.TryParse(widget.AsSpan(MapOverlay.SwatchPrefix.Length), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out int i) && i < MarkerKinds.Swatches.Length
            ? i
            : null;

    /// <summary>A swatch: the label (as typed so far, when the box has the focus) gets the swatch's tag and is committed.</summary>
    static void Colour(MyGuiScreenHost screens, Marker marker, int swatch)
    {
        string text = Typing(screens) ? screens.GetText(MapOverlay.Screen, MapOverlay.LabelBox) : marker.Label;
        SetLabel(marker, MarkerKinds.WithColour(text, swatch), trimEnd: true);
        screens.SetText(MapOverlay.Screen, MapOverlay.LabelBox, marker.Label);
    }

    static void Close(MyGuiScreenHost screens)
    {
        StopTyping(screens);
        Deselect();
    }

    /// <summary>
    /// Typing keeps the cleaned label at once (the box shows the cleaned text); Enter commits it, trailing spaces trimmed, and deselects, as the
    /// original's confirm key does.
    /// </summary>
    static void LabelEvent(MyGuiScreenHost screens, Marker marker, MyGuiEvent e)
    {
        if (e.Action == MapOverlay.SubmitAction)
        {
            SetLabel(marker, e.Text, trimEnd: true);
            s_typing = 0;
            Deselect();
            return;
        }

        if (e.Action != "text") return;

        SetLabel(marker, e.Text, trimEnd: false);
        if (marker.Label != e.Text) screens.SetText(MapOverlay.Screen, MapOverlay.LabelBox, marker.Label);
    }
}
