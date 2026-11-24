using System.Globalization;
using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation.Trading;

namespace Meitou.OrganizeTrader;

/// <summary>
/// The controls over the trade window's <c>Content</c>: the header (trader's money in the money plate, the deal balance at the centre, the player's
/// money at the right) and, in ARRANGE's row across the stock's width, the query box (hint, clear button inside, counter after it) and the sort
/// (its "Sort" caption, the preset list and the direction button "^" / "v"). A row too narrow for both puts the sort on a row of its own above.
/// </summary>
sealed class TraderControls
{
    const float Gap = 6;

    readonly ModContext _mod;
    readonly TraderSettings _settings;
    readonly TraderState _state;
    readonly TraderRects _rects;
    readonly StockView _stock;

    /// <summary>
    /// The last built window's trade and command queue, for the presses' layout orders.
    /// </summary>
    TradeView? _trade;
    Action<Simulation.SimCommand>? _enqueue;
    Func<long>? _tick;
    readonly string _text, _right, _hint, _count, _button, _typed, _clear, _list, _pick, _direction;

    public TraderControls(ModContext mod, TraderSettings settings, TraderState state, TraderRects rects, StockView stock,
        UiRegistration registration)
    {
        _stock = stock;
        _mod = mod;
        _settings = settings;
        _state = state;
        _rects = rects;
        var ink = new Vector4(.91f, .86f, .74f, 1);
        _text = registration.RegisterStyle("text", new() { Text = ink });
        _right = registration.RegisterStyle("right", new() { Text = ink, TextAlign = "Right VCenter" });
        _hint = registration.RegisterStyle("hint", new() { Text = new(.6f, .58f, .52f, 1) });
        _count = registration.RegisterStyle("count", new() { Text = ink, TextAlign = "Center" });
        _button = registration.RegisterStyle("button", new() { Skin = "Kenshi_Button1Skin", TextAlign = "Center" });
        _typed = registration.RegisterAction("typed", c => Changed(() => state.SetQuery(c.Text)));
        _clear = registration.RegisterAction("clear", _ => Changed(() => state.SetQuery("")));
        _list = registration.RegisterAction("list", _ => state.ListOpen = !state.ListOpen);
        _pick = registration.RegisterAction("pick", Pick);
        _direction = registration.RegisterAction("direction", _ => Changed(() => state.SetSort(state.Mode, !state.Descending)));
    }

    void Pick(UiActionContext c)
    {
        _state.ListOpen = false;
        if (int.TryParse(c.Argument, NumberStyles.None, CultureInfo.InvariantCulture, out int mode) && mode < StockView.Presets.Length)
            Changed(() => _state.SetSort((SortMode)mode, _state.Descending));
    }

    /// <summary>
    /// Applies a change of the query or sort, then has the trader's items moved to match, as the original does: the layout order
    /// (<see cref="OrganizeStock"/>) and the game's own <c>ArrangeStock</c>, which <see cref="StockOrderSystem"/> answers. Nothing is sent for
    /// an empty query with the Default preset.
    /// </summary>
    void Changed(Action change)
    {
        change();
        if (_trade is not { } trade || _enqueue is not { } enqueue || _tick is not { } tick) return;

        var order = _stock.Of(trade, _state.Typed, _state.Mode, _state.Descending).Order;
        if (order.Length == 0) return;

        enqueue(new OrganizeStock(order) { Tick = tick() });
        enqueue(new ArrangeStock { Tick = tick() });
    }

    /// <summary>
    /// The controls for this frame's trade window.
    /// </summary>
    public UiNode Build(UiScreenContext context, TradeView trade, StockShown shown)
    {
        _trade = trade;
        _enqueue = context.Enqueue;
        _tick = () => context.Tick;
        var root = new UiNode("ott-root") { Placement = UiPlacement.Fill, Pickable = false };
        var origin = new Vector2(context.Anchor.X, context.Anchor.Y);
        AddHeader(root, context, trade, origin);
        if (_rects.Arrange is { } arrange && _rects.View is { } view) AddRow(root, context, shown, Local(arrange, origin), Local(view, origin));

        return root;
    }

    static UiRect Local(UiRect r, Vector2 origin) => r with { X = r.X - origin.X, Y = r.Y - origin.Y };

    #region Header

