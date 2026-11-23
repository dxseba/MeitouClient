using System.Globalization;
using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Game.Ui;

namespace Meitou.MapMarkers;

/// <summary>
/// The mod's controls on the overview's Map tab, built each frame as screen controls over the map image: the markers, the hover label and the
/// selected marker's editor; and the Markers: On/Off button in the footer. Sizes are the original's pixels times the UI scale.
/// </summary>
static class MapOverlay
{
    public const string Screen = "overview";

    const string Hint = "MMB new marker | LMB move | Enter save\nDel or the button - delete | RMB deselect";
    const float ToggleWidth = 108, ToggleHeight = 24, ToggleMargin = 12;

    public static Vector4 Rgba(uint rgba) =>
        new(((rgba >> 24) & 255) / 255f, ((rgba >> 16) & 255) / 255f, ((rgba >> 8) & 255) / 255f, (rgba & 255) / 255f);

    /// <summary>The markers, hover label and editor over the map image (the anchor), or nothing while the mod is off or the map is not shown.</summary>
    public static UiNode? Build(UiScreenContext context, MapStyles styles, MapActions actions)
    {
        if (!MapMarkersMod.Settings.Enabled || context.Map is not { } view || !MapState.Active || MapState.Book is not { } book) return null;

        var layout = MapLayout.Of(view);
        var root = new UiNode("map", UiNodeKind.Panel) { Pickable = false };
        var markers = book.Snapshot();
        foreach (var marker in markers) AddMarker(root, layout, marker, styles);

        if (MapMarkersMod.Settings.ShowHoverLabels && MapState.MarkerAt(layout, MapState.Pointer, markers) is { } hovered)
            AddHoverLabel(root, layout, hovered, styles);
        if (MapState.Selected is { } selected) AddEditor(root, layout, selected, styles, actions);

        return root;
    }

    /// <summary>A child of a node laid out over <paramref name="origin"/>, at the screen rectangle <paramref name="at"/>.</summary>
    static UiPlacement Place(UiRect origin, UiRect at) => new(at.Width, at.Height, at.X - origin.X, at.Y - origin.Y);

    /// <summary>A square in the kind's colour with its glyph (the original's tinted button), slightly see-through unless selected.</summary>
    static void AddMarker(UiNode root, MapLayout layout, Marker marker, MapStyles styles)
    {
        bool selected = MapState.IsSelected(marker);
        var box = layout.Box(marker, selected);
        if (!layout.Shows(box)) return;

        root.Children.Add(new UiNode("marker-" + marker.Id.ToString(CultureInfo.InvariantCulture), UiNodeKind.Button)
        {
            Placement = Place(layout.Image, box), Style = styles.Marker(marker.Kind, selected), Text = MarkerKinds.Glyph(marker.Kind),
            Pickable = false, TextScale = layout.Ui,
        });
    }

    /// <summary>"Kind: label" on a dark strip above the marker, kept inside the viewport (30 high, 40 to 480 wide: the fork measures the text).</summary>
    static void AddHoverLabel(UiNode root, MapLayout layout, Marker marker, MapStyles styles)
    {
        string caption = MarkerKinds.HoverCaption(marker);
        var box = layout.Box(marker, MapState.IsSelected(marker));
        float ui = layout.Ui, width = LabelWidth(caption) * ui, height = 30 * ui, view = layout.Viewport.X + layout.Viewport.Width;
        float x = Math.Clamp(box.X + box.Width / 2 - width / 2, layout.Viewport.X, Math.Max(layout.Viewport.X, view - width));
        float y = Math.Max(layout.Viewport.Y, box.Y - height - ui);
        var (text, back) = MapMarkersMod.Settings.HoverColours();

        var strip = new UiRect(x, y, width, height);
        root.Children.Add(new UiNode("hover", UiNodeKind.Panel) { Placement = Place(layout.Image, strip), Style = styles.Strip(back), Pickable = false });
        root.Children.Add(new UiNode("hover-text", UiNodeKind.Label)
        {
            Placement = Place(layout.Image, new(x + 8 * ui, y + 3 * ui, width - 16 * ui, height - 6 * ui)), Style = styles.Text, Text = caption,
            TextColour = text, TextScale = ui,
        });
    }

    /// <summary>The original's width estimate for a caption: per character 4 (space), 6 (lower case), 7 (capitals, digits, ! + $) or 5.</summary>
    static float LabelWidth(string caption)
    {
        float width = 0;
        foreach (char c in MarkerKinds.WithoutTags(caption))
            width += c == ' ' ? 4 : char.IsLower(c) ? 6 : char.IsUpper(c) || char.IsDigit(c) || c is '!' or '+' or '$' ? 7 : 5;

        return Math.Clamp(width + 2 * 8 + 4, 40, 480);
    }

