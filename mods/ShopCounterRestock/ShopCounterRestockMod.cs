using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Ai;
using Meitou.Data.Gameplay.Construction;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Production;

namespace Meitou.ShopCounterRestock;

/// <summary>
/// Port of ShopCounterRestock 0.1.7-r10 (workshop 3826183068): a worker assigned to a player shop counter keeps the counter's whitelisted items at
/// their target quantities, delivering what it carries and fetching more from the player's storage in the same town or outpost. The assignment is
/// a mod job (<see cref="JobType"/>) in the worker's job list; the counter's whitelist, targets and switch are saved with the game.
/// </summary>
public sealed class ShopCounterRestockMod : IMod
{
    /// <summary>
    /// The mod job's task type (a mod code: <c>SimulationExtensions.FirstModCode</c> and up).
    /// </summary>
    internal const TaskType JobType = (TaskType)4311;

    /// <summary>
    /// The BuildingFunction of a shop counter (BF_SHOP).
    /// </summary>
    internal const int ShopFunction = 9;

    /// <summary>
    /// The running world's system, for the window; null before a world starts.
    /// </summary>
    internal static volatile RestockSystem? Running;

    public void Initialize(ModContext context)
    {
        var counters = CounterRecords(context.Data);
        var catalog = new ItemCatalog(context.Data);
        context.AddJobType(JobType, "Shop counter restock");
        if (counters.Count > 0) context.AddJobOffer(new ModJobOffer(JobType, counters, "Restock...", ModJobTakers.People));

        context.AddSystem("restock", _ => Running = new RestockSystem(catalog) { Log = context.Log }, SystemAnchor.End);
        context.AddUi(new RestockWindow(context, catalog, counters));
        MenuPatch.Install(context);
        context.Log($"ShopCounterRestock initialized: {counters.Count} shop counter records, {catalog.Count} items");
    }

    /// <summary>
    /// The BUILDING records whose functionality is a shop counter (function 9).
    /// </summary>
    static List<string> CounterRecords(Data.GameDatabase data) =>
        [.. data.OfType(FcsRecordType.Building).Where(r => MachineData.From(data, r) is { Function: ShopFunction }).Select(r => r.StringId)
            .Order(StringComparer.Ordinal)];
}

/// <summary>
/// Gives <paramref name="Worker"/> the restock job at counter <paramref name="Counter"/> (the settings window's worker choice), or takes the
/// counter's worker off it when <paramref name="Worker"/> is none.
/// </summary>
public sealed record AssignWorker(string Counter, CharacterId Worker) : SimCommand;

/// <summary>
/// Adds <paramref name="Item"/> to the counter's whitelist at <paramref name="Target"/> units, or changes its target.
/// </summary>
public sealed record SetTarget(string Counter, string Item, int Target) : SimCommand;

/// <summary>
/// Takes <paramref name="Item"/> off the counter's whitelist; goods the worker carries stay with it.
/// </summary>
public sealed record RemoveItem(string Counter, string Item) : SimCommand;

/// <summary>
/// Turns restocking at the counter on or off.
/// </summary>
public sealed record SetEnabled(string Counter, bool On) : SimCommand;
