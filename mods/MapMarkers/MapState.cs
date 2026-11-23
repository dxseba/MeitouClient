using System.Numerics;
using Meitou.Game.Host.Input;

namespace Meitou.MapMarkers;

/// <summary>
/// The interface's side of the markers, on the host's thread: the selection, the pointer, and what clicks, keys and the editor's buttons do (the
/// original's <c>TryAddMarkerFromMiddleClick</c>, <c>TryHandleLeftClickSelectionOrMove</c>, <c>TryDeselectMarkerFromRightClick</c> and
/// <c>TryHandleMarkerKeyDown</c>).
/// </summary>
static class MapState
{
    /// <summary>The selected marker's id (0: none).</summary>
    static int s_selected;

    static MapMarkersSettings Settings => MapMarkersMod.Settings;

    /// <summary>The pointer as of this frame (<see cref="Meitou.Modding.ModFrame.Pointer"/>), for the hover label.</summary>
    public static Vector2 Pointer { get; set; }

    public static MarkerBook? Book => MapMarkersMod.Running;

    /// <summary>The selected marker, if it still exists in the running world.</summary>
    public static Marker? Selected => s_selected == 0 ? null : Book?.Find(s_selected);

    public static bool IsSelected(Marker marker) => marker.Id == s_selected;

    /// <summary>Markers draw and take clicks: the mod is on and the markers are shown.</summary>
    public static bool Active => Settings.Enabled && Settings.MarkersVisible;

    /// <summary>Closes the editor; the label typed so far is kept with its trailing spaces trimmed (the original's commit).</summary>
    public static void Deselect()
    {
        if (Selected is { } marker) SetLabel(marker, marker.Label, trimEnd: true);

        s_selected = 0;
    }

    static void Select(int id)
    {
        Deselect();
        s_selected = id;
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
    public static void Press(MouseButton button, Vector2 p, MapLayout layout)
    {
        if (!Active || Book is not { } book) return;

        var hovered = MarkerAt(layout, p, book.Snapshot());
        if (button == MouseButton.Middle && hovered is null) Select(book.Add(layout.Normalized(p), Settings.DefaultType).Id);
        else if (button == MouseButton.Left && hovered is not null) Select(hovered.Id);
        else if (button == MouseButton.Left && Selected is { } moved) book.Edit(moved, m => m.At = layout.Normalized(p));
        else if (button == MouseButton.Right) Deselect();
    }

    /// <summary>Delete with the map showing and no box being typed in: removes the selected marker. True when it did.</summary>
    public static bool DeleteKey()
    {
        if (!Active || Selected is null) return false;

        DeleteSelected();
        return true;
    }

    public static void DeleteSelected()
    {
        if (Book is { } book && s_selected != 0) book.Remove(s_selected);

        s_selected = 0;
    }

    /// <summary>The map closed: the label in progress is committed and, with the setting, the editor closes.</summary>
    public static void MapClosed()
    {
        if (Settings.CloseEditorOnMapClose) Deselect();
        else if (Selected is { } marker) SetLabel(marker, marker.Label, trimEnd: true);
    }

    static void SetLabel(Marker marker, string text, bool trimEnd)
    {
        string label = MarkerKinds.Clean(text, trimEnd);
        Book?.Edit(marker, m => m.Label = label);
    }

    /// <summary>The footer's Markers: On/Off.</summary>
    public static void Toggle() => Settings.MarkersVisible = !Settings.MarkersVisible;

    /// <summary>The type button: the next kind in the cycle.</summary>
    public static void CycleType()
    {
        if (Selected is { } marker) Book?.Edit(marker, m => m.Kind = MarkerKinds.Next(m.Kind));
    }

    /// <summary>A swatch: the label gets the swatch's tag (or loses it for "-") and is committed.</summary>
    public static void Colour(int swatch)
    {
        if (swatch < 0 || swatch >= MarkerKinds.Swatches.Length || Selected is not { } marker) return;

        SetLabel(marker, MarkerKinds.WithColour(marker.Label, swatch), trimEnd: true);
    }

    /// <summary>Typing in the label box keeps the cleaned label at once; the box takes the cleaned text from the next build.</summary>
    public static void Typed(string text)
    {
        if (Selected is { } marker) SetLabel(marker, text, trimEnd: false);
    }
}
