using System.Numerics;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.CheatMenu;

/// <summary>The window's pages: the main page (Quick cheats and the category buttons) and the original's sub-windows.</summary>
enum Page
{
    Main,
    Squad,
    Selected,
    Base,
    World,
    Items,
    Rules,
    Factions,
    Fun,
    Experimental,
    Skills,
    Towns,
    Squads,
    SkillXp,
}

/// <summary>
/// The original's window (<c>Kenshi_WindowCX</c>, titled "Cheat Menu (F8)"), opened and closed by the menu key: the main page with Quick cheats,
/// the category buttons and the menu key, and one page per sub-window with a Back button. Its buttons queue the cheat system's commands or the
/// game's own debug commands; the switches and tiers it shows are the system's published view. Esc closes it. Host thread only.
/// </summary>
internal sealed partial class CheatWindow(ModContext context) : IUiExtension
{
    /// <summary>The original's "near the camera" radius (<b>Verified</b>: 300 for its character and building sweeps).</summary>
    const float NearCamera = 300;

    volatile bool _open;
    Page _page = Page.Main;
    string _status = "";
    string _cats = "", _hour = "", _name = "", _search = "", _value = "";
    Styles _styles = null!;
    Actions _actions = null!;

    // The last frame's view, copied on the host thread (ModFrame must not be kept).
    WorldSnapshot? _snapshot;
    IReadOnlyList<CharacterId> _selected = [];
    Vector3 _camera;

    /// <summary>The running world's cheat system (the window reads its view); set by the system's factory.</summary>
    internal static volatile CheatSystem? Running;

    /// <summary>The menu key's action, for its key's name and the rebind.</summary>
    internal Game.Host.Input.ModKeyAction? MenuKey { get; set; }

    bool _rebinding;

    public string Id => "dxseba.cheatmenu.window";

    public void Register(UiRegistration registration)
    {
        _styles = RegisterStyles(registration);
        _actions = RegisterActions(registration);
        registration.AddKeyInput(Key);
        registration.Contribute(UiSlots.Overlay, _ => Build(), order: 57);
    }

    internal void Toggle()
    {
        _open = !_open;
        _page = Page.Main;
        _status = "";
    }

    /// <summary>Each frame: what the actions need from the screen (the selection, the camera's point, the snapshot).</summary>
    internal void Frame(ModFrame frame)
    {
        _snapshot = frame.Snapshot;
        _selected = [.. frame.Selected];
        _camera = frame.CameraTarget;
        bool wantsCamera = Running is { } system && (system.On(CheatMenu.Toggle.Raids) || system.On(CheatMenu.Toggle.FreeConstruction));
        if (wantsCamera && frame.Tick % 30 == 0) Send(new CameraAt(_camera) { Tick = T });
    }

    bool Key(UiKeyInput input)
    {
        if (input.Kind != UiKeyInputKind.Down) return false;

        if (_rebinding)
        {
            _rebinding = false;
            if (!input.Cancel) MenuKey?.Rebind(Game.Host.Input.Binding.Of(input.Key).With(input.Modifiers));
            return true;
        }

        if (!_open || !input.Cancel) return false;

        if (_page == Page.Main) _open = false;
        else _page = Back(_page);
        return true;
    }

    static Page Back(Page page) => page switch
    {
        Page.Skills => Page.Selected,
        Page.SkillXp => Page.Squad,
        Page.Towns or Page.Squads => Page.World,
        _ => Page.Main,
    };

    static Styles RegisterStyles(UiRegistration r) => new(
        r.RegisterStyle("window", new UiStyle { Skin = "Kenshi_WindowCX" }),
        r.RegisterStyle("text", new UiStyle { Skin = "Kenshi_TextboxStandardText", TextPadding = new(6, 1, 6, 1) }),
        r.RegisterStyle("head", new UiStyle { Skin = "Kenshi_TextboxStandardText", TextAlign = "Center", TextPadding = new(6, 1, 6, 1) }),
        r.RegisterStyle("button", new UiStyle { Skin = "Kenshi_Button1", TextAlign = "Center" }),
        r.RegisterStyle("list", new UiStyle { Skin = "Kenshi_ScrollView" }));

    sealed record Actions(string Page, string Toggle, string Tier, string Act, string Pick, string Cats, string Hour, string Name, string Search,
        string Value, string Close);

    Actions RegisterActions(UiRegistration r) => new(
        r.RegisterAction("page", a => Open(Enum.Parse<Page>(a.Argument))),
        r.RegisterAction("toggle", a => Flip(Enum.Parse<Toggle>(a.Argument))),
        r.RegisterAction("tier", a => CycleTier(Enum.Parse<Tier>(a.Argument))),
        r.RegisterAction("act", a => Act(a.Argument)),
        r.RegisterAction("pick", a => Pick(a.Argument)),
        r.RegisterAction("cats", a => _cats = a.Text),
        r.RegisterAction("hour", a => _hour = a.Text),
        r.RegisterAction("name", a => _name = a.Text),
        r.RegisterAction("search", a => _search = a.Text),
        r.RegisterAction("value", a => _value = a.Text),
        r.RegisterAction("close", _ => _open = false));

    void Open(Page page)
    {
        _page = page;
        _search = "";
        _status = "";
    }

    void Flip(Toggle which)
    {
        bool on = Running?.View.On[(int)which] ?? false;
        Send(new SetToggle(which, !on) { Tick = T });
        if (which == CheatMenu.Toggle.Raids && !on) Send(new CameraAt(_camera) { Tick = T });
    }

    void CycleTier(Tier which) => Send(new CycleTier(which) { Tick = T });

    /// <summary>Queues a command for the running world, stamped with the current tick.</summary>
    void Send(SimCommand command) => context.Enqueue(command);

    /// <summary>The tick a command is stamped with.</summary>
    long T => context.Tick;

    void Say(string text) => _status = text;

    string Tr(string english) => context.Tr(english);

    Simulation.Population.PopulationData Population => context.Population;

    // ----- targets -----

    IReadOnlyList<CharacterId> Selected => _selected;

    IReadOnlyList<CharacterId> Squad => _snapshot is { } s ? [.. s.Characters.Where(c => c.IsPlayer).Select(c => c.Id)] : [];

    /// <summary>The characters within <see cref="NearCamera"/> of the camera's point; the player's own only with <paramref name="own"/>.</summary>
    IReadOnlyList<CharacterId> Near(bool own) => _snapshot is { } s
        ? [.. s.Characters.Where(c => (own || !c.IsPlayer) && Vector3.Distance(c.Position, _camera) <= NearCamera).Select(c => c.Id)]
        : [];

    IReadOnlyList<CharacterId> SelectedNpcs => _snapshot is { } s ? [.. s.Characters.Where(c => c.Selected && !c.IsPlayer).Select(c => c.Id)] : [];
}
