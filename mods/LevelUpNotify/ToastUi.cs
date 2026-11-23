using System.Globalization;
using System.Numerics;
using Meitou.Game.Ui;

namespace Meitou.LevelUpNotify;

/// <summary>
/// Draws the toasts as the original lays them out: boxes of 600 x 52 (at least 1.8 font heights) with 6 between, stacked oldest first from the
/// chosen corner, edge or centre at the offset, every size and the font scaled by screen height / 2160 and the UI scale; text in the box's column
/// alignment, in the ARGB colour, its alpha times the opacity and the fade. Drawn also with the interface hidden unless
/// <see cref="LevelUpSettings.HideWithGameUi"/>.
/// </summary>
public sealed class ToastUi(LevelUpSettings settings) : IUiExtension
{
    const float ReferenceHeight = 2160, Width = 600, Height = 52, Spacing = 6, FontBoxes = 1.8f;

    /// <summary>The standard font's own height in pixels (20 pt at 50 dpi), what a text scale of 1 draws.</summary>
    const float FontPixels = 20 * 50 / 72f;

    /// <summary>The colour presets of the original's panel (RGB).</summary>
    static readonly (string Name, uint Rgb)[] Colours =
    [
        ("Yellow", 0xFFFF00), ("White", 0xFFFFFF), ("Green", 0x00FF88), ("Cyan", 0x00FFFF),
        ("Orange", 0xFF8800), ("Red", 0xFF4040), ("Purple", 0xC080FF), ("Gray", 0xC0C0C0),
    ];

    static readonly string[] Aligns = ["Left Top", "HCenter Top", "Right Top"];

    readonly string[] _styles = new string[Aligns.Length];

    public string Id => "dxseba.levelup.toast";

    public void Register(UiRegistration registration)
    {
        for (int g = 0; g < Aligns.Length; g++)
            _styles[g] = registration.RegisterStyle("align" + g, new UiStyle { TextAlign = Aligns[g] });
        registration.Contribute(UiSlots.Overlay, Build, order: 41);
    }

    UiNode Build(UiFrameContext frame)
    {
        var toasts = Toasts.Current();
        var root = new UiNode("levelup-toasts") { Placement = UiPlacement.Fill, Pickable = false, ShowWithHudHidden = !settings.HideWithGameUi };
        if (toasts.Count == 0) return root;

        int anchor = Math.Max(0, Array.IndexOf(LevelUpSettings.Anchors, settings.Anchor));
        float scale = frame.Viewport.Height / ReferenceHeight * settings.Scale;
        float font = Math.Max(1, (int)(settings.FontSize * scale));
        var box = new Vector2(Math.Max(1, (int)(Width * scale)), Math.Max((int)(font * FontBoxes), Math.Max(1, (int)(Height * scale))));
        float gap = (int)(Spacing * scale);
        var origin = Origin(frame.Viewport.Width, frame.Viewport.Height, anchor, box, gap, toasts.Count, scale);

        var colour = Colour();
        float opacity = Math.Clamp(settings.Opacity / 100f, 0, 1) * colour.W;
        float textScale = Math.Clamp(font / FontPixels, 0.01f, UiHost.MaxTextScale);
        for (int i = 0; i < toasts.Count; i++)
        {
            var (fade, text) = toasts[i];
            var placement = new UiPlacement(box.X, box.Y, Left: origin.X, Top: origin.Y + i * (box.Y + gap));
            root.Children.Add(new("levelup-toast-" + i, UiNodeKind.Label)
            {
                Placement = placement, Text = text, Style = _styles[anchor % 3], Pickable = false, Animated = true,
                TextColour = colour with { W = 1 }, TextScale = textScale, Opacity = Math.Clamp(fade * opacity, 0, 1),
            });
        }

        return root;
    }

    /// <summary>The first box's top left: at the offset from the anchored edge, centred on a centred axis.</summary>
    Vector2 Origin(float width, float height, int anchor, Vector2 box, float gap, int count, float scale)
    {
        float x = (anchor % 3) switch
        {
            0 => (int)(settings.OffsetX * scale),
            1 => (int)((width - box.X) / 2),
            _ => width - (int)(settings.OffsetX * scale) - box.X,
        };
        float block = count * box.Y + (count - 1) * gap;
        float y = (anchor / 3) switch
        {
            0 => (int)(settings.OffsetY * scale),
            1 => (int)((height - block) / 2),
            _ => height - block - (int)(settings.OffsetY * scale),
        };
        return new(x, y);
    }

    /// <summary>The text colour (RGBA): the preset, or the custom #AARRGGBB (also #RRGGBB, opaque); yellow for one that does not read.</summary>
    Vector4 Colour()
    {
        if (settings.CustomColour && Argb(settings.TextColor) is { } custom) return custom;

        int preset = Math.Max(0, Array.FindIndex(Colours, c => c.Name == settings.Colour));
        return Unpack(0xFF000000 | Colours[preset].Rgb);
    }

    static Vector4? Argb(string text)
    {
        var hex = text.Trim().TrimStart('#');
        if (hex.Length is not (6 or 8) || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value)) return null;

        return Unpack(hex.Length == 6 ? 0xFF000000 | value : value);
    }

    static Vector4 Unpack(uint argb) =>
        new(((argb >> 16) & 0xff) / 255f, ((argb >> 8) & 0xff) / 255f, (argb & 0xff) / 255f, ((argb >> 24) & 0xff) / 255f);
}