    /// <summary>The footer's Markers: On/Off, level with the zoom buttons (laid out just left of the zoom-in button) at the map viewport's left.</summary>
    public static UiNode? Toggle(UiScreenContext context, MapStyles styles, MapActions actions)
    {
        if (!MapMarkersMod.Settings.Enabled || context.Map is not { } view) return null;

        float ui = view.UiScale, width = ToggleWidth * ui, height = ToggleHeight * ui;
        var parent = context.Anchor with { X = context.Anchor.X - context.Anchor.Width };
        var at = new UiRect(view.Viewport.X + ToggleMargin * ui, parent.Y + (parent.Height - height) / 2, width, height);
        string caption = MapMarkersMod.Settings.MarkersVisible ? "Markers: On" : "Markers: Off";
        return new UiNode("toggle", UiNodeKind.Button)
        {
            Placement = Place(parent, at), Style = styles.Button, Text = caption, Action = actions.Toggle, TextScale = ui,
        };
    }

    #region Editor

    /// <summary>
    /// The selected marker's editor in the MCM fork's layout (<b>Verified</b>, original DLL (decompiled) and its rdata: a 510 x 198 panel): the
    /// header with an "x" close button, the type button in the kind's colour beside Delete, the label box, the Colour row (a "-" button and ten
    /// swatches that set the label's leading colour tag) and the two-line controls hint. Dragging the panel moves it; a new marker's label box
    /// takes the keys.
    /// </summary>
    static void AddEditor(UiNode root, MapLayout layout, Marker marker, MapStyles styles, MapActions actions)
    {
        var e = layout.Editor;
        float ui = layout.Ui;
        UiPlacement At(float x, float y, float w, float h) => Place(layout.Image, new(e.X + x * ui, e.Y + y * ui, w * ui, h * ui));
        UiNode Node(string id, UiNodeKind kind, UiPlacement at, string style, string text = "", string action = "") =>
            new(id, kind) { Placement = at, Style = style, Text = text, Action = action, TextScale = ui };

        var c = root.Children;
        c.Add(new UiNode("editor", UiNodeKind.Panel)
        {
            Placement = Place(layout.Image, e), Style = styles.Panel, Pointer = UiPointerEvents.Drag, Action = actions.Drag,
        });
        c.Add(Node("header", UiNodeKind.Label, At(16, 16, 442, 18), styles.Text, MarkerKinds.Header(marker)));
        c.Add(Node("close", UiNodeKind.Button, At(470, 13, 24, 24), styles.Button, "x", actions.Close));
        string type = "Type: " + MarkerKinds.Name(marker.Kind);
        c.Add(Node("type", UiNodeKind.Button, At(16, 42, 346, 24), styles.Type(marker.Kind), type, actions.Type));
        c.Add(Node("delete", UiNodeKind.Button, At(374, 42, 120, 24), styles.Button, "Delete", actions.Delete));
        c.Add(Node("label-title", UiNodeKind.Label, At(16, 74, 80, 24), styles.Text, "Label"));
        c.Add(new UiNode("label", UiNodeKind.TextBox)
        {
            Placement = At(108, 74, 386, 24), Style = styles.Box, Text = marker.Label, Action = actions.Label, MaxLength = MarkerKinds.LabelMax,
            TextScale = ui, Focus = MapState.FocusLabel,
        });
        c.Add(Node("colour-title", UiNodeKind.Label, At(16, 106, 80, 24), styles.Text, "Colour"));
        AddSwatches(c, At, styles, actions, ui);
        c.Add(Node("hint", UiNodeKind.Label, At(16, 142, 478, 40), styles.Hint, Hint));
    }

    /// <summary>The Colour row: "-" (no tag), then each colour as an 18 px square in a 22 px near-black frame, 28 px apart from x 108.</summary>
    static void AddSwatches(List<UiNode> c, Func<float, float, float, float, UiPlacement> at, MapStyles styles, MapActions actions, float ui)
    {
        for (int i = 0; i < MarkerKinds.Swatches.Length; i++)
        {
            float x = 108 + 28 * i;
            string arg = i.ToString(CultureInfo.InvariantCulture);
            bool none = MarkerKinds.Swatches[i].Length == 0;
            c.Add(new UiNode("colour-" + arg, UiNodeKind.Button)
            {
                Placement = at(x, 107, 22, 22), Style = none ? styles.Button : styles.Frame, Text = none ? "-" : "", Action = actions.Swatch,
                Argument = arg, TextScale = ui,
            });
            if (none) continue;

            c.Add(new UiNode("colour-fill-" + arg, UiNodeKind.Panel) { Placement = at(x + 2, 109, 18, 18), Style = styles.Swatch(i), Pickable = false });
        }
    }

    #endregion
}
