using System.Globalization;
using System.Numerics;
using Meitou.Game.Host.Input;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.ForceAwakening;

/// <summary>
/// The skill bar (above the HUD, <c>hud_bottom</c> pixels from the screen's foot), skill mode (G: 1-5 use the bar, otherwise they select squads),
/// the click-a-target cast (a target skill waits for the next left click in the world, <see cref="ClickPatch"/>; Esc cancels) and the grimoire
/// (L): the path, its specializations and passives, the five bar slots, every skill of the path with its auto-cast tick, Renounce. Host thread.
/// </summary>
sealed class ForceUi(ForceAwakeningMod mod, ModContext context) : IUiExtension
{
    const float SlotWidth = 118, Line = 22;

    readonly record struct Styles(string Panel, string Text, string Dim, string Button, string Lit);

    Styles _styles;
    string _faction = "", _slot = "", _grimoire = "", _pick = "", _place = "", _auto = "", _tree = "", _renounce = "", _close = "";

    bool _mode, _open;
    int _grimSlot;
    long _renounceAsk;
    string? _notice;

    /// <summary>The HUD's character and the character under the pointer, as of the last frame.</summary>
    CharacterId? _shown, _hovered;

    /// <summary>The armed cast: the caster and its bar slot; slot -1 when none waits for a click.</summary>
    (CharacterId Caster, int Slot) _armed = (CharacterId.None, -1);

    public string Id => "dxseba.forceawakening.ui";

    public bool Armed => _armed.Slot >= 0;

    public CharacterId? Hovered => _hovered;

    public void Register(UiRegistration registration)
    {
        var ink = new Vector4(.91f, .86f, .74f, 1);
        var pad = new Meitou.Data.Ui.UiInsets(6, 1, 6, 1);
        _styles = new(
            registration.RegisterStyle("panel", new() { Background = new(.08f, .07f, .09f, .92f), Text = ink }),
            registration.RegisterStyle("text", new() { Text = ink, TextPadding = pad }),
            registration.RegisterStyle("dim", new() { Text = new(.62f, .60f, .55f, 1), TextPadding = pad }),
            registration.RegisterStyle("button", new()
            {
                Background = new(.18f, .15f, .20f, .95f), Hover = new(.30f, .24f, .34f, 1), Disabled = new(.12f, .11f, .13f, .8f), TextPadding = pad,
                TextAlign = "Center",
            }),
            registration.RegisterStyle("lit", new()
            {
                Background = new(.25f, .35f, .55f, .95f), Hover = new(.33f, .45f, .68f, 1), TextPadding = pad, TextAlign = "Center",
            }));
        _slot = registration.RegisterAction("slot", c => Use(int.Parse(c.Argument, CultureInfo.InvariantCulture)));
        _grimoire = registration.RegisterAction("grimoire", _ => _open = !_open);
        _close = registration.RegisterAction("close", _ => _open = false);
        _pick = registration.RegisterAction("pick", c => _grimSlot = int.Parse(c.Argument, CultureInfo.InvariantCulture));
        _place = registration.RegisterAction("place", c => Send(id => new BarOrder(id, _grimSlot, Number(c.Argument)) { Tick = context.Tick }));
        _auto = registration.RegisterAction("auto", c => Auto(c.Argument));
        _tree = registration.RegisterAction("tree", c => Send(id => new TreeOrder(id, Number(c.Argument)) { Tick = context.Tick }));
        _renounce = registration.RegisterAction("renounce", _ => Renounce());
        _faction = registration.RegisterAction("faction", _ => Send(id => new BrainwashFactionOrder(id) { Tick = context.Tick }));
        registration.AddKeyInput(Key);
        registration.Contribute(UiSlots.Overlay, Build, order: 57);
    }

