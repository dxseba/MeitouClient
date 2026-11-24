using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.OrganizeTrader;

/// <summary>
/// What the trade window's controls keep between frames: the query, the sort, the open trade and the trader's money when it opened (the deal
/// balance), and the requests for the cursor. Host thread only.
/// </summary>
sealed class TraderState(ModContext mod, TraderSettings settings)
{
    public string Typed { get; private set; } = "";

    public SortMode Mode { get; private set; }

    public bool Descending { get; private set; }

    /// <summary>
    /// Whether the preset list is open.
    /// </summary>
    public bool ListOpen { get; set; }

    /// <summary>
    /// Whether the controls are on (Ctrl+Shift+F8; on at start, not saved, as the original's).
    /// </summary>
    public bool ControlsOn { get; private set; } = true;

    /// <summary>
    /// Whether the controls show now: the mod enabled and its controls on.
    /// </summary>
    public bool Active => settings.Enabled && ControlsOn;

    /// <summary>
    /// The trader's money when this window opened; the deal is that less the money now.
    /// </summary>
    public long StartMoney { get; private set; }

    CharacterId _trader = CharacterId.None;
    bool _focus;
    int _frame, _builtFrame = -10;

    /// <summary>
    /// The frame callback: counts the frame and follows the open trade (a new trader starts a new deal and clears the query).
    /// </summary>
    public void Frame(ModFrame frame)
    {
        _frame++;
        if (frame.Snapshot.Trade is not { } trade)
        {
            _trader = CharacterId.None;
            ListOpen = false;
            Patches.Shorten = 0;
            return;
        }

        if (trade.Trader == _trader) return;

        _trader = trade.Trader;
        StartMoney = trade.ShopMoney;
        ListOpen = false;
        if (Typed.Length > 0 && settings.DebugLogging) mod.Log("search query reset reason=target_changed");
        Typed = "";
        _focus = settings.AutoFocus;
    }

    /// <summary>
    /// Notes that the controls were built this frame.
    /// </summary>
    public void Built() => _builtFrame = _frame;

    /// <summary>
    /// Whether the controls were built in the last frames.
    /// </summary>
    public bool Shown => _frame - _builtFrame <= 2;

    public void SetQuery(string typed)
    {
        if (typed == Typed) return;

        Typed = typed;
        if (settings.DebugLogging) mod.Log($"search input changed query=\"{typed}\"");
    }

    public void SetSort(SortMode mode, bool descending)
    {
        Mode = mode;
        Descending = descending;
        if (settings.DebugLogging) mod.Log($"sort mode={mode} descending={descending}");
    }

    public void Toggle()
    {
        ControlsOn = !ControlsOn;
        ListOpen = false;
        mod.Log(ControlsOn ? "controls toggled ON" : "controls toggled OFF");
    }

    /// <summary>
    /// Asks for the cursor in the query box at the next build.
    /// </summary>
    public void RequestFocus() => _focus = true;

    /// <summary>
    /// Takes the request for the cursor (true once per request).
    /// </summary>
    public bool TakeFocus()
    {
        bool focus = _focus;
        _focus = false;
        return focus;
    }
}