    /// <summary>
    /// "Trader: c.N" in the money plate's text area, "Deal: +c.N" (Good) or "-c.N" (Bad) centred in the window, "Player: c.N" at the right.
    /// </summary>
    void AddHeader(UiNode root, UiScreenContext context, TradeView trade, Vector2 origin)
    {
        if (_rects.Money is not { } money) return;

        var plate = Local(money, origin);
        float width = context.Anchor.Width;
        root.Children.Add(Label("ott-trader", $"{_mod.Tr("Trader")}: {Money(trade.ShopMoney)}", plate, "Left VCenter"));
        var row = new UiRect(plate.X, plate.Y, Math.Max(0, width - 2 * plate.X), plate.Height);
        if (_settings.ShowDealBalance) root.Children.Add(Label("ott-deal", Deal(_state.StartMoney - trade.ShopMoney), row, "Center"));

        root.Children.Add(Label("ott-player", $"{_mod.Tr("Player")}: {Money(trade.PlayerMoney)}", row, "Right VCenter"));
    }

    string Deal(long balance)
    {
        string amount = balance switch
        {
            > 0 => "#a8b774+" + Money(balance),
            < 0 => "#59231a-" + Money(-balance),
            _ => Money(0),
        };
        return $"{_mod.Tr("Deal")}: {amount}";
    }

    /// <summary>
    /// A sum as the window's own money line writes it.
    /// </summary>
    static string Money(long cats) => "c." + cats.ToString("N0", CultureInfo.InvariantCulture);

    UiNode Label(string id, string text, UiRect at, string align) => new(id, UiNodeKind.Label)
    {
        Placement = new(at.Width, at.Height, Left: at.X, Top: at.Y), Text = text,
        Style = align == "Left VCenter" ? _text : align == "Center" ? _count : _right,
    };

    #endregion

    #region Row

    void AddRow(UiNode root, UiScreenContext context, StockShown shown, UiRect arrange, UiRect view)
    {
        float h = arrange.Height, left = view.X, right = view.X + view.Width;
        string counter = Counter(shown);
        var m = RowMetrics.Of(context.Frame, _mod, _settings, counter, right - left, h);
        float sortTop = m.TwoRows ? arrange.Y - h - 4 : arrange.Y;
        Reserve(m.TwoRows, view, sortTop, context.Anchor.Height);
        AddSearch(root, m, counter, left, arrange.Y, h);
        AddSort(root, m, right - m.SortWidth, sortTop, h);
    }

    /// <summary>
    /// With two rows, the stock's scroll view is shortened at its next bind to end 2 px above the sort row (<see cref="Patches"/>); measured
    /// against the view as it would be unshortened, so the cut stays put.
    /// </summary>
    static void Reserve(bool twoRows, UiRect view, float sortTop, float contentHeight)
    {
        Patches.ContentHeight = contentHeight;
        float bottom = view.Y + view.Height + Patches.Applied;
        Patches.Shorten = twoRows ? Math.Max(0, bottom - (sortTop - 2)) : 0;
    }

    /// <summary>
    /// "shown / listed", then " | " and the shown units, as the settings ask; empty for neither.
    /// </summary>
    string Counter(StockShown shown)
    {
        string entries = _settings.ShowEntryCount ? $"{shown.Visible} / {shown.Total}" : "";
        string units = _settings.ShowQuantityCount ? shown.Quantity.ToString(CultureInfo.InvariantCulture) : "";
        return entries.Length > 0 && units.Length > 0 ? entries + " | " + units : entries + units;
    }

    void AddSearch(UiNode root, RowMetrics m, string counter, float x, float y, float h)
    {
        root.Children.Add(new UiNode("ott-edit", UiNodeKind.TextBox)
        {
            Placement = new(m.Field, h, Left: x, Top: y), Text = _state.Typed, Action = _typed, MaxLength = 64, Focus = _state.TakeFocus(),
        });
        if (_state.Typed.Length == 0)
            root.Children.Add(new UiNode("ott-hint", UiNodeKind.Label)
            {
                Placement = new(Math.Max(0, m.Field - 16), h, Left: x + 10, Top: y), Text = "Search items...", Style = _hint,
            });

        float clear = Math.Clamp(h - 4, 14, 32);
        if (_settings.ShowClearButton)
            root.Children.Add(new UiNode("ott-clear", UiNodeKind.Button)
            {
                Placement = new(clear, clear, Left: x + m.Field - clear - 2, Top: y + (h - clear) / 2), Text = "x", Style = _button, Action = _clear,
            });

        if (counter.Length > 0)
            root.Children.Add(new UiNode("ott-count", UiNodeKind.Label)
            {
                Placement = new(m.Count, h, Left: x + m.Field + Gap, Top: y), Text = counter, Style = _count,
            });
    }

