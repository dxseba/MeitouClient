using System.Globalization;
using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.ElementalEnchantment;

/// <summary>
/// The ENCHANTMENT window of one of your characters, with the original's texts: its level and the way to the next, the main element (None or
/// one of five) and the secondary (Locked below level 50), its aptitudes, the active effect's figures and the resonance. A choice is a command
/// the world applies in its slow step. Sized by the panel scale setting; dragging its title moves it. Opened from the ENCHANTMENT button under the
/// stats window (the original's button in that window), the character menu row or the HUD panel line.
/// </summary>
sealed class EnchantWindow(ModContext context, EnchantSettings settings) : IUiExtension
{
    /// <summary>
    /// The window's size at scale 1, its row height and the gap between parts.
    /// </summary>
    const float Width = 470, Height = 420, Row = 26, Gap = 6;

    /// <summary>
    /// The character whose window is open; none while closed.
    /// </summary>
    CharacterId _open = CharacterId.None;

    /// <summary>
    /// The character the stats window's button was last shown for (host thread).
    /// </summary>
    CharacterId _statsCharacter = CharacterId.None;

    /// <summary>
    /// How far the player dragged the window from the centre, pixels (kept for the session).
    /// </summary>
    Vector2 _moved;

    /// <summary>
    /// Registered style and action keys.
    /// </summary>
    string _panel = "", _title = "", _text = "", _dim = "", _chosen = "", _bar = "", _barFill = "", _close = "", _main = "", _second = "", _drag = "",
        _statsButton = "";

    public string Id => "dxseba.elementalenchantment.window";

    /// <summary>
    /// Opens the window on <paramref name="character"/> (interface thread).
    /// </summary>
    public void Open(CharacterId character) => _open = character;

    public void Register(UiRegistration registration)
    {
        _panel = registration.RegisterStyle("panel", new() { Skin = "Kenshi_GenericTextBoxFlatSkin" });
        _title = registration.RegisterStyle("title", new() { Text = new(.92f, .86f, .74f, 1), FontName = "Kenshi_PaintedTextFont_Medium" });
        _text = registration.RegisterStyle("text", new() { Text = new(.90f, .86f, .76f, 1), TextPadding = new UiInsets(4, 0, 4, 0) });
        _dim = registration.RegisterStyle("dim", new() { Text = new(.78f, .72f, .62f, 1), TextPadding = new UiInsets(4, 0, 4, 0) });
        _chosen = registration.RegisterStyle("chosen", new() { Text = new(1, .95f, .8f, 1), Background = new(1, 1, 1, .18f), TextAlign = "Center" });
        _bar = registration.RegisterStyle("bar", new() { Background = new(0, 0, 0, .45f) });
        _barFill = registration.RegisterStyle("fill", new() { Background = new(.85f, .62f, .25f, .85f) });
        _close = registration.RegisterAction("close", _ => _open = CharacterId.None);
        _main = registration.RegisterAction("main", c => Choose(c.Argument, secondary: false));
        _second = registration.RegisterAction("second", c => Choose(c.Argument, secondary: true));
        _drag = registration.RegisterAction("drag", c => _moved += c.Kind == UiEventKind.Drag ? c.Delta : Vector2.Zero);
        _statsButton = registration.RegisterAction("stats", _ => _open = _statsCharacter);
        registration.Contribute(UiSlots.Overlay, Build, order: 62);
        registration.AddScreenControls("stats", "Root", UiAnchorPlacement.Below, StatsButton);
    }

    /// <summary>
    /// The ENCHANTMENT button just under the stats window, for its selected character when that is one of yours with elemental ability.
    /// </summary>
    UiNode? StatsButton(UiScreenContext screen)
    {
        if (!settings.Enabled || screen.Character is not { IsPlayer: true } character) return null;
        if (ElementalEnchantmentMod.Running?.View.TryGetValue(character.Id, out var view) != true || !view!.Able) return null;

        _statsCharacter = character.Id;
        return new UiNode("ee-stats", UiNodeKind.Button) { Placement = new(150, 26, Left: 0, Top: 2), Text = "ENCHANTMENT", Action = _statsButton };
    }

    /// <summary>
    /// A button press: the element (its number) for the main or secondary slot.
    /// </summary>
    void Choose(string argument, bool secondary)
    {
        if (_open.IsNone || !byte.TryParse(argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte element)) return;

        context.Enqueue(new ChooseElementOrder(_open, secondary, element) { Tick = context.Tick });
    }

