using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Crime;
using Meitou.Simulation.Items;

namespace Meitou.BetterLooting;

/// <summary>A place to loot: its inventory, the body's slot (-1 for storage) and its distance from the looter.</summary>
readonly record struct LootSource(Inventory Inventory, int Body, float Distance);

/// <summary>The looting key: the bodies round the lead, nearest first, then the ground.</summary>
sealed partial class LootSystem
{
    void Collect(World world, int lead, CharacterCold looter)
    {
        var sources = Sources(world, lead);
        int units = 0, places = 0;
        foreach (var source in sources)
        {
            int moved = LootFrom(world, lead, looter, source);
            units += moved;
            if (moved > 0) places++;
        }

        int queued = Settings.GroundPickup ? QueueGround(world, lead, looter) : 0;
        world.Post(Report(looter.Name, units, places, queued));
    }

    static string Report(string name, int units, int places, int queued)
    {
        string taken = units > 0 ? $"{name} looted {Plural(units, "item")} from {Plural(places, "source")}" : $"{name} found nothing worth looting";
        return queued > 0 ? $"{taken}; {Plural(queued, "ground item")} queued to pick up." : taken + ".";
    }

    /// <summary>
    /// Downed or dead non-player characters with items that no player character carries, and with the protection off the player's storage,
    /// within the radius.
    /// </summary>
    List<LootSource> Sources(World world, int lead)
    {
        var table = world.Characters;
        var at = table.Next[lead].Position;
        var list = new List<LootSource>();
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (slot == lead || !table.Next[slot].Alive) continue;
            if (table.Cold(slot) is not { IsPlayer: false, Inventory: { Items.Count: > 0 } inventory, Medical: { } med } cold) continue;
            if (!med.Dead && !med.Unconscious || CarriedByPlayer(table, cold)) continue;

            float distance = Vector3.Distance(table.Next[slot].Position, at);
            if (distance <= Settings.RadiusUnits) list.Add(new(inventory, slot, distance));
        }

        if (!Settings.ProtectPlayerStorage) AddStorage(world, at, list);
        list.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        return list;
    }

    /// <summary>
    /// The fork's carried protection: a body (dead or alive) that one of the player's characters carries is never looted.
    /// </summary>
    static bool CarriedByPlayer(CharacterTable table, CharacterCold body) =>
        !body.CarriedBy.IsNone && table.TryResolveNext(body.CarriedBy, out int carrier) && table.Cold(carrier) is { IsPlayer: true };

    static void AddStorage(World world, Vector3 at, List<LootSource> list)
    {
        foreach (var machine in world.Machines.All)
        {
            if (PlayerStorage(world, machine, at) is { } distance) list.Add(new(machine.Storage!, -1, distance));
        }

        foreach (var fixture in world.Fixtures.All)
        {
            if (fixture.Contents is not { Items.Count: > 0 } contents || fixture.Owner != world.Player.Faction) continue;

            float distance = Vector3.Distance(fixture.Position, at);
            if (distance <= Settings.RadiusUnits) list.Add(new(contents, -1, distance));
        }
    }

    /// <summary>Takes what passes the rules from one source; the units moved.</summary>
    int LootFrom(World world, int lead, CharacterCold looter, LootSource source)
    {
        var table = world.Characters;
        var victim = source.Body >= 0 ? table.Cold(source.Body) : null;
        bool theft = victim is not null && CrimeSystem.IsLootTheft(world, looter, victim);
        bool marks = victim is not null && CrimeSystem.MarksTheft(world, looter, victim);
        StolenMark? mark = marks ? StolenMark.Of(victim!, table.IdOf(source.Body)) : null;

        int units = 0;
        bool worn = false;
        foreach (var candidate in Ordered(Candidates(world, looter, source.Inventory, theft, victim is not null)))
        {
            bool equipment = InventoryCommands.IsEquipmentSection(candidate.Item.Section);
            int moved = TakeOne(world, looter, candidate, mark);
            units += moved;
            worn |= moved > 0 && equipment;
        }

        if (worn && victim is not null) CharacterRefresh.Refresh(data, victim, CharacterRefreshKind.Equipment);
        if (units > 0 && theft) world.Systems.OfType<CrimeSystem>().FirstOrDefault()?.Loot(world, lead, source.Body);
        return units;
    }

    /// <summary>
    /// The loose items of an inventory and its worn bag, a body's worn gear too (never a locked-on piece, a fitted limb, a bag, or a death
    /// item, which butchering takes), judged by the rules. <paramref name="theft"/>: taking from this body is a crime.
    /// </summary>
    List<LootCandidate> Candidates(World world, CharacterCold looter, Inventory inventory, bool theft, bool body)
    {
        var list = new List<LootCandidate>();
        var bag = inventory.Backpack?.Contents;
        var items = inventory.Items.Select(i => (Item: i, Owner: inventory)).Concat(bag?.Items.Select(i => (Item: i, Owner: bag)) ?? []);
        foreach (var (item, owner) in items.ToList())
        {
            if (!Lootable(item, body)) continue;

            bool stolen = theft || item.IsStolenFor(looter.Faction, data);
            if (Judge(world, item, owner, stolen) is { } candidate) list.Add(candidate);
        }

        return list;
    }

    static bool Lootable(ItemInstance item, bool body)
    {
        if (item.LockedOn || item.Death || item.Contents is not null || InventoryCommands.IsLimbSection(item.Section)) return false;

        return item.Section is "main" or "backpack_content" || body && InventoryCommands.IsEquipmentSection(item.Section);
    }

    /// <summary>One candidate into the looter, marked stolen from the body when the take marks theft; money into the purse.</summary>
    int TakeOne(World world, CharacterCold looter, LootCandidate candidate, StolenMark? mark)
    {
        var item = candidate.Item;
        if (item.ItemFunction == LootTransfer.MoneyFunction && looter.Faction == world.Player.Faction)
            return LootTransfer.Cash(world, data, candidate.Owner, item) ? 1 : 0;   // a stack of money counts as one item

        var before = item.Mark;
        if (mark is { } arriving && before == StolenMark.None) item.Mark = arriving;

        int moved = LootTransfer.Take(candidate.Owner, item, looter, Settings.PreferBackpack);
        if (candidate.Owner.Items.Contains(item)) item.Mark = before;
        return moved;
    }
}
