using System.Globalization;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.CityBuilder;

/// <summary>
/// The original's Settlement window (its CTY button or Ctrl+Shift+H; here the key, the town panel's Settlement line and a selected CITY building's
/// panel button): every CITY building with its state, the offer with Sell and Refuse, a trade house's shop buttons, a guard post's hiring, a
/// tavern's beds, guests and share. It opens by itself, and pauses the game, when buyers make an offer. What it shows is the system's published
/// view; a press queues a command. Interface thread only.
/// </summary>
internal sealed class CityWindow(ModContext context, CitySettings settings, CityNames names) : IUiExtension
{
    const float Width = 560, Height = 600, Line = 22;

    /// <summary>The shop names by offset (General Goods ... Crossbow Shop; 5 is the tavern).</summary>
    static readonly string[] ShopNames =
        ["General Goods", "Weapon Shop", "Armour Shop", "Prosthetics Workshop", "Food Shop", "Tavern", "Clothing Shop", "Tool Shop", "Crossbow Shop"];

    volatile bool _open;
    int _seenOffers;
    CitySystem? _seenSystem;

    public string Id => "dxseba.citybuilder.ui";

    public void Toggle() => _open = !_open;

    public void Register(UiRegistration registration)
    {
        var styles = new Styles(
            registration.RegisterStyle("panel", new() { Background = new(.10f, .085f, .07f, .96f), Text = new(.91f, .86f, .74f, 1) }),
            registration.RegisterStyle("title", new() { Text = new(.98f, .90f, .62f, 1), TextPadding = new(6, 1, 6, 1) }),
            registration.RegisterStyle("text", new() { Text = new(.91f, .86f, .74f, 1), TextPadding = new(6, 1, 6, 1) }),
            registration.RegisterStyle("block", new() { Background = new(.15f, .12f, .09f, .9f) }),
            registration.RegisterStyle("button", new()
            {
                Background = new(.20f, .15f, .11f, .95f),
                Hover = new(.32f, .22f, .16f, 1),
                Disabled = new(.14f, .12f, .10f, .8f),
                TextPadding = new(6, 2, 6, 2),
            }));
        var actions = new Actions(
            registration.RegisterAction("sell", a => Sell(a.Argument)),
            registration.RegisterAction("refuse", a => Refuse(a.Argument)),
            registration.RegisterAction("hire", a => Hire(a.Argument)),
            registration.RegisterAction("shop", a => Shop(a.Argument)),
            registration.RegisterAction("close", _ => _open = false));
        registration.Contribute(UiSlots.Overlay, _ => Build(styles, actions), order: 57);
        registration.AddBuildingActions(Buttons);
        registration.AddTownPanelLines(_ => [new UiTownLine("Settlement", "CTY", null, Toggle)]);
        string toggle = registration.RegisterAction("toggle", _ => Toggle());
        registration.AddScreenControls(UiScreens.Hud, "ShortcutsPanel", UiAnchorPlacement.Above, _ => HudButton(styles, toggle));
        registration.AddText(names.Text);
        registration.AddKeyInput(input => _open && input.Cancel && !(_open = false));
    }

    /// <summary>
    /// The original's CTY button on the bottom panel (it adds one to the shortcuts panel, <c>CityMod_ShortcutCityButton</c>): here just above that
    /// panel's right end.
    /// </summary>
    UiNode HudButton(Styles s, string toggle) =>
        new("city-hud", UiNodeKind.Button)
        {
            Placement = new(52, 24, Right: 0, Bottom: 2),
            Text = "CTY",
            Action = toggle,
            Style = s.Button,
        };

    /// <summary>Opens the window on a new offer and pauses the game (the original's auto-open); host thread, from the frame.</summary>
    public void Frame(ModFrame frame)
    {
        if (CityBuilderMod.Running is not { } system) return;

        var view = system.View;
        if (!ReferenceEquals(system, _seenSystem))
        {
            (_seenSystem, _seenOffers) = (system, view.OfferSerial);
            return;
        }

        if (view.OfferSerial <= _seenOffers) return;

        _seenOffers = view.OfferSerial;
        _open = true;
        context.Interface.Pause(true);
    }

    void Sell(string site) => context.Enqueue(new SellPlaceOrder(site) { Tick = context.Tick });

    void Refuse(string site) => context.Enqueue(new RefusePlaceOrder(site) { Tick = context.Tick });

    void HireGuards(string site, int count) => context.Enqueue(new HireGuardsOrder(site, count) { Tick = context.Tick });

