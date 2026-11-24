using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Items;
using Meitou.Simulation.Population;
using Meitou.Simulation.Trading;

namespace Meitou.BetterLooting;

/// <summary>
/// A loot key press: the character whose inventory window was open, Alt (store instead of loot) and the "Always take these items" list as
/// set at the press. A paused command: while paused it applies between ticks.
/// </summary>
public sealed record LootRequest(CharacterId Lead, bool Deposit, RequiredItems Required) : SimCommand, IPausedCommand;

/// <summary>An item that passed the rules: where it is, its value per unit and weight per unit.</summary>
readonly record struct LootCandidate(ItemInstance Item, Inventory Owner, int Value, float Weight);

/// <summary>
/// Runs each <see cref="LootRequest"/> in its input step, and while paused in the paused interface step: loots the bodies (and, unprotected, the player's
/// storage) round the lead into it, or stores its items in the player's storage, then posts how many items moved.
/// </summary>
sealed partial class LootSystem(PopulationData data) : IPausedCommandHandler
{
    static LootSettings Settings => BetterLootingMod.Settings;

    /// <summary>
    /// The list of the request being run.
    /// </summary>
    RequiredItems _required = RequiredItems.None;

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands)
        {
            if (command is LootRequest request) Run(world, request);
        }
    }

    void Run(World world, LootRequest request)
    {
        var table = world.Characters;
        if (!Settings.Enabled || !table.TryResolveNext(request.Lead, out int lead)) return;
        if (table.Cold(lead) is not { IsPlayer: true, Inventory: not null, Medical: not { Unconscious: true } and not { Dead: true } } cold) return;

        _required = request.Required;
        TraceRules();
        if (request.Deposit) Deposit(world, lead, cold);
        else Collect(world, lead, cold);
    }

    /// <summary>The rules' verdict on one item; null when it is skipped.</summary>
    LootCandidate? Judge(World world, ItemInstance item, Inventory owner, bool stolen)
    {
        int value = ValueOf(world, item);
        return Passes(item, stolen, value) ? new(item, owner, value, item.UnitWeight) : null;
    }

    bool Passes(World world, ItemInstance item, bool stolen) => Passes(item, stolen, ValueOf(world, item));

    bool Passes(ItemInstance item, bool stolen, int value)
    {
        bool required = _required.Matches(item, data.Db);
        var category = LootRules.CategoryOf(item, data.Db);
        var decision = LootRules.Decide(Settings, category, stolen, value, item.UnitWeight, required);
        BetterLootingMod.Trace(() => $"PLAN item={item.Record} category={category} value={value} weight={item.UnitWeight:0.###} stolen={stolen} " +
            $"required={required} decision={Decision(decision)}");
        return decision == LootDecision.Loot;
    }

    static string Decision(LootDecision decision) => decision switch
    {
        LootDecision.SkipCategory => "SKIP_CATEGORY",
        LootDecision.SkipStolen => "SKIP_STOLEN",
        LootDecision.SkipMinValue => "SKIP_MIN_VALUE",
        LootDecision.SkipValuePerKg => "SKIP_VALUE_PER_KG",
        _ => "LOOT",
    };

    /// <summary>The original's CONFIG and AUDIT_RULES lines for a press.</summary>
    void TraceRules()
    {
        BetterLootingMod.Trace(() => $"CONFIG minValue={Settings.MinValue} minValuePerKg={Settings.MinValuePerKg:0.00} " +
            $"allowStolen={Settings.AllowStolen} requiredList={!_required.IsEmpty}");
        BetterLootingMod.Trace(() => $"AUDIT_RULES radius={Settings.LootRadius:0.00} categories=" +
            string.Join(",", Enum.GetValues<LootCategory>().Where(Settings.Allows)) + $" bestItemsFirst={Settings.BestItemsFirst} " +
            $"preferBackpack={Settings.PreferBackpack} protectStorage={Settings.ProtectPlayerStorage} ground={Settings.GroundPickup}");
    }

    /// <summary>The item's base value per unit at its level (<c>Market.BaseValue</c>, the inventory tooltip's Value), 0 without a market.</summary>
    static int ValueOf(World world, ItemInstance item) => world.Systems.OfType<TradeSystem>().FirstOrDefault()?.Market(world).BaseValue(item) ?? 0;

    /// <summary>The candidates in the Best items first order (value per kilogram, then value, highest first), else as found.</summary>
    static List<LootCandidate> Ordered(List<LootCandidate> candidates)
    {
        if (Settings.BestItemsFirst)
        {
            candidates.Sort((a, b) =>
            {
                int byRank = LootRules.Rank(b.Value, b.Weight).CompareTo(LootRules.Rank(a.Value, a.Weight));
                return byRank != 0 ? byRank : b.Value.CompareTo(a.Value);
            });
        }

        return candidates;
    }

    /// <summary>A storage building of the player's faction, complete, within the radius of <paramref name="at"/>; its distance.</summary>
    static float? PlayerStorage(World world, Meitou.Simulation.Production.Machine machine, Vector3 at)
    {
        if (!machine.Data.IsStorage || machine.Storage is null || machine.Closed) return null;
        if (world.Buildings.Find(machine.Site) is not { Complete: true } site || site.Owner != world.Player.Faction) return null;

        float distance = Vector3.Distance(machine.Position, at);
        return distance <= Settings.RadiusUnits ? distance : null;
    }

    static string Plural(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";
}
