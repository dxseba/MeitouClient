using System.Numerics;
using Meitou.Game.Host.Input;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.DirectorPanel;

/// <summary>
/// The panel window (the original's ImGui window, 650 by 1051 pixels by default, shrunk to the screen): a header to drag it by with the RU/EN
/// switch and a close button, the character it works with, the tab row and the open tab's controls, and the last command's status line. Each
/// frame its nodes are built again from the published view; a button's press runs the handler it was built with. Interface thread only.
/// </summary>
internal sealed partial class DirectorWindow(ModContext context, Catalog catalog, Texts texts, Points points, ModSettingsGroup group) : IUiExtension
{
    const float Width = 650, MaxHeight = 1051, Line = 22, Gap = 3;

    /// <summary>The original's tabs, in its order.</summary>
    static readonly string[] Tabs =
        ["Items", "Stats", "NPC Spawn", "Relations", "Research", "Behaviour", "Barbershop", "Body", "Effects", "Teleport", "World"];

    readonly Dictionary<string, Action> _handlers = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> _fields = new(StringComparer.Ordinal);

    volatile bool _open;
    int _tab;
    Vector2 _place = new(60, 40);
    string _do = "", _text = "", _drag = "";
    Styles _styles;

    /// <summary>The panel's character (the first selected, else the HUD's), the selection and the pointer's Shift, as of the last frame.</summary>
    CharacterId _target = CharacterId.None;
    IReadOnlyList<CharacterId> _selected = [];
    Vector2 _screen = new(1920, 1080);
    (CharacterId Target, int A, int B) _watched = (CharacterId.None, -1, -1);

    internal ModKeyAction? Key { get; set; }

    public string Id => "dxseba.directorpanel.window";

    public void Register(UiRegistration registration)
    {
        _styles = RegisterStyles(registration);
        _do = registration.RegisterAction("do", a => Run(a.Argument));
        _text = registration.RegisterAction("text", a => _fields[a.Argument] = a.Text);
        _drag = registration.RegisterAction("drag", a => _place += a.Delta);
        registration.Contribute(UiSlots.Overlay, _ => Build(), order: 57);
        registration.AddKeyInput(input => _open && input.Cancel && Close());
        registration.AddMapInput(MapClick);
    }

    internal void Toggle() => _open = !_open;

    bool Close()
    {
        _open = false;
        return true;
    }

    /// <summary>Once a frame: the character the panel works with, and the watch command when it or the Relations pair changed.</summary>
    internal void Frame(ModFrame frame)
    {
        _selected = [.. frame.Selected];
        _target = _selected.Count > 0 ? _selected[0] : frame.Shown ?? CharacterId.None;
        _shown = frame.Shown ?? CharacterId.None;
        _screen = frame.ScreenSize;
        KeepLooped();
        texts.Russian = DirectorPanelMod.Settings.Language == PanelSettings.Russian;
        var watch = (_target, PairA, PairB);
        if (!_open || watch == _watched) return;

        _watched = watch;
        context.Enqueue(new Watch(_target, PairA, PairB) { Tick = context.Tick });
    }

    void Run(string key)
    {
        if (_handlers.TryGetValue(key, out var handler)) handler();
    }

    /// <summary>A line of the window's own (a refusal it makes itself), shown until the next command.</summary>
    string _note = "";

    void Send(DirectorCommand command)
    {
        _note = "";
        context.Enqueue(command);
    }

    DirectorView? View => DirectorPanelMod.Running?.View;

    TargetView? Target => View?.Target is { } t && t.Id == _target ? t : null;

    UiNode Build()
    {
        var root = new UiNode("dp-root") { Placement = UiPlacement.Fill, Pickable = false };
        if (!_open) return root;

        _handlers.Clear();
        float height = Math.Min(MaxHeight, _screen.Y - 2 * _place.Y);
        var body = new UiNode("dp-window")
        {
            Placement = new(Width, Math.Max(300, height), Left: _place.X, Top: _place.Y),
            Style = _styles.Panel,
            Flow = UiFlow.Column,
            Padding = 6,
            Gap = Gap,
        };

        body.Children.Add(Header());
        body.Children.Add(Label("dp-who", WhoText()));
        body.Children.Add(TabRow(0, 6));
        body.Children.Add(TabRow(6, Tabs.Length));
        body.Children.Add(TabBody(Math.Max(300, height) - 12 - 5 * (Line + 4 + Gap) - Line));
        body.Children.Add(Label("dp-status", StatusText(), _styles.Dim));
        root.Children.Add(body);
        return root;
    }

    UiNode Header()
    {
        var line = Row("dp-header");
        line.Children.Add(new UiNode("dp-title", UiNodeKind.Button)
        {
            Placement = new(Width - 12 - 2 * 70 - 8, Line + 4), Text = "Director Panel", Style = _styles.Title, Action = _drag,
            Pointer = UiPointerEvents.Drag,
        });
        line.Children.Add(Button("dp-lang", texts.Russian ? "EN" : "RU", SwitchLanguage, 70));
        line.Children.Add(Button("dp-close", "X", () => Close(), 70));
        return line;
    }

    void SwitchLanguage() =>
        group.Settings.First(s => s.Key == nameof(PanelSettings.Language))
            .Set(texts.Russian ? PanelSettings.English : PanelSettings.Russian);

    string WhoText()
    {
        if (_target.IsNone) return texts["Nobody selected."];

        return texts["Working with character:"] + " " + (Target?.Name ?? "...");
    }

    string StatusText() => _note.Length > 0 ? _note : DirectorPanelMod.Running?.Status is { } line ? texts.Format(line) : "";

    UiNode TabRow(int from, int to)
    {
        var line = Row("dp-tabs" + from);
        float width = (Width - 12 - (to - from - 1) * 4) / (to - from);
        for (int i = from; i < to; i++)
        {
            int tab = i;
            string caption = (tab == _tab ? "> " : "") + texts[Tabs[tab]];
            line.Children.Add(Button("dp-tab" + tab, caption, () => _tab = tab, width));
        }

        return line;
    }

    UiNode TabBody(float height)
    {
        var scroll = new UiNode("dp-body" + _tab, UiNodeKind.ScrollView)
        {
            Placement = new(UiLength.Fill, height), Style = _styles.List, Flow = UiFlow.Column, Padding = 4, Gap = Gap,
        };

        foreach (var node in TabNodes()) scroll.Children.Add(node);
        return scroll;
    }

    IEnumerable<UiNode> TabNodes() => _tab switch
    {
        0 => ItemsTab(),
        1 => StatsTab(),
        2 => SpawnTab(),
        3 => RelationsTab(),
        4 => ResearchTab(),
        5 => BehaviourTab(),
        6 => BarbershopTab(),
        7 => BodyTab(),
        8 => EffectsTab(),
        9 => TeleportTab(),
        _ => WorldTab(),
    };
}
