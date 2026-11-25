using System.Globalization;
using System.Numerics;
using Meitou.Game.Ui;

namespace Meitou.Transmog;

/// <summary>
/// The chosen appearance's editor: a weapon's rendering and finish, the dye (hex codes, the game's presets and a colour picker: a grid of
/// swatches of 18 hues at five shades and nine greys, setting the channel chosen with Colour 1 / Colour 2) and the model offset.
/// </summary>
sealed partial class TransmogWindow
{
    const int Hues = 18, Shades = 5, Greys = 9;
    const float Cell = 13;

    readonly List<(int Colour, string Style)> _palette = [];

    void RegisterEditorActions(UiRegistration r)
    {
        _hexTyped1 = r.RegisterAction("hex1", c => TypedHex(1, c.Text));
        _hexTyped2 = r.RegisterAction("hex2", c => TypedHex(2, c.Text));
        _presetStep = r.RegisterAction("preset", c => StepPreset(c.Argument == "+" ? 1 : -1));
        _resetColour = r.RegisterAction("reset-colour", _ => ResetColour());
        _channelAction = r.RegisterAction("channel", c => _channel = c.Argument == "2" ? 2 : 1);
        _pick = r.RegisterAction("pick", c => Pick(Index(c.Argument)));
        _nudge = r.RegisterAction("nudge", c => Nudge(c.Argument));
        _resetOffset = r.RegisterAction("reset-offset", _ => ResetOffset());
        _copy = r.RegisterAction("copy", _ => Copy());
        _paste = r.RegisterAction("paste", _ => Paste());
        _rendered = r.RegisterAction("rendered", _ => ToggleRendered());
        _originalFinish = r.RegisterAction("original-finish", _ => ToggleOriginalFinish());
        _finishStep = r.RegisterAction("finish", c => StepFinish(c.Argument == "+" ? 1 : -1));
    }

    /// <summary>One style per swatch (a style holds a node's fill colour).</summary>
    void RegisterPalette(UiRegistration registration)
    {
        _palette.Clear();
        for (int s = 0; s < Shades; s++)
        {
            for (int h = 0; h < Hues; h++) AddSwatch(registration, Hsv(h / (float)Hues, s < 2 ? 0.45f + 0.275f * s : 1, s < 2 ? 1 : 1 - 0.22f * (s - 1)));
        }

        for (int g = 0; g < Greys; g++)
        {
            int v = (int)MathF.Round(255 * g / (float)(Greys - 1));
            AddSwatch(registration, v << 16 | v << 8 | v);
        }
    }

    void AddSwatch(UiRegistration registration, int colour)
    {
        var fill = new Vector4((colour >> 16 & 255) / 255f, (colour >> 8 & 255) / 255f, (colour & 255) / 255f, 1);
        string style = registration.RegisterStyle("swatch" + _palette.Count.ToString(CultureInfo.InvariantCulture),
            new() { Background = fill, Hover = Vector4.Lerp(fill, Vector4.One, 0.4f) });
        _palette.Add((colour, style));
    }

    static int Hsv(float h, float s, float v)
    {
        float r = Channel(5, h, s, v), g = Channel(3, h, s, v), b = Channel(1, h, s, v);
        return (int)(r * 255) << 16 | (int)(g * 255) << 8 | (int)(b * 255);
    }

    static float Channel(float n, float h, float s, float v)
    {
        float k = (n + h * 6) % 6;
        return v - v * s * Math.Clamp(Math.Min(k, 4 - k), 0, 1);
    }

    /// <summary>The chosen appearance's weapon options (a weapon slot), dye (when it takes one) and model offset.</summary>
    void Editor(UiNode window, float x, float y)
    {
        if (Chosen is not { } layer)
        {
            window.Children.Add(Text("tm-edit-none", "Edit", x, y, 200, _dim));
            return;
        }

        window.Children.Add(Text("tm-edit", context.Tr("Edit") + ": " + NameOf(layer), x, y, 420, _title));
        window.Children.Add(Button("tm-copy", "Copy", Width - 200, y, 90, Row, _copy));
        window.Children.Add(Button("tm-paste", "Paste", Width - 104, y, 90, Row, _paste));
        y += Row + Gap;
        if (Slots.WeaponOf(_slot) >= 0) y = WeaponRows(window, x, y, layer);
        if (Dyeable(layer)) DyeRows(window, x, y);
        OffsetRows(window, x + 400, y, layer);
    }

    float WeaponRows(UiNode window, float x, float y, Layer layer)
    {
        window.Children.Add(Button("tm-rendered", (layer.Rendered ? "[x] " : "[ ] ") + context.Tr("Enable rendering"), x, y, 190, Row, _rendered));
        window.Children.Add(Button("tm-finish-own", (layer.OriginalFinish ? "[x] " : "[ ] ") + context.Tr("Use original weapon material"),
            x + 196, y, 200, Row, _originalFinish));
        y += Row + 2;
        string finish = !layer.OriginalFinish && context.Data.Find(layer.Finish) is { } maker ? maker.ShownName() : context.Tr("Search manufacturer or grade");
        window.Children.Add(Button("tm-finish-prev", "<", x, y, 28, Row, _finishStep, "-"));
        window.Children.Add(Text("tm-finish", finish, x + 32, y, 330, _text));
        window.Children.Add(Button("tm-finish-next", ">", x + 366, y, 28, Row, _finishStep, "+"));
        return y + Row + Gap;
    }

