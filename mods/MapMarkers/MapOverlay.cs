using System.Globalization;
using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Game.Ui;

namespace Meitou.MapMarkers;

/// <summary>
/// The mod's controls on the overview's Map tab, added after the map's own each frame: the markers (over the places, under the rest), the hover
/// label, the selected marker's editor and the Markers: On/Off button in the footer. Sizes are the original's pixels times the UI scale.
/// </summary>
static class MapOverlay
{
    public const string Screen = "overview", Prefix = "mapmarkers-";
    public const string ToggleButton = Prefix + "toggle", TypeButton = Prefix + "type", DeleteButton = Prefix + "delete";
    public const string CloseButton = Prefix + "close", LabelBox = Prefix + "label", SubmitAction = "mapmarkers:submit";

    const string Font = "Kenshi_StandardFont_Medium", ButtonSkin = "Kenshi_Button1Skin", PanelSkin = "Kenshi_GenericTextBoxFlatSkin";
    const string Hint = "MMB new marker | LMB move | Enter save\nDel or the button - delete | RMB deselect";

    /// <summary>The swatch buttons' names: this and the swatch's index in <see cref="MarkerKinds.Swatches"/>.</summary>
    public const string SwatchPrefix = Prefix + "colour-";

    /// <summary>The editor's size (the fork's 510 x 198) and its offsets from the viewport's top right; the footer button's size and margins.</summary>
    const float EditorWidth = 510, EditorHeight = 198, EditorTop = 48, EditorRight = 18;
    const float ToggleWidth = 108, ToggleHeight = 24, ToggleMargin = 12, ToggleBottom = 18;

    static readonly Vector4 Ink = new(.86f, .82f, .71f, 1), Hover = new(.32f, .22f, .16f, 1), PanelBack = new(.10f, .09f, .08f, .94f);
    static readonly Vector4 GlyphInk = new(.06f, .06f, .06f, 1), HoverText = Rgba(0xF5F5F5FF), HoverBack = Rgba(0x1A1A1AF2);
    static readonly Vector4 SwatchFrame = new(.05f, .05f, .05f, 1);

    static Vector4 Rgba(uint rgba) =>
        new(((rgba >> 24) & 255) / 255f, ((rgba >> 16) & 255) / 255f, ((rgba >> 8) & 255) / 255f, (rgba & 255) / 255f);

    /// <summary>The map's layout this frame from its image control (its bounds pan and zoom, its clip is the viewport).</summary>
    public static MapLayout Measure(UiControl image, float ui)
    {
        var viewport = image.Clip ?? image.Bounds;
        bool editor = MapState.Active && MapState.Selected is not null;
        return new(image.Bounds, viewport, ui, editor ? EditorBox(viewport, ui) : default);
    }

    static UiRect EditorBox(UiRect viewport, float ui)
    {
        float width = EditorWidth * ui, height = EditorHeight * ui;
        float x = Math.Max(viewport.X, viewport.X + viewport.Width - width - EditorRight * ui);
        float y = Math.Clamp(viewport.Y + EditorTop * ui, viewport.Y, Math.Max(viewport.Y, viewport.Y + viewport.Height - height));
        return new(x, y, width, height);
    }

    /// <summary>Adds the markers after the map's content and the rest on top of the screen's controls.</summary>
    public static void Add(List<UiControl> controls, MapLayout layout, MyGuiScreenHost screens, Vector2 mouse)
    {
        if (MapState.Active && MapState.Book is { } book)
        {
            var markers = book.Snapshot();
            int at = MapContentEnd(controls);
            controls.InsertRange(at, MarkerControls(layout, markers, mouse));
            if (MapState.Selected is { } selected) AddEditor(controls, layout, selected, screens);
        }

        controls.Add(Toggle(controls, layout));
    }

    /// <summary>Just past the map image and the map's own markers (named <c>map-</c>).</summary>
    static int MapContentEnd(List<UiControl> controls)
    {
        int image = controls.FindIndex(c => c.Name == "MapImage");
        int last = controls.FindLastIndex(c => c.Name.StartsWith("map-", StringComparison.Ordinal));
        return Math.Max(image, last) + 1;
    }

    static List<UiControl> MarkerControls(MapLayout layout, Marker[] markers, Vector2 mouse)
    {
        var controls = new List<UiControl>(markers.Length + 2);
        foreach (var marker in markers)
        {
            bool selected = MapState.IsSelected(marker);
            var box = layout.Box(marker, selected);
            if (layout.Shows(box)) controls.Add(MarkerControl(layout, marker, box, selected));
        }

        if (MapMarkersMod.Settings.ShowHoverLabels && MapState.MarkerAt(layout, mouse, markers) is { } hovered)
            controls.AddRange(HoverLabel(layout, hovered));

        return controls;
    }