    static int Number(string text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0;

    #region Input

    public void Frame(ModFrame frame)
    {
        _shown = frame.Shown;
        _hovered = frame.Hovered;
        if (_notice is { } notice) frame.Notify(notice);
        _notice = null;
    }

    public void ToggleMode(ModKeyContext key)
    {
        _mode = !_mode;
        if (!_mode) _armed = (CharacterId.None, -1);
        _notice = ForceTexts.T(_mode ? "ST_MODE_ON" : "ST_MODE_OFF");
    }

    public void ToggleGrimoire(ModKeyContext key) => _open = !_open;

    /// <summary>Esc cancels an armed cast; in skill mode 1-5 use the bar (the keys are taken, so they do not select squads).</summary>
    bool Key(UiKeyInput input)
    {
        if (input.Cancel && Armed)
        {
            _armed = (CharacterId.None, -1);
            _notice = ForceTexts.T("ST_CANCELLED");
            return true;
        }

        if (!_mode || input.Ctrl || input.Alt || input.Key is < Meitou.Game.Host.Input.Key.D1 or > Meitou.Game.Host.Input.Key.D5) return false;

        Use(input.Key - Meitou.Game.Host.Input.Key.D1);
        return true;
    }

    /// <summary>A bar slot used: a skill on the caster (or round it) goes at once; a target or point skill waits for the click.</summary>
    void Use(int slot)
    {
        if (View() is not { } view || slot < 0 || slot >= view.Slots.Count || view.Slots[slot].Spell is not { } spell) return;

        if (ForceSystem.SelfKinds.Contains(spell.Kind))
        {
            Enqueue(new CastOrder(view.Id, slot, CharacterId.None, null) { Tick = context.Tick });
            return;
        }

        _armed = (view.Id, slot);
        _notice = ForceTexts.T("ST_PICK_TARGET", ForceTexts.SpellName(spell));
    }

    /// <summary>The armed cast's click (<see cref="ClickPatch"/>): on the character under the pointer, else the ground point.</summary>
    public void Click(CharacterId? target, Vector3? point)
    {
        var (caster, slot) = _armed;
        _armed = (CharacterId.None, -1);
        if (target is null && point is null)
        {
            _notice = ForceTexts.T("ST_TARGET_GONE");
            return;
        }

        Enqueue(new CastOrder(caster, slot, target ?? CharacterId.None, point) { Tick = context.Tick });
    }

    void Auto(string argument)
    {
        int spell = Number(argument);
        if (View() is { } view) Enqueue(new AutoOrder(view.Id, spell, !view.Auto.Contains(spell)) { Tick = context.Tick });
    }

    void Renounce()
    {
        if (View() is not { } view) return;

        long now = Environment.TickCount64;
        if (now - _renounceAsk > 5000)
        {
            _renounceAsk = now;
            _notice = ForceTexts.T("GRIM_RENOUNCE_ASK", view.Name, ForceTexts.PathName(view.Path));
            return;
        }

        _renounceAsk = 0;
        Enqueue(new RenounceOrder(view.Id) { Tick = context.Tick });
    }

    void Send(Func<CharacterId, SimCommand> order)
    {
        if (View() is { } view) Enqueue(order(view.Id));
    }

    void Enqueue(SimCommand command) => context.Enqueue(command);

    BarView? View() => _shown is { } id && ForceAwakeningMod.Running?.Bars.GetValueOrDefault(id) is { } view ? view : null;

    #endregion

    #region Layout

    UiNode Build(UiFrameContext frame)
    {
        var root = new UiNode("force") { Placement = UiPlacement.Fill, Pickable = false };
        if (View() is not { } view) return root;

        root.Children.Add(Bar(frame, view));
        if (_open) root.Children.Add(Grimoire(frame, view));
        return root;
    }

    string PoolLine(BarView v)
    {
        string pool = $"{v.Pool:0}", max = $"{v.PoolMax:0}", level = $"{v.Level:0}";
        return v.Path switch
        {
            ForcePath.Sith => ForceTexts.T("HUD_SITH", pool, max, level),
            ForcePath.Hunter => ForceTexts.T("HUD_FOCUS", pool, max, level),
            _ => ForceTexts.T("HUD_FORCE", pool, max, level),
        };
    }

    UiNode Bar(UiFrameContext frame, BarView view)
    {
        float width = SlotWidth * 6 + 7 * 4, height = Line + 48 + 12;
        var bar = new UiNode("force-bar")
        {
            Placement = new(width, height, Left: (frame.Viewport.Width - width) / 2, Top: frame.Viewport.Height - mod.Config.HudBottom - height),
            Style = _styles.Panel, Flow = UiFlow.Column, Padding = 4, Gap = 2,
        };
        string head = $"{view.Name}  -  {PoolLine(view)}" + (_mode ? "   " + ForceTexts.T("HUD_MODE", "G") : "");
        bar.Children.Add(Label("force-bar-head", head, UiLength.Fill, _styles.Text));

        var row = Row("force-bar-row", 48);
        for (int i = 0; i < view.Slots.Count; i++) row.Children.Add(SlotButton(view, i));
        row.Children.Add(Button("force-bar-grim", ForceTexts.T("HUD_GRIMOIRE", "L"), SlotWidth, 48, _grimoire, "", true, false));
        bar.Children.Add(row);
        return bar;
    }

    UiNode SlotButton(BarView view, int i)
    {
        var slot = view.Slots[i];
        if (slot.Spell is not { } spell) return Button($"force-slot-{i}", $"{i + 1}\n" + ForceTexts.T("GRIM_EMPTY"), SlotWidth, 48, "", "", false, false);

        string state = slot.Cooldown > 0 ? ForceTexts.T("HUD_COOLDOWN", $"{slot.Cooldown:0}") : spell.Full ? ForceTexts.T("HUD_ALL", "") : $"{spell.Cost:0}";
        bool lit = _armed.Slot == i && _armed.Caster == view.Id;
        return Button($"force-slot-{i}", $"{i + 1}  {ForceTexts.Short(spell)}\n{state}", SlotWidth, 48, _slot, $"{i}", slot.Usable || lit, lit);
    }

    UiNode Grimoire(UiFrameContext frame, BarView view)
    {
        const float width = 640;
        var lines = new List<UiNode>();
        var head = Row("force-grim-head", Line);
        head.Children.Add(Label("force-grim-title", ForceTexts.T("GRIM_TITLE", view.Name, ForceTexts.PathName(view.Path)), width - 90, _styles.Text));
        head.Children.Add(Button("force-grim-close", "X", 70, Line, _close, "", true, false));
        lines.Add(head);
        lines.Add(Label("force-grim-pool", PoolLine(view), UiLength.Fill, _styles.Dim));
        lines.AddRange(Trees(view));
        lines.Add(Label("force-grim-bar", ForceTexts.T("GRIM_BAR") + "  -  " + ForceTexts.T("GRIM_HELP"), UiLength.Fill, _styles.Dim));
        lines.Add(BarSlots(view));
        lines.Add(Label("force-grim-all", ForceTexts.T("GRIM_ALL"), UiLength.Fill, _styles.Dim));
        lines.AddRange(view.Known.Select(k => SpellRow(view, k)));
        if (view.Known.Any(k => k.Spell.Kind == "brainwash"))
            lines.Add(Button("force-grim-faction", ForceTexts.T("GRIM_BRAINWASH", view.BrainwashFaction), 300, Line, _faction, "", true, false));
        lines.Add(Button("force-grim-renounce", ForceTexts.T("GRIM_RENOUNCE"), 200, Line, _renounce, "", true, false));

        float height = 16 + lines.Count * (Line + 2);
        var window = new UiNode("force-grim")
        {
            Placement = new(width, height, Left: (frame.Viewport.Width - width) / 2, Top: 90), Style = _styles.Panel, Flow = UiFlow.Column,
            Padding = 6, Gap = 2,
        };
        window.Children.AddRange(lines);
        return window;
    }

    /// <summary>The two specializations (a button each, the chosen one lit) and the chosen tree's three passives.</summary>
    IEnumerable<UiNode> Trees(BarView view)
    {
        string key = ForceConfig.KeyOf(view.Path);
        var row = Row("force-grim-trees", Line);
        row.Children.Add(Label("force-grim-pick", ForceTexts.T("GRIM_PATH_PICK"), 300, _styles.Dim));
        for (int tree = 1; tree <= 2; tree++)
            row.Children.Add(Button($"force-tree-{tree}", ForceTexts.T($"PATH_{key}_{tree}"), 150, Line, _tree, $"{tree}", true, view.Tree == tree));
        yield return row;
        if (view.Tree is not (1 or 2)) yield break;

        float[] levels = mod.Config.PassiveLevels;
        for (int i = 1; i <= 3; i++)
        {
            float value = mod.Config.Passives.GetValueOrDefault((view.Path, view.Tree, i));
            bool on = view.Level >= levels[i - 1];
            string text = $"{levels[i - 1]:0}: " + ForceTexts.T($"PASSIVE_{key}_{view.Tree}_{i}", $"{value:0}") + (on ? ForceTexts.T("GRIM_PASSIVE_ON") : "");
            yield return Label($"force-passive-{i}", text, UiLength.Fill, on ? _styles.Text : _styles.Dim);
        }
    }

    UiNode BarSlots(BarView view)
    {
        var row = Row("force-grim-slots", Line);
        for (int i = 0; i < view.Slots.Count; i++)
        {
            string name = view.Slots[i].Spell is { } s ? ForceTexts.Short(s) : ForceTexts.T("GRIM_EMPTY");
            row.Children.Add(Button($"force-grim-slot-{i}", $"{i + 1}: {name}", SlotWidth, Line, _pick, $"{i}", true, _grimSlot == i));
        }

        return row;
    }

    UiNode SpellRow(BarView view, SpellView known)
    {
        var s = known.Spell;
        var row = Row($"force-spell-{s.Number}", Line);
        string needs = known.Learned ? "" : "  (" + ForceTexts.T("GRIM_NEEDS", $"{s.Level:0}", view.Path == ForcePath.Hunter ? "Crossbows" : ForceTexts.T("STAT_FORCE")) + ")";
        string times = ForceTexts.T("GRIM_TIMES", $"{s.CastTime:0}", $"{s.Cooldown:0}");
        row.Children.Add(Button($"force-spell-{s.Number}-put", ForceTexts.SpellName(s) + needs, 260, Line, _place, $"{s.Number}", known.Learned, false));
        row.Children.Add(Label($"force-spell-{s.Number}-cost", (s.Full ? ForceTexts.T("HUD_ALL", "") : $"{s.Cost:0}") + times, 250, _styles.Dim));
        bool auto = view.Auto.Contains(s.Number);
        row.Children.Add(Button($"force-spell-{s.Number}-auto", auto ? ForceTexts.T("GRIM_AUTO_ON") : ForceTexts.T("GRIM_AUTO_OFF"), 110, Line, _auto,
            $"{s.Number}", known.Learned, auto));
        return row;
    }

    static UiNode Row(string id, float height) => new(id) { Placement = new(UiLength.Fill, height), Flow = UiFlow.Row, Gap = 4, Pickable = false };

    static UiNode Label(string id, string text, UiLength width, string style) =>
        new(id, UiNodeKind.Label) { Placement = new(width, Line), Text = text, Style = style };

    UiNode Button(string id, string text, float width, float height, string action, string argument, bool enabled, bool lit) =>
        new(id, UiNodeKind.Button)
        {
            Placement = new(width, height), Text = text, Style = lit ? _styles.Lit : _styles.Button, Action = enabled ? action : "", Argument = argument,
            Enabled = enabled,
        };

    #endregion
}
