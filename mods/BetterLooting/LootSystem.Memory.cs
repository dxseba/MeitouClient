using Meitou.Simulation;
using Meitou.Simulation.Items;

namespace Meitou.BetterLooting;

/// <summary>Units of a record a press looted into a character out of the player's storage machine <paramref name="Site"/>.</summary>
sealed record Provenance(CharacterId Looter, string Record, string Site)
{
    public int Quantity { get; set; }
}

/// <summary>
/// Units of a record a press looted into a character, kept out of the game's automatic carry-off: <see cref="Protected"/> units while the
/// character holds more than <see cref="Baseline"/>.
/// </summary>
sealed record Guarded(CharacterId Looter, string Record)
{
    public int Baseline { get; set; }
    public int Protected { get; set; }
}

/// <summary>
/// What the original remembers of its presses for the session (<b>Verified</b>, decompiled DLL: its <c>ORIGIN_PROVENANCE_*</c> and
/// <c>AUTODEPOSIT_PROTECT_*</c> / <c>AUTODEPOSIT_FILTER</c> steps): where looted units came from, so a deposit puts them back there first, and
/// how many it looted, which the game's excess-loot carry-off (<see cref="IDitchItemFilter"/>) leaves with the character. Not saved, as the
/// original's.
/// </summary>
sealed partial class LootSystem : IDitchItemFilter
{
    readonly List<Provenance> _origins = [];
    readonly List<Guarded> _guards = [];

    /// <summary>The guards as the filter reads them (published in the serial steps; the filter runs on worker threads).</summary>
    volatile Guarded[] _published = [];

    /// <summary>A loot take of <paramref name="units"/> into <paramref name="looter"/> from <paramref name="site"/> (null: not a storage machine).</summary>
    void Remember(CharacterId looter, CharacterCold cold, string record, string? site, int units)
    {
        if (site is not null) RememberOrigin(looter, record, site, units);
        Protect(looter, cold, record, units);
    }

    void RememberOrigin(CharacterId looter, string record, string site, int units)
    {
        var known = _origins.FirstOrDefault(o => o.Looter == looter && o.Record == record && o.Site == site);
        if (known is null) _origins.Add(known = new(looter, record, site));
        known.Quantity += units;
        BetterLootingMod.Trace(() => $"ORIGIN_PROVENANCE record={record} origin={site} quantity={known.Quantity}");
    }

    /// <summary>The storage the first remembered take of the record came from, while units of it are left; null without one.</summary>
    string? OriginOf(CharacterId looter, string record) =>
        _origins.FirstOrDefault(o => o.Looter == looter && o.Record == record && o.Quantity > 0)?.Site;

    /// <summary>Units of a record put into storage leave its remembered takes, the first ones first.</summary>
    void ReleaseOrigin(CharacterId looter, string record, int units)
    {
        foreach (var origin in _origins.Where(o => o.Looter == looter && o.Record == record))
        {
            int released = Math.Min(units, origin.Quantity);
            origin.Quantity -= released;
            units -= released;
            if (units == 0) break;
        }

        _origins.RemoveAll(o => o.Quantity == 0);
    }

    /// <summary>
    /// The original's protect step: a new record starts at the count before the take with the take protected; a known one whose count is still
    /// above its baseline protects the take too, else starts again.
    /// </summary>
    void Protect(CharacterId looter, CharacterCold cold, string record, int units)
    {
        int count = CountOf(cold, record);
        var guard = _guards.FirstOrDefault(g => g.Looter == looter && g.Record == record);
        if (guard is null) _guards.Add(guard = new(looter, record) { Baseline = Math.Max(0, count - units), Protected = units });
        else if (guard.Baseline < count) guard.Protected += units;
        else (guard.Baseline, guard.Protected) = (Math.Max(0, count - units), units);

        BetterLootingMod.Trace(() => $"AUTODEPOSIT_PROTECT record={record} baseline={guard.Baseline} current={count} protected={guard.Protected}");
        _published = [.. _guards.Select(g => g with { })];
    }

    /// <summary>Each tick: a guard whose character holds no more than its baseline (or is gone) is cleared, the rest held to what is above it.</summary>
    void SettleGuards(World world)
    {
        if (_guards.Count == 0) return;

        bool changed = false;
        foreach (var guard in _guards)
        {
            int above = Cold(world, guard.Looter) is { } cold ? CountOf(cold, guard.Record) - guard.Baseline : 0;
            int allowed = Math.Max(0, Math.Min(guard.Protected, above));
            changed |= allowed != guard.Protected;
            guard.Protected = allowed;
        }

        changed |= _guards.RemoveAll(g => g.Protected == 0) > 0;
        if (changed) _published = [.. _guards.Select(g => g with { })];
    }

    public void SlowWorld(World world) => SettleGuards(world);

    /// <summary>
    /// The original's filter on the excess-loot carry-off: the character's stacks of a guarded record, in inventory order, are kept while
    /// protected units are left (each stack using its units, at least one).
    /// </summary>
    public bool Keeps(World world, in DitchItemAsk ask)
    {
        if (ask.Kind != DitchKind.Resources) return false;

        var item = ask.Item;
        var carrier = ask.Character;
        var guard = _published.FirstOrDefault(g => g.Looter == carrier && g.Record == item.Record);
        if (guard is null || Cold(world, carrier) is not { Inventory: { } inventory }) return false;

        int left = Math.Min(guard.Protected, CountOf(inventory, item.Record) - guard.Baseline);
        foreach (var stack in Stacks(inventory).Where(i => i.Record == item.Record))
        {
            if (left <= 0) return false;
            if (ReferenceEquals(stack, item)) return true;

            left -= Math.Max(1, stack.Quantity);
        }

        return false;
    }

    static CharacterCold? Cold(World world, CharacterId id) =>
        world.Characters.TryResolveNext(id, out int slot) ? world.Characters.Cold(slot) : null;

    static int CountOf(CharacterCold cold, string record) => cold.Inventory is { } inventory ? CountOf(inventory, record) : 0;

    static int CountOf(Inventory inventory, string record) => Stacks(inventory).Where(i => i.Record == record).Sum(i => i.Quantity);

    /// <summary>The inventory's items, then its worn bag's.</summary>
    static IEnumerable<ItemInstance> Stacks(Inventory inventory) => inventory.Items.Concat(inventory.Backpack?.Contents?.Items ?? []);
}
