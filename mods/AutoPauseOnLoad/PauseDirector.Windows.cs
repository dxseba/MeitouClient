using Meitou.Modding;

namespace Meitou.AutoPauseOnLoad;

/// <summary>
/// The trade and inventory pauses (<b>Verified</b>, DLL decompile, its UI step): trade is the trade window; inventory is an inventory window while
/// no trade shows. Opening one pauses the game when it runs, and the window owns that pause; when the game is already paused it owns it only when
/// the other window owns the pause (handed over, the fork's v2 fix for switching characters while trading), else it notes the game was paused
/// before. Closing one it owns, with its resume setting on, resumes the game unless a window whose pause is on still shows.
/// </summary>
sealed partial class PauseDirector
{
    /// <summary>
    /// One window's state: whether it showed at the last frame, owns the pause, and found the game paused by someone else when it opened.
    /// </summary>
    struct Window
    {
        public bool Open, Owns, FoundPaused;
    }

    Window _trade, _inventory;

    void ForgetWindows() => (_trade, _inventory) = (default, default);

    void WindowStep(ModFrame frame)
    {
        bool trade = frame.IsOpen("trader") || frame.Snapshot.Trade is not null;
        bool inventory = !trade && frame.IsOpen("inventory");
        bool tradeOpened = trade && !_trade.Open, tradeClosed = !trade && _trade.Open;
        bool inventoryOpened = inventory && !_inventory.Open, inventoryClosed = !inventory && _inventory.Open;

        if (tradeOpened && Settings.PauseOnTrade) Opened(ref _trade, _inventory, "trade");
        if (inventoryOpened && Settings.PauseOnInventory) Opened(ref _inventory, _trade, "inventory_open");

        _trade.Open = trade;
        _inventory.Open = inventory;
        if (tradeClosed) Closed(ref _trade, Settings.PauseOnTrade && Settings.ResumeAfterTrade, "trade_closed");
        if (inventoryClosed) Closed(ref _inventory, Settings.PauseOnInventory && Settings.ResumeAfterInventory, "inventory_closed");
    }

    void Opened(ref Window window, Window other, string source)
    {
        window.Owns = false;
        window.FoundPaused = false;
        if (Paused)
        {
            if (other.Owns && !other.FoundPaused)
            {
                window.Owns = true;
                Debug("pause ownership passed to " + source);
            }
            else window.FoundPaused = true;

            return;
        }

        window.Owns = PauseFor(source);
    }

    void Closed(ref Window window, bool resumes, string source)
    {
        bool owned = window.Owns && !window.FoundPaused;
        window.Owns = false;
        window.FoundPaused = false;
        if (!resumes || !owned || !Paused) return;
        if (_trade.Open && Settings.PauseOnTrade || _inventory.Open && Settings.PauseOnInventory) return;

        if (game.Pause(false)) Debug("resumed=true source=" + source);
    }
}