    void DyeRows(UiNode window, float x, float y)
    {
        HexRow(window, 1, _hex1, _hexTyped1, x, y);
        HexRow(window, 2, _hex2, _hexTyped2, x, y + Row + 2);
        y += 2 * (Row + 2);
        string preset = _preset >= 0 && _presets is { } p && _preset < p.Count ? p[_preset].ShownName() : context.Tr("Search colour presets");
        window.Children.Add(Button("tm-preset-prev", "<", x, y, 28, Row, _presetStep, "-"));
        window.Children.Add(Text("tm-preset", preset, x + 32, y, 186, _text));
        window.Children.Add(Button("tm-preset-next", ">", x + 222, y, 28, Row, _presetStep, "+"));
        window.Children.Add(Button("tm-reset-colour", "Reset colour", x + 256, y, 130, Row, _resetColour));
        Picker(window, x, y + Row + 4);
    }

    /// <summary>A channel's row: its label (a click makes the picker set it), its #RRGGBB box and the colour it holds.</summary>
    void HexRow(UiNode window, int channel, string hex, string action, float x, float y)
    {
        string key = channel.ToString(CultureInfo.InvariantCulture);
        var label = Button($"tm-c{key}", channel == 1 ? "Colour 1" : "Colour 2", x, y, 100, Row, _channelAction, key);
        window.Children.Add(_channel == channel ? Restyle(label, _chosen) : label);
        window.Children.Add(new UiNode($"tm-hex{key}", UiNodeKind.TextBox)
        {
            Placement = new(110, Row, Left: x + 104, Top: y), Text = hex, Action = action, MaxLength = 7,
        });
        var colour = ParseHex(hex) is { } c ? new Vector4((c >> 16 & 255) / 255f, (c >> 8 & 255) / 255f, (c & 255) / 255f, 1) : (Vector4?)null;
        string hint = colour is null ? context.Tr("Use #RRGGBB for colours") : "||||||||";
        window.Children.Add(new UiNode($"tm-hex{key}-swatch", UiNodeKind.Label)
        {
            Placement = new(170, Row, Left: x + 218, Top: y), Text = hint, Style = _dim, TextColour = colour,
        });
    }

    void Picker(UiNode window, float x, float y)
    {
        for (int i = 0; i < _palette.Count; i++)
        {
            bool grey = i >= Hues * Shades;
            int column = grey ? i - Hues * Shades : i % Hues, row = grey ? Shades : i / Hues;
            window.Children.Add(new UiNode($"tm-sw{i}", UiNodeKind.Button)
            {
                Placement = new(Cell - 1, Cell - 1, Left: x + column * Cell, Top: y + row * Cell), Style = _palette[i].Style, Action = _pick,
                Argument = _palette[i].Colour.ToString(CultureInfo.InvariantCulture),
            });
        }
    }

    void OffsetRows(UiNode window, float x, float y, Layer layer)
    {
        window.Children.Add(Text("tm-offset", "Model offset", x, y, 200, _dim));
        string[] axes = ["X", "Y", "Z"];
        for (int a = 0; a < 3; a++)
        {
            Nudger(window, $"p{a}", context.Tr("Position") + " " + axes[a], Value(layer.Position, a, "0.00"), x, y + Row * (a + 1));
            Nudger(window, $"r{a}", context.Tr("Rotation") + " " + axes[a], Value(layer.Rotation, a, "0"), x, y + Row * (a + 4));
        }

        Nudger(window, "s0", context.Tr("Scale"), layer.Scale.ToString("0.00", CultureInfo.InvariantCulture), x, y + Row * 7);
        window.Children.Add(Button("tm-reset-offset", "Reset Offset", x, y + Row * 8 + 4, 160, Row, _resetOffset));
    }

    static string Value(Vector3 v, int axis, string format) =>
        (axis == 0 ? v.X : axis == 1 ? v.Y : v.Z).ToString(format, CultureInfo.InvariantCulture);

    void Nudger(UiNode window, string key, string label, string value, float x, float y)
    {
        window.Children.Add(Text($"tm-{key}-label", label, x, y, 130, _text));
        window.Children.Add(Button($"tm-{key}-minus", "-", x + 132, y, 28, Row - 2, _nudge, key + "-"));
        window.Children.Add(Text($"tm-{key}-value", value, x + 162, y, 60, _text));
        window.Children.Add(Button($"tm-{key}-plus", "+", x + 224, y, 28, Row - 2, _nudge, key + "+"));
    }
}