    #region Layout

    /// <summary>
    /// The window while open on a character the world still shows; closed otherwise.
    /// </summary>
    UiNode Build(UiFrameContext frame)
    {
        if (_open.IsNone || !settings.Enabled || ElementalEnchantmentMod.Running?.View.TryGetValue(_open, out var view) != true)
            return new UiNode("ee-hidden") { Visible = false };

        float s = Math.Clamp(settings.UiScale, 0.8f, 2);
        var root = new UiNode("ee") { Placement = UiPlacement.Fill, Pickable = false };
        var window = new UiNode("ee-window")
        {
            Placement = new(Width * s, Height * s, Left: (frame.Viewport.Width - Width * s) / 2 + _moved.X,
                Top: (frame.Viewport.Height - Height * s) / 2 + _moved.Y),
            Style = _panel,
        };
        Fill(window, view!, s);
        root.Children.Add(window);
        return root;
    }

    /// <summary>
    /// The window's parts, top to bottom.
    /// </summary>
    void Fill(UiNode window, EnchantView view, float s)
    {
        float y = 8;
        window.Children.Add(new UiNode("ee-title", UiNodeKind.Label)
        {
            Placement = new((Width - 60) * s, Row * s, Left: 10 * s, Top: y * s), Text = "ELEMENTAL ENCHANTMENT", Style = _title, Pickable = true,
            Pointer = UiPointerEvents.Drag, Action = _drag,
        });
        window.Children.Add(Button("ee-close", "X", Width - 38, y, 28, Row - 2, s, _close, ""));
        y += Row + Gap;

        if (!view.Able)
        {
            window.Children.Add(Text("ee-none", "No elemental ability", 10, y, Width - 20, Row, s, _text));
            return;
        }

        y = Progress(window, view, y, s);
        y = Elementals(window, "Main", view.Main, view.Level >= 0, _main, y, s, "main");
        bool open = view.Level >= Elements.SecondaryLevel;
        y = Elementals(window, "Secondary", view.Second, open, _second, y, s, "second");
        y = Aptitudes(window, view, y, s);
        Effects(window, view, y, s);
    }

    /// <summary>
    /// "Level N" (or MAX LEVEL) and the bar to the next level.
    /// </summary>
    float Progress(UiNode window, EnchantView view, float y, float s)
    {
        bool max = view.Level >= Elements.MaxLevel;
        string title = max ? "ELEMENTAL MASTER" : "ELEMENTAL";
        window.Children.Add(Text("ee-level", $"{title}  {view.Level}", 10, y, 200, Row, s, _text));

        int from = Elements.XpFor(view.Level), to = Elements.XpFor(view.Level + 1);
        float part = max ? 1 : Math.Clamp((view.Xp - from) / (float)Math.Max(1, to - from), 0, 1);
        string label = max ? "MAX LEVEL" : $"Next Level  {view.Xp - from} / {to - from}";
        window.Children.Add(Panel("ee-bar", 210, y + 4, 250, Row - 8, s, _bar));
        window.Children.Add(Panel("ee-fill", 210, y + 4, 250 * part, Row - 8, s, _barFill));
        window.Children.Add(Text("ee-next", label, 210, y, 250, Row, s, _dim));
        return y + Row + Gap;
    }

    /// <summary>
    /// A slot's row: None and the five elements, the chosen one marked; "Locked" while the slot is not open.
    /// </summary>
    float Elementals(UiNode window, string label, Element chosen, bool open, string action, float y, float s, string key)
    {
        window.Children.Add(Text($"ee-{key}", label, 10, y, 90, Row, s, _text));
        if (!open)
        {
            window.Children.Add(Text($"ee-{key}-locked", "Locked", 100, y, 200, Row, s, _dim));
            return y + Row + Gap;
        }

        float x = 100, w = 58;
        foreach (var element in (Element[])[Element.None, .. Elements.All])
        {
            string argument = ((byte)element).ToString(CultureInfo.InvariantCulture);
            var node = Button($"ee-{key}-{element}", element.ToString(), x, y, w - 2, Row, s, action, argument);
            window.Children.Add(element == chosen ? Restyle(node, _chosen) : node);
            x += w;
        }

        return y + Row + Gap;
    }