    void Hire(string argument)
    {
        var parts = argument.Split('|');
        if (parts.Length == 2 && int.TryParse(parts[1], CultureInfo.InvariantCulture, out int count)) HireGuards(parts[0], count);
    }

    void Shop(string argument)
    {
        var parts = argument.Split('|');
        if (parts.Length == 2 && int.TryParse(parts[1], CultureInfo.InvariantCulture, out int shop))
            context.Enqueue(new ChooseShopOrder(parts[0], shop) { Tick = context.Tick });
    }

    /// <summary>A selected CITY building's panel: Sell and Refuse while an offer waits, Hire a guard on a post, else the window.</summary>
    IEnumerable<UiBuildingAction> Buttons(UiBuildingContext c)
    {
        if (c.Building.SiteId is not { } site || CityBuilderMod.Running?.View.Find(site) is not { } place) return [];

        if (place.State == PlaceState.Offer)
        {
            return [new UiBuildingAction(context.Tr("Sell — ") + Cats(place.Price), null, () => Sell(site)),
                new UiBuildingAction("Refuse", null, () => Refuse(site))];
        }

        if (place.Kind == CityKind.GuardPost && place.Guards + place.Pending < place.Capacity)
            return [new UiBuildingAction(context.Tr("Hire a guard — ") + Cats(settings.HireCost), null, () => HireGuards(site, 1)),
                new UiBuildingAction("Settlement", null, () => _open = true)];

        return [new UiBuildingAction("Settlement", null, () => _open = true)];
    }

    string Cats(long amount) => amount.ToString("N0", CultureInfo.InvariantCulture) + context.Tr(" cats");

    #region Layout

    readonly record struct Styles(string Panel, string Title, string Text, string Block, string Button);

    readonly record struct Actions(string Sell, string Refuse, string Hire, string Shop, string Close);

    UiNode Build(Styles s, Actions a)
    {
        if (!_open || CityBuilderMod.Running?.View is not { } view) return new UiNode("city-hidden") { Visible = false };

        var root = new UiNode("city") { Placement = UiPlacement.Fill, Pickable = false };
        var window = new UiNode("city-window", UiNodeKind.Panel)
        {
            Placement = new(Width, Height, Left: 60, Top: 90),
            Style = s.Panel,
            Flow = UiFlow.Column,
            Padding = 6,
            Gap = 4,
            Layer = UiOverlayLayer.Popup,
        };

        var head = new UiNode("city-head") { Placement = new(UiLength.Fill, Line + 4), Flow = UiFlow.Row, Gap = 4 };
        head.Children.Add(Label("city-title", "Settlement", s.Title, Width - 100));
        head.Children.Add(Button("city-close", "X", a.Close, "", s.Button, 60));
        window.Children.Add(head);

        var list = new UiNode("city-list", UiNodeKind.ScrollView)
        {
            Placement = new(UiLength.Fill, Height - Line - 24),
            Flow = UiFlow.Column,
            Gap = 6,
        };
        if (view.Places.Count == 0)
        {
            list.Children.Add(Label("city-none", "No settlement yet", s.Text));
            const string hint = "Build something from the CITY category: a residence, a trade house, a guard post or a tavern.";
            list.Children.Add(Label("city-none-2", hint, s.Text, lines: 2));
        }

        foreach (var place in view.Places.OrderBy(p => p.State == PlaceState.Offer ? 0 : 1).ThenBy(p => p.Site, StringComparer.Ordinal))
            list.Children.Add(Block(place, s, a));

        window.Children.Add(list);
        root.Children.Add(window);
        return root;
    }

    UiNode Block(PlaceView p, Styles s, Actions a)
    {
        var lines = Lines(p);
        var buttons = ButtonsOf(p, s, a);
        int rows = buttons.Count == 0 ? 0 : (buttons.Count + 3) / 4;
        var block = new UiNode("city-b-" + p.Site, UiNodeKind.Panel)
        {
            Placement = new(UiLength.Fill, 8 + (lines.Count + 1) * (Line + 1) + rows * (Line + 5)),
            Style = s.Block,
            Flow = UiFlow.Column,
            Padding = 4,
            Gap = 1,
        };

        block.Children.Add(Label("city-t-" + p.Site, p.Title, s.Title));
        for (int i = 0; i < lines.Count; i++) block.Children.Add(Label($"city-l-{p.Site}-{i}", lines[i], s.Text));
        for (int r = 0; r < rows; r++)
        {
            var row = new UiNode($"city-r-{p.Site}-{r}") { Placement = new(UiLength.Fill, Line + 4), Flow = UiFlow.Row, Gap = 4 };
            foreach (var button in buttons.Skip(r * 4).Take(4)) row.Children.Add(button);
            block.Children.Add(row);
        }

        return block;
    }