    void AddSort(UiNode root, RowMetrics m, float x, float y, float h)
    {
        if (m.Label > 0)
            root.Children.Add(new UiNode("ott-sort-label", UiNodeKind.Label)
            {
                Placement = new(m.Label, h, Left: x, Top: y), Text = _mod.Tr("Sort"), Style = _text,
            });

        float combo = x + m.Label + (m.Label > 0 ? Gap : 0);
        root.Children.Add(new UiNode("ott-sort", UiNodeKind.Button)
        {
            Placement = new(m.Combo, h, Left: combo, Top: y), Text = _mod.Tr(StockView.Presets[(int)_state.Mode]) + "  v", Style = _button,
            Action = _list,
        });
        root.Children.Add(new UiNode("ott-direction", UiNodeKind.Button)
        {
            Placement = new(h, h, Left: combo + m.Combo + Gap, Top: y), Text = _state.Descending ? "v" : "^", Style = _button,
            Action = _direction,
        });
        if (_state.ListOpen) AddList(root, combo, y, Math.Max(m.Combo, m.ListWidth), h);
    }

    /// <summary>
    /// The open preset list, upward from the list button, the shown preset first in its place.
    /// </summary>
    void AddList(UiNode root, float x, float y, float width, float h)
    {
        int count = StockView.Presets.Length;
        for (int i = 0; i < count; i++)
        {
            float top = y - (count - i) * h;
            root.Children.Add(new UiNode($"ott-preset-{i}", UiNodeKind.Button)
            {
                Placement = new(width, h, Left: x, Top: top), Text = (i == (int)_state.Mode ? "> " : "") + _mod.Tr(StockView.Presets[i]),
                Style = _button, Action = _pick, Argument = i.ToString(CultureInfo.InvariantCulture),
            });
        }
    }

    #endregion
}

/// <summary>
/// The widths of the controls' row: the query box (the preferred width, shrunk to what is left but not below 120), the counter (its text plus 8),
/// the "Sort" caption (its text plus 8, at least 52, dropped when the row is short), the preset list button (the set width, else its longest
/// translated preset plus room for its arrow) and the direction button (square); <see cref="TwoRows"/> when search and sort do not fit side by
/// side even so.
/// </summary>
readonly record struct RowMetrics(float Field, float Count, float Label, float Combo, float ListWidth, bool TwoRows)
{
    const float Gap = 6, SortGap = 12;

    public float SortWidth => Label + (Label > 0 ? Gap : 0) + Combo + Gap + DirectionWidth;

    float DirectionWidth { get; init; }

    public static RowMetrics Of(UiFrameContext frame, ModContext mod, TraderSettings s, string counter, float width, float h)
    {
        float count = counter.Length > 0 ? frame.Measure(counter).X + 8 : 0;
        float label = Math.Max(52, frame.Measure(mod.Tr("Sort")).X + 8);
        float longest = StockView.Presets.Max(p => frame.Measure(mod.Tr(p) + "  v").X);
        float combo = s.SortListWidth > 0 ? s.SortListWidth : longest + 16;
        float list = frame.Measure("> ").X + longest + 16;
        float search = s.SearchFieldWidth;
        float countPart = count > 0 ? Gap + count : 0;

        float sort = label + Gap + combo + Gap + h;
        float field = Math.Max(120, Math.Min(search, width - countPart - SortGap - sort));
        if (field + countPart + SortGap + sort <= width) return new(field, count, label, combo, list, false) { DirectionWidth = h };

        sort -= label + Gap;
        field = Math.Max(120, Math.Min(search, width - countPart - SortGap - sort));
        if (field + countPart + SortGap + sort <= width) return new(field, count, 0, combo, list, false) { DirectionWidth = h };

        field = Math.Max(120, Math.Min(search, width - countPart));
        return new(field, count, label, combo, list, true) { DirectionWidth = h };
    }
}