    /// <summary>
    /// The aptitude line: each element's percentage (all 100% with the setting).
    /// </summary>
    float Aptitudes(UiNode window, EnchantView view, float y, float s)
    {
        var profile = new Profile { Favoured = view.Favoured, Rare = view.Rare };
        var parts = Elements.All.Select(e => $"{e} {(settings.FlatAptitude ? 100 : profile.Aptitude(e))}%");
        window.Children.Add(Text("ee-aptitude", "Aptitude: " + string.Join("  ", parts), 10, y, Width - 20, Row, s, _dim));
        return y + Row + Gap;
    }

    /// <summary>
    /// The ACTIVE EFFECT figures of the main and the secondary element, and the resonance line.
    /// </summary>
    void Effects(UiNode window, EnchantView view, float y, float s)
    {
        window.Children.Add(Text("ee-active", "ACTIVE EFFECT", 10, y, Width - 20, Row, s, _title));
        y += Row;

        int i = 0;
        foreach (var line in EffectLines(view.Enchant))
        {
            window.Children.Add(Text($"ee-effect{i++}", line, 16, y, Width - 30, Row - 4, s, _text));
            y += Row - 4;
        }

        window.Children.Add(Text("ee-resonance", Resonance(view), 16, y + Gap, Width - 30, Row - 4, s, _dim));
    }

    /// <summary>
    /// The figures each element adds now, at the effect strength.
    /// </summary>
    IEnumerable<string> EffectLines(Enchant e)
    {
        if (e.Main == Element.None)
        {
            yield return "No active element";
            yield return "Select an element above.";
            yield break;
        }

        if (settings.Strength <= 0)
        {
            yield return "Effect is disabled.";
            yield break;
        }

        float m = Math.Min(settings.Strength, Elements.MaxStrength);
        foreach (var element in Elements.All)
        {
            float w = e.Weight(element);
            if (w <= 0) continue;

            foreach (var line in Lines(element, e.Level, m * w)) yield return $"{element}: {line}";
        }
    }

    /// <summary>
    /// One element's figures at a weight.
    /// </summary>
    static IEnumerable<string> Lines(Element element, int level, float w)
    {
        switch (element)
        {
            case Element.Fire:
                yield return $"Blunt damage +{Pct(Elements.Percent(level) * w)} (percent of base damage)";
                break;
            case Element.Lightning:
                yield return $"Armour penetration +{Pct(Elements.Percent(level) * w)}";
                yield return $"Attack / bolt speed +{Pct(Elements.LightningSpeed(level) * w)}";
                break;
            case Element.Wind:
                yield return $"Attack / block speed +{Pct(Elements.Minor(level) * w)}";
                break;
            case Element.Water:
                yield return $"Cutting damage +{Pct(Elements.Percent(level) * w)} (percent of base damage)";
                break;
            case Element.Ice:
                yield return $"Cutting damage +{Pct(Elements.Minor(level) * w)} (percent of base damage)";
                yield return $"Bleed multiplier +{(Elements.IceBleed(level) * w).ToString("0.00", CultureInfo.InvariantCulture)}";
                break;
        }
    }

    /// <summary>
    /// The resonance line: active with three different elements, the hint with a ward, off with the setting at 0.
    /// </summary>
    string Resonance(EnchantView view)
    {
        if (settings.Resonance <= 0) return "Resonance: off (MCM setting)";

        return view.Enchant.Resonant ? "3-Element Resonance" : "Resonance: use different elements in all three slots";
    }

    static string Pct(float value) => (value * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    #endregion

    #region Nodes

    UiNode Text(string id, string text, float x, float y, float w, float h, float s, string style) =>
        new(id, UiNodeKind.Label) { Placement = new(w * s, h * s, Left: x * s, Top: y * s), Text = text, Style = style };

    static UiNode Panel(string id, float x, float y, float w, float h, float s, string style) =>
        new(id) { Placement = new(Math.Max(0, w) * s, h * s, Left: x * s, Top: y * s), Style = style, Pickable = false };

    static UiNode Button(string id, string text, float x, float y, float w, float h, float s, string action, string argument) =>
        new(id, UiNodeKind.Button) { Placement = new(w * s, h * s, Left: x * s, Top: y * s), Text = text, Action = action, Argument = argument };

    /// <summary>
    /// The same button in another style (the chosen element).
    /// </summary>
    static UiNode Restyle(UiNode node, string style) =>
        new(node.Id, UiNodeKind.Button) { Placement = node.Placement, Text = node.Text, Action = node.Action, Argument = node.Argument, Style = style };

    #endregion
}
