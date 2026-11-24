using System.Globalization;
using Meitou.Data.Ui;
using Meitou.Game.Host.Input;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation.Trading;

namespace Meitou.OrganizeTrader;

/// <summary>
/// The trade window's changes (screen <c>trader</c>): the stock grid shown searched and sorted (screen overrides on its <c>trade-item-*</c> cells:
/// moved or left out, <see cref="StockView"/>), ARRANGE hidden, the native "Money:" line replaced by the header, and the controls
/// (<see cref="TraderControls"/>) laid over <c>Content</c>. Ctrl+F or / puts the cursor in the query box, Esc closes the preset list and
/// Ctrl+Shift+F8 turns everything off and on for the session.
/// </summary>
sealed class TraderUi(ModContext mod, TraderSettings settings, TraderState state, StockView stock) : IUiExtension
{
    const string Screen = "trader", CellPrefix = "trade-item-";

    static readonly string[] CellParts = ["*-background", "*-icon", "*-charge*", "?", "??", "???", "????"];

    /// <summary>
    /// The widgets' rectangles this frame (null when the layout lacks one).
    /// </summary>
    readonly TraderRects _rects = new();

    /// <summary>
    /// The stock as shown this frame and the grid's cell size.
    /// </summary>
    StockShown? _shown;
    float _cellWidth, _cellHeight;
    int _faulted;

    TraderControls? _controls;

    public const string ExtensionId = "dxseba.organizetrader.ui";

    public string Id => ExtensionId;

    public void Register(UiRegistration registration)
    {
        _controls = new TraderControls(mod, settings, state, _rects, stock, registration);
        registration.AddScreenOverrides(Screen, "Content", c => Capture(c, _rects.Start));
        registration.AddScreenOverrides(Screen, "datapanel", c => Capture(c, r => _rects.Money = r));
        registration.AddScreenOverrides(Screen, "scrollview_backpack_content", c => Capture(c, r => _rects.View = r));
        registration.AddScreenOverrides(Screen, "ArrangeButton", Arrange);
        registration.AddScreenOverrides(Screen, "datapanel-*", MoneyLine);
        registration.AddScreenOverrides(Screen, "backpack_content", Grid);
        // One source per cell part, as a source is asked for at most 512 widgets a frame: the background, picture, charge bar and the item's text
        // box (its name the prefix and the index alone).
        foreach (string part in CellParts) registration.AddScreenOverrides(Screen, CellPrefix + part, Cell);
        registration.AddScreenControls(Screen, "Content", UiAnchorPlacement.Inside, Controls);
        registration.AddKeyInput(Key);
    }

    UiWidgetOverride? Capture(UiScreenContext context, Action<UiRect> keep)
    {
        keep(context.Anchor);
        return null;
    }

    /// <summary>
    /// ARRANGE: its place is the controls' row; it is hidden while they show (the original hides it for good).
    /// </summary>
    UiWidgetOverride? Arrange(UiScreenContext context)
    {
        _rects.Arrange = context.Anchor;
        return state.Active ? new UiWidgetOverride { Visible = false } : null;
    }

    /// <summary>
    /// The native "Money:" line, left out while the header shows.
    /// </summary>
    UiWidgetOverride? MoneyLine(UiScreenContext context) =>
        state.Active && _rects.Money is not null ? new UiWidgetOverride { Visible = false } : null;

    /// <summary>
    /// The stock grid: the frame's layout of the stock and the cell size.
    /// </summary>
    UiWidgetOverride? Grid(UiScreenContext context)
    {
        _shown = null;
        if (!state.Active || context.Snapshot.Trade is not { } trade) return null;

        _cellWidth = context.Anchor.Width / Math.Max(1, trade.StockWidth);
        _cellHeight = context.Anchor.Height / Math.Max(1, trade.StockHeight);
        _shown = Shown(trade);
        return null;
    }

    StockShown? Shown(TradeView trade)
    {
        try
        {
            var shown = stock.Of(trade, state.Typed, state.Mode, state.Descending);
            if (shown.Skipped is { } reason && settings.DebugLogging) mod.Log($"search packing skipped reason={reason}");
            return shown;
        }
        catch (Exception e)
        {
            if (Interlocked.Exchange(ref _faulted, 1) == 0) mod.LogError($"searching the stock failed, shown as the game lays it out: {e}");
            return null;
        }
    }

    /// <summary>
    /// A part of a stock item's cell: left out when the item does not match, moved with its item when the layout moves it.
    /// </summary>
    UiWidgetOverride? Cell(UiScreenContext context)
    {
        if (_shown is not { } shown || Index(context.AnchorWidget) is not { } index) return null;
        if (shown.Hidden.Contains(index)) return new UiWidgetOverride { Visible = false };
        if (shown.Moves is null || !shown.Moves.TryGetValue(index, out var move)) return null;

        float dx = (move.X - move.FromX) * _cellWidth, dy = (move.Y - move.FromY) * _cellHeight;
        if (dx == 0 && dy == 0) return null;

        var at = context.Anchor;
        return new UiWidgetOverride { Bounds = at with { X = at.X + dx, Y = at.Y + dy } };
    }

    /// <summary>
    /// The stock index a cell part's name carries (<c>trade-item-{index}</c>, then its part's suffix).
    /// </summary>
    static int? Index(string name)
    {
        var rest = name.AsSpan(CellPrefix.Length);
        int end = rest.IndexOf('-');
        var digits = end < 0 ? rest : rest[..end];
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int index) ? index : null;
    }

    UiNode? Controls(UiScreenContext context)
    {
        if (!state.Active || context.Snapshot.Trade is not { } trade || _controls is null)
        {
            Patches.Shorten = 0;
            return null;
        }

        state.Built();
        return _controls.Build(context, trade, _shown ?? StockShown.Native(trade.Stock.Count, trade.Stock.Sum(i => i.Quantity)));
    }

    /// <summary>
    /// Ctrl+Shift+F8 turns the controls off and on (left to the game and the other mods too); while they show, Ctrl+F or / asks for the cursor
    /// and Esc closes an open preset list.
    /// </summary>
    bool Key(UiKeyInput input)
    {
        if (input.Kind != UiKeyInputKind.Down) return false;

        if (input.Key == Game.Host.Input.Key.F8 && input.Ctrl && input.Shift && !input.Alt)
        {
            state.Toggle();
            return false;
        }

        if (!state.Shown) return false;
        if (input.Cancel && state.ListOpen)
        {
            state.ListOpen = false;
            return true;
        }

        bool find = input.Key == Game.Host.Input.Key.F && input.Ctrl && !input.Shift && !input.Alt;
        bool slash = input.Key == Game.Host.Input.Key.Slash && !input.Ctrl && !input.Alt;
        if (!find && !slash) return false;

        state.RequestFocus();
        return true;
    }
}

/// <summary>
/// The trade window's widget rectangles as arranged this frame (screen pixels).
/// </summary>
sealed class TraderRects
{
    public UiRect? Content, Money, View, Arrange;

    /// <summary>
    /// A new frame's arrangement: <c>Content</c> (asked first), the others forgotten until they are asked.
    /// </summary>
    public void Start(UiRect content)
    {
        Content = content;
        Money = View = Arrange = null;
    }
}
