using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Items;
using Meitou.Simulation.Population;
using Meitou.Simulation.Trading;

namespace Meitou.BetterLooting;

/// <summary>
/// A loot key press: the character whose inventory window was open, Alt (store instead of loot) and the "Always take these items" list read
/// from <c>BetterLooting.ini</c> at the press.
/// </summary>
public sealed record LootRequest(CharacterId Lead, bool Deposit, RequiredItems Required);

/// <summary>An item that passed the rules: where it is, its value per unit and weight per unit.</summary>
readonly record struct LootCandidate(ItemInstance Item, Inventory Owner, int Value, float Weight);

/// <summary>
/// Runs a <see cref="LootRequest"/> in the player system's input step (so also while paused): loots the bodies (and, unprotected, the player's
/// storage) round the lead into it, or stores its items in the player's storage, then posts how many items moved.
/// </summary>
sealed partial class LootSystem(PopulationData data) : ITickSystem
{
    readonly EquipmentRefresh? _refresh = EquipmentRefresh.Create(data);

    static LootSettings Settings => BetterLootingMod.Settings;

    /// <summary>
    /// The list of the request being run.
    /// </summary>
    RequiredItems _required = RequiredItems.None;

    public void Run(World world, LootRequest request)
    {
        var table = world.Characters;
        if (!Settings.Enabled || !table.TryResolveNext(request.Lead, out int lead)) return;
        if (table.Cold(lead) is not { IsPlayer: true, Inventory: not null, Medical: not { Unconscious: true } and not { Dead: true } } cold) return;

        _required = request.Required;
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
        return LootRules.Decide(Settings, LootRules.CategoryOf(item, data.Db), stolen, value, item.UnitWeight, required) == LootDecision.Loot;
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
