using System.Globalization;
using System.Numerics;
using Meitou.Game.Ui;

namespace Meitou.LevelUpNotify;

/// <summary>
/// Draws the bound toasts as the original lays them out: boxes of 600 x 52 (at least 1.8 font heights) with 6 between, stacked oldest first from
/// the chosen corner, edge or centre at the offset, every size scaled by screen height / 2160 and the UI scale; text in the box's column alignment.
/// </summary>
public sealed class ToastUi(LevelUpSettings settings) : IUiExtension
{
    const float ReferenceHeight = 2160, Width = 600, Height = 52, Spacing = 6, FontSize = 36, FontBoxes = 1.8f;
    const int AlphaSteps = 10;

    /// <summary>The colour presets of the original's panel (ARGB without the alpha).</summary>
    static readonly (string Name, uint Rgb)[] Colours =
    [
        ("Yellow", 0xFFFF00), ("White", 0xFFFFFF), ("Green", 0x00FF88), ("Cyan", 0x00FFFF),
        ("Orange", 0xFF8800), ("Red", 0xFF4040), ("Purple", 0xC080FF), ("Gray", 0xC0C0C0),
    ];

    static readonly string[] Aligns = ["Left Top", "HCenter Top", "Right Top"];

    readonly Dictionary<(int Colour, int Alpha, int Align), string> _styles = [];

    public string Id => "dxseba.levelup.toast";

    public void Register(UiRegistration registration)
    {
        RegisterStyles(registration);
        registration.Contribute(UiSlots.Overlay, Build, order: 41);
    }

    void RegisterStyles(UiRegistration registration)
    {
        for (int c = 0; c < Colours.Length; c++)
        {
            for (int a = 0; a <= AlphaSteps; a++)
            {
                for (int g = 0; g < Aligns.Length; g++)
                {
                    var text = Colour(Colours[c].Rgb, a / (float)AlphaSteps);
                    var style = new UiStyle { Text = text, TextAlign = Aligns[g] };
                    _styles[(c, a, g)] = registration.RegisterStyle(string.Create(CultureInfo.InvariantCulture, $"t{c}-{a}-{g}"), style);
                }
            }
        }
    }

    static Vector4 Colour(uint rgb, float alpha) => new(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, alpha);

    UiNode Build(UiFrameContext frame)
    {
        var toasts = Bound(frame);
        var root = new UiNode("levelup-toasts") { Placement = UiPlacement.Fill, Pickable = false };
        if (toasts.Count == 0) return root;

        int anchor = Math.Max(0, Array.IndexOf(LevelUpSettings.Anchors, settings.Anchor));
        int column = anchor % 3, row = anchor / 3;
        float scale = frame.Viewport.Height / ReferenceHeight * settings.Scale;
        float w = Math.Max(1, (int)(Width * scale));
        float h = Math.Max((int)(Math.Max(1, (int)(FontSize * scale)) * FontBoxes), Math.Max(1, (int)(Height * scale)));
        float gap = (int)(Spacing * scale);

        float x = column switch
        {
            0 => (int)(settings.OffsetX * scale),
            1 => (int)((frame.Viewport.Width - w) / 2),
            _ => frame.Viewport.Width - (int)(settings.OffsetX * scale) - w,
        };
        float block = toasts.Count * h + (toasts.Count - 1) * gap;
        float y = row switch
        {
            0 => (int)(settings.OffsetY * scale),
            1 => (int)((frame.Viewport.Height - block) / 2),
            _ => frame.Viewport.Height - block - (int)(settings.OffsetY * scale),
        };

        int colour = Math.Max(0, Array.FindIndex(Colours, c => c.Name == settings.Colour));
        for (int i = 0; i < toasts.Count; i++)
        {
            var (alpha, text) = toasts[i];
            string style = _styles[(colour, (int)MathF.Round(alpha * AlphaSteps), column)];
            var placement = new UiPlacement(w, h, Left: x, Top: y + i * (h + gap));
            root.Children.Add(new("levelup-toast-" + i, UiNodeKind.Label) { Placement = placement, Text = text, Style = style, Pickable = false });
        }

        return root;
    }

    static List<(float Alpha, string Text)> Bound(UiFrameContext frame)
    {
        var toasts = new List<(float, string)>();
        for (int i = 0; i < Toasts.Slots; i++)
        {
            var parts = frame.Text(Toasts.Key + i).Split('|', 2);
            if (parts.Length != 2 || !float.TryParse(parts[0], CultureInfo.InvariantCulture, out float alpha)) break;

            toasts.Add((Math.Clamp(alpha, 0, 1), parts[1]));
        }

        return toasts;
    }
}
