using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.ShopCounterRestock;

/// <summary>
/// The original's settings window ("Shop counter restock - counter", opened by Restock... on a selected player shop counter's panel): the
/// assigned worker, an item search by name or FCS id, the counter's whitelist with targets, Add / update, Set target, Remove, Enable / Disable
/// restock, Close and the status line. Esc closes it. Its choices are queued as commands; what it shows is the system's published view.
/// Interface thread only.
/// </summary>
internal sealed partial class RestockWindow(ModContext context, ItemCatalog catalog, IReadOnlyList<string> counterRecords) : IUiExtension
{
    const int ResultLines = 8;

    volatile string? _counter;
    string _search = "";
    string _targetText = "10";
    string? _picked;
    int _workerCursor = -1;

    /// <summary>
    /// The window of the running interface, for the menu row's patch (<see cref="MenuPatch"/>).
    /// </summary>
    internal static volatile RestockWindow? Instance;

    public string Id => "dxseba.shopcounterrestock.ui";

    public void Register(UiRegistration registration)
    {
        Instance = this;
        var styles = RegisterStyles(registration);
        var actions = RegisterActions(registration);
        registration.Contribute(UiSlots.Overlay, _ => Build(styles, actions), order: 56);
        registration.AddBuildingActions(Buttons);
        registration.AddKeyInput(input => _counter is not null && input.Cancel && Close());
    }

    /// <summary>
    /// Restock... on the panel of a selected shop counter the player owns.
    /// </summary>
    IEnumerable<UiBuildingAction> Buttons(UiBuildingContext context)
    {
        var building = context.Building;
        if (!building.Owned || building.SiteId is not { } site || building.Record is not { } record || !counterRecords.Contains(record)) return [];

        return [new UiBuildingAction("Restock...", null, () => Open(site))];
    }

    internal void Open(string site)
    {
        _counter = site;
        _picked = null;
        _workerCursor = -1;
    }

    bool Close()
    {
        _counter = null;
        return true;
    }

    Actions RegisterActions(UiRegistration r) => new(
        r.RegisterAction("search", a => _search = a.Text),
        r.RegisterAction("target", a => _targetText = a.Text),
        r.RegisterAction("pick", a => _picked = a.Argument),
        r.RegisterAction("worker", a => Cycle(a.Argument)),
        r.RegisterAction("assign", a => AssignChosen(a.Argument == "none")),
        r.RegisterAction("add", _ => AddOrUpdate()),
        r.RegisterAction("remove", _ => RemovePicked()),
        r.RegisterAction("enable", a => Send(new SetEnabled(_counter!, a.Argument == "on") { Tick = context.Tick })),
        r.RegisterAction("close", _ => Close()));

    static Styles RegisterStyles(UiRegistration r) => new(
        r.RegisterStyle("panel", new() { Background = new(.10f, .085f, .07f, .96f), Text = new(.91f, .86f, .74f, 1) }),
        r.RegisterStyle("text", new() { Text = new(.91f, .86f, .74f, 1), TextPadding = new(6, 1, 6, 1) }),
        r.RegisterStyle("job", new() { Text = new(0xaf / 255f, 0xa6 / 255f, 0x8b / 255f, 1), TextPadding = new(6, 1, 6, 1) }),
        r.RegisterStyle("button", new()
        {
            Background = new(.20f, .15f, .11f, .95f),
            Hover = new(.32f, .22f, .16f, 1),
            Active = new(.40f, .28f, .18f, 1),
            Disabled = new(.14f, .12f, .10f, .8f),
            TextPadding = new(6, 2, 6, 2),
        }),
        r.RegisterStyle("list", new() { Background = new(.06f, .05f, .04f, .9f) }));

    readonly record struct Styles(string Panel, string Text, string Job, string Button, string List);

    readonly record struct Actions(string Search, string Target, string Pick, string Worker, string Assign, string Add, string Remove, string Enable,
        string Close);

    void Cycle(string by)
    {
        if (ShopCounterRestockMod.Running?.View is not { Workers.Count: > 0 } view || !int.TryParse(by, out int step)) return;

        int count = view.Workers.Count;
        _workerCursor = ((_workerCursor < 0 ? 0 : _workerCursor + step) % count + count) % count;
    }

    void AssignChosen(bool none)
    {
        if (_counter is not { } counter || ShopCounterRestockMod.Running?.View is not { } view) return;
        if (none)
        {
            Send(new AssignWorker(counter, CharacterId.None) { Tick = context.Tick });
            return;
        }

        if (_workerCursor < 0 || _workerCursor >= view.Workers.Count) return;

        Send(new AssignWorker(counter, view.Workers[_workerCursor].Id) { Tick = context.Tick });
    }

    /// <summary>
    /// Add / update: the picked item (a search result or a whitelist line) at the typed target.
    /// </summary>
    void AddOrUpdate()
    {
        if (_counter is not { } counter || _picked is not { } item) return;

        int target = int.TryParse(_targetText.Trim(), out int t) ? t : 0;
        Send(new SetTarget(counter, item, target) { Tick = context.Tick });
    }

    void RemovePicked()
    {
        if (_counter is { } counter && _picked is { } item) Send(new RemoveItem(counter, item) { Tick = context.Tick });
    }

    void Send(SimCommand command)
    {
        if (_counter is null) return;

        context.Enqueue(command);
    }
}