    List<string> Lines(PlaceView p)
    {
        string T(string text) => context.Tr(text);
        string hours = " " + T(" (~") + Math.Ceiling(p.HoursLeft).ToString(CultureInfo.InvariantCulture) + T(" h)");
        string shop = T(ShopNames[Math.Clamp(p.Shop, 0, ShopNames.Length - 1)]);
        return (p.Kind, p.State) switch
        {
            (CityKind.Residence, PlaceState.Waiting) => [T("[waiting] ") + T("Waiting for settlers") + hours],
            (CityKind.Residence, PlaceState.Offer) => [T("[offer] ") + T("Settlers want to buy the house"),
                T("They offer ") + Cats(p.Price) + T(" for the house and want to live in it.")],
            (CityKind.Residence, _) => [T("[sold] ") + T("Sold, people live here"), T("Residents: ") + p.People],
            (CityKind.TradeHouse, PlaceState.Waiting) => [T("[waiting] ") + T("Waiting for a trader") + hours, T("Future shop: ") + shop],
            (CityKind.TradeHouse, PlaceState.Offer) => [T("[offer] ") + T("A trader wants to buy the building"),
                T("Offers ") + Cats(p.Price) + T(" for the building to run a \"") + shop + "\"."],
            (CityKind.TradeHouse, _) => [T("[open] ") + T("Sold, the shop is open"), T("Shop: ") + shop + " · " + T("Trader: ") + p.People,
                T("Tax: ") + Cats((long)(p.Price * settings.TaxPercentPerDay / 100.0 + 0.5)) + T(" a day, received so far: ") + Cats(p.Income)],
            (CityKind.GuardPost, _) => [T("Guards: ") + p.Guards.ToString(CultureInfo.InvariantCulture) + " / "
                + p.Capacity.ToString(CultureInfo.InvariantCulture)
                + (p.Pending > 0 ? T("  (+") + p.Pending.ToString(CultureInfo.InvariantCulture) + T(" on their way)") : ""),
                T("Hiring: ") + Cats(settings.HireCost) + T(" per guard")],
            (CityKind.Tavern, PlaceState.Open) => [T("The tavern is open") + " · " + T("Innkeeper: ") + p.People,
                T("Beds: ") + p.Beds.ToString(CultureInfo.InvariantCulture) + T(" · seats: ") + p.Seats.ToString(CultureInfo.InvariantCulture)
                + " · " + T("Guests last night: ") + p.LastGuests.ToString(CultureInfo.InvariantCulture),
                T("Your share so far: ") + Cats(p.Income)],
            _ => [T("Waiting for an innkeeper") + hours],
        };
    }

    List<UiNode> ButtonsOf(PlaceView p, Styles s, Actions a)
    {
        var list = new List<UiNode>();
        if (p.State == PlaceState.Offer)
        {
            list.Add(Button($"city-sell-{p.Site}", context.Tr("Sell — ") + Cats(p.Price), a.Sell, p.Site, s.Button, 200));
            list.Add(Button($"city-refuse-{p.Site}", "Refuse", a.Refuse, p.Site, s.Button, 120));
        }
        else if (p.Kind == CityKind.GuardPost)
        {
            bool room = p.Guards + p.Pending < p.Capacity;
            string hire = context.Tr("Hire a guard — ") + Cats(settings.HireCost);
            list.Add(Button($"city-hire-{p.Site}", hire, a.Hire, p.Site + "|1", s.Button, 220, room));
            list.Add(Button($"city-full-{p.Site}", "Hire to full", a.Hire, p.Site + "|0", s.Button, 140, room));
        }

        if (p.Kind == CityKind.TradeHouse && p.State != PlaceState.Offer)
        {
            foreach (int shop in CityCatalog.ShopKinds)
                list.Add(Button($"city-shop-{p.Site}-{shop}", ShopNames[shop], a.Shop, p.Site + "|" + shop, s.Button, 128, shop != p.Shop));
        }

        return list;
    }

    static UiNode Label(string id, string text, string style, float width = -1, int lines = 1) =>
        new(id, UiNodeKind.Label)
        {
            Placement = new(width < 0 ? UiLength.Fill : width, Line * lines),
            Text = text,
            Style = style,
        };

    static UiNode Button(string id, string text, string action, string argument, string style, float width, bool enabled = true) =>
        new(id, UiNodeKind.Button)
        {
            Placement = new(width, Line + 4),
            Text = text,
            Action = action,
            Argument = argument,
            Enabled = enabled,
            Style = style,
        };

    #endregion
}