    /// <summary>A square in the kind's colour with its glyph (the original's tinted button), slightly see-through unless selected.</summary>
    static UiControl MarkerControl(MapLayout layout, Marker marker, UiRect box, bool selected)
    {
        var colour = MarkerKinds.Colour(marker.Kind, selected) * new Vector4(1, 1, 1, selected ? 1 : .92f);
        return new(Prefix + "marker-" + marker.Id.ToString(CultureInfo.InvariantCulture), "Button", box, MarkerKinds.Glyph(marker.Kind), "", "",
            true, Props("Center"), colour, GlyphInk, Pickable: false, TextScale: layout.Ui, Clip: layout.Viewport);
    }

    /// <summary>"Kind: label" on a dark strip above the marker, kept inside the viewport (30 high, 40 to 480 wide: the fork measures the text, at most 480).</summary>
    static IEnumerable<UiControl> HoverLabel(MapLayout layout, Marker marker)
    {
        string caption = MarkerKinds.HoverCaption(marker);
        var box = layout.Box(marker, MapState.IsSelected(marker));
        float ui = layout.Ui, width = LabelWidth(caption) * ui, height = 30 * ui, view = layout.Viewport.X + layout.Viewport.Width;
        float x = Math.Clamp(box.X + box.Width / 2 - width / 2, layout.Viewport.X, Math.Max(layout.Viewport.X, view - width));
        float y = Math.Max(layout.Viewport.Y, box.Y - height - ui);
        var strip = new UiRect(x, y, width, height);
        var text = new UiRect(x + 8 * ui, y + 3 * ui, width - 16 * ui, height - 6 * ui);

        yield return Panel(Prefix + "hover", strip, "", HoverBack, layout.Viewport, pickable: false);
        yield return Text(Prefix + "hover-text", text, caption, HoverText, ui, layout.Viewport);
    }

    /// <summary>The original's width estimate for a caption: per character 4 (space), 6 (lower case), 7 (capitals, digits, ! + $) or 5.</summary>
    static float LabelWidth(string caption)
    {
        float width = 0;
        foreach (char c in MarkerKinds.WithoutTags(caption))
            width += c == ' ' ? 4 : char.IsLower(c) ? 6 : char.IsUpper(c) || char.IsDigit(c) || c is '!' or '+' or '$' ? 7 : 5;

        return Math.Clamp(width + 2 * 8 + 4, 40, 480);
    }

    /// <summary>The footer's Markers: On/Off, at the zoom buttons' height on the map's left (the viewport's foot when they are missing).</summary>
    static UiControl Toggle(List<UiControl> controls, MapLayout layout)
    {
        float ui = layout.Ui, width = ToggleWidth * ui, height = ToggleHeight * ui;
        var zoom = controls.Where(c => c.Name is "MapZoomInButton" or "MapZoomOutButton" or "MapCenterButton").ToList();
        float y = zoom.Count >= 2
            ? zoom.Average(c => c.Bounds.Y) + Math.Max(0, (zoom.Average(c => c.Bounds.Height) - height) / 2)
            : layout.Viewport.Y + layout.Viewport.Height - height - ToggleBottom * ui;
        var at = new UiRect(layout.Viewport.X + ToggleMargin * ui, y, width, height);
        string caption = MapMarkersMod.Settings.MarkersVisible ? "Markers: On" : "Markers: Off";
        return Button(ToggleButton, at, caption, Ink, ui, null);
    }

    static UiControl Button(string name, UiRect at, string caption, Vector4 ink, float ui, UiRect? clip) =>
        new(name, "Button", at, caption, "mapmarkers:" + name[Prefix.Length..], ButtonSkin, true, Props("Center"), default, ink,
            HoverColour: Hover, Pickable: true, TextScale: ui, Clip: clip);

    static UiControl Text(string name, UiRect at, string text, Vector4 ink, float scale, UiRect clip) =>
        new(name, "TextBox", at, text, "", "", true, Props("Left VCenter"), default, ink, Pickable: false, TextScale: scale, Clip: clip);

    static UiControl Panel(string name, UiRect at, string skin, Vector4 back, UiRect clip, bool pickable) =>
        new(name, "Widget", at, "", "", skin, true, new Dictionary<string, string>(), back, Pickable: pickable, Clip: clip);

    static Dictionary<string, string> Props(string align) => new() { ["FontName"] = Font, ["TextAlign"] = align };

    #region Editor

