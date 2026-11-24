using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.OrganizeTrader;

/// <summary>
/// Port of Organize the Trader (MCM fork, workshop 3813852189, of Emkej8's original, Nexus 1886; GPLv3): search and sort for the trade window's
/// stock (the matches packed at the grid's start, the rest left out; the items moved by <see cref="StockOrderSystem"/>), a counter, the
/// trader's money, the deal balance and the player's money in the header (<see cref="TraderUi"/>).
/// </summary>
public sealed class OrganizeTraderMod : IMod
{
    public void Initialize(ModContext context)
    {
        // The settings, the captions and the presets in the game's language, from the installed fork's catalogues.
        if (OriginalFolder.Find(context) is { } original) context.AddTranslations("organize_the_trader", original);

        var settings = new TraderSettings();
        context.AddSettings(settings);
        var state = new TraderState(context, settings);
        context.AddFrame(state.Frame);
        context.AddSystem("organize-stock", _ => new StockOrderSystem(), SystemAnchor.Before(StandardParts.Trade));
        Patches.Install(context, TraderUi.ExtensionId);
        context.AddUi(new TraderUi(context, settings, state, new StockView(context.Data)));
        context.Log("Organize the Trader initialized");
    }
}