    /// <summary>
    /// The selected marker's editor in the MCM fork's layout (<b>Verified</b>, original DLL (decompiled) and its rdata: a 510 x 198 panel): the
    /// header with an "x" close button, the type button in the kind's colour beside Delete, the label box, the Colour row (a "-" button and ten
    /// swatches that set the label's leading colour tag) and the two-line controls hint.
    /// </summary>
    static void AddEditor(List<UiControl> controls, MapLayout layout, Marker marker, MyGuiScreenHost screens)
    {
        var e = layout.Editor;
        float ui = layout.Ui;
        UiRect At(float x, float y, float w, float h) => new(e.X + x * ui, e.Y + y * ui, w * ui, h * ui);

        controls.Add(Panel(Prefix + "editor", e, PanelSkin, PanelBack, layout.Viewport, pickable: true));
        controls.Add(Text(Prefix + "header", At(16, 16, 442, 18), MarkerKinds.Header(marker), Ink, ui, layout.Viewport));
        controls.Add(Button(CloseButton, At(470, 13, 24, 24), "x", Ink, ui, layout.Viewport));

        string kind = "Type: " + MarkerKinds.Name(marker.Kind);
        controls.Add(Button(TypeButton, At(16, 42, 346, 24), kind, MarkerKinds.Colour(marker.Kind, true), ui, layout.Viewport));
        controls.Add(Button(DeleteButton, At(374, 42, 120, 24), "Delete", Ink, ui, layout.Viewport));
        controls.Add(Text(Prefix + "label-title", At(16, 74, 80, 24), "Label", Ink, ui, layout.Viewport));
        var label = LabelControl(screens, At(108, 74, 386, 24), marker, layout);
        controls.Add(label);
        controls.Add(Text(Prefix + "colour-title", At(16, 106, 80, 24), "Colour", Ink, ui, layout.Viewport));
        AddSwatches(controls, layout, At);

        controls.Add(Text(Prefix + "hint", At(16, 142, 478, 40), Hint, Ink * new Vector4(1, 1, 1, .8f), ui, layout.Viewport));

        if (MapState.FocusPending) FocusLabel(screens, label);
    }

    /// <summary>The Colour row: "-" (no tag), then each colour as an 18 px square in a 22 px near-black frame, 28 px apart from x 108.</summary>
    static void AddSwatches(List<UiControl> controls, MapLayout layout, Func<float, float, float, float, UiRect> at)
    {
        for (int i = 0; i < MarkerKinds.Swatches.Length; i++)
        {
            float x = 108 + 28 * i;
            string name = SwatchPrefix + i.ToString(CultureInfo.InvariantCulture);
            if (MarkerKinds.Swatches[i].Length == 0)
            {
                controls.Add(Button(name, at(x, 107, 22, 22), "-", Ink, layout.Ui, layout.Viewport));
                continue;
            }

            controls.Add(Button(name, at(x, 107, 22, 22), "", Ink, layout.Ui, layout.Viewport) with { Skin = "", Background = SwatchFrame });
            controls.Add(Panel(name + "-fill", at(x + 2, 109, 18, 18), "", Rgba(MarkerKinds.SwatchRgba(i)), layout.Viewport, pickable: false));
        }
    }

    /// <summary>
    /// The label box: the marker's label, or while it has the typing focus the text being typed with the caret (a box a screen adds gets no caret
    /// from the host). Enter sends <see cref="SubmitAction"/>.
    /// </summary>
    static UiControl LabelControl(MyGuiScreenHost screens, UiRect at, Marker marker, MapLayout layout)
    {
        var props = Props("Left VCenter");
        props["MaxTextLength"] = MarkerKinds.LabelMax.ToString(CultureInfo.InvariantCulture);
        props["SubmitAction"] = SubmitAction;

        string text = marker.Label;
        if (screens.Focus is { } f && f.Screen == Screen && f.Widget == LabelBox)
        {
            text = screens.GetText(Screen, LabelBox);
            props["Focused"] = "true";
            props["CaretIndex"] = f.Caret.ToString(CultureInfo.InvariantCulture);
            props["SelectionStart"] = f.SelectionStart.ToString(CultureInfo.InvariantCulture);
            props["SelectionLength"] = f.SelectionLength.ToString(CultureInfo.InvariantCulture);
        }
        else screens.SetText(Screen, LabelBox, marker.Label);

        return new(LabelBox, "EditBox", at, text, "", "Kenshi_EditBox", true, props, new(.04f, .04f, .04f, .9f), Ink, Pickable: true,
            TextScale: layout.Ui, Clip: layout.Viewport);
    }

    /// <summary>A marker just added takes the typing focus in its label box, so a label can be typed at once (the original's focus).</summary>
    static void FocusLabel(MyGuiScreenHost screens, UiControl box)
    {
        MapState.FocusPending = false;
        Interface.Focus(screens, Screen, box);
    }

    #endregion
}
