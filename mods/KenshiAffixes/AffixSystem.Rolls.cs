using Meitou.Data.Fcs;
using Meitou.Simulation;
using Meitou.Simulation.Items;

namespace Meitou.KenshiAffixes;

/// <summary>
/// The sweep (every 60th tick): a fixed NPC's equipped gear by its profile (also after a load, as the original's), shop stock not yet rolled (stock
/// from before the mod ran), a Meitou weapon anywhere by its template. The player's own plain gear is marked plain and never rolled (the original
/// rolls none for it, not even once sold or stored). Ordinary NPC gear rolls once as the NPC is made (<see cref="Created"/>).
/// </summary>
sealed partial class AffixSystem
{
    void RollAll(World world)
    {
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { Inventory: { } inventory } cold) continue;

            if (cold.IsPlayer) PlayerGear(world, inventory);
            else if (settings.AutoNpc && cold.Stats is { } stats) FixedNpcGear(world, cold, inventory, Rolling.NpcLevel(stats));
        }

        if (settings.AutoShops) RollShops(world);
    }

    /// <summary>The player's characters: Meitou weapons take their profile; other plain gear is marked plain.</summary>
    void PlayerGear(World world, Inventory inventory)
    {
        for (int i = 0; i < inventory.Items.Count; i++)
        {
            var item = inventory.Items[i];
            if (!Wearing.Eligible(item) || Meitou(world, item)) continue;
            if (!AffixTag.Rolled(item.ModTag) && !AffixTag.Plain(item.ModTag)) AffixTag.MarkPlain(item);
        }
    }

    /// <summary>An NPC's equipped gear: Meitou first, then a fixed NPC's profile (replacing an ordinary roll).</summary>
    void FixedNpcGear(World world, Simulation.CharacterCold cold, Inventory inventory, float level)
    {
        for (int i = 0; i < inventory.Items.Count; i++)
        {
            var item = inventory.Items[i];
            if (!Wearing.Eligible(item) || !Wearing.Equipped(item) || Meitou(world, item) || AffixTag.Plain(item.ModTag)) continue;
            if (Rolling.BaselineOf(item, combat!) is not { } baseline) continue;

            if (profiles.Npc(cold.RecordId, baseline.Armour) is not { } fixedNpc || AffixTag.Profile(item.ModTag) is not null) continue;

            var (ilvl, affixes) = Rolling.Fixed(baseline, Rolling.Band(item), level, fixedNpc.Priorities);
            AffixTag.Write(item, ilvl, affixes, fixedNpc.Name);
        }
    }

    /// <summary>The trader squads' stock (a building shop's in its stock containers) at 12 + 16 x the item's quality band.</summary>
    void RollShops(World world)
    {
        foreach (var squad in world.Squads.All)
        {
            if (squad.Shop is not { } shop || squad.Faction == world.Player.Faction) continue;

            ulong key = (ulong)(uint)squad.Id << 32;
            if (shop.Building is null)
            {
                Stock(world, shop.Stock, key);
                continue;
            }

            for (int c = 0; c < shop.Containers.Count; c++)
                Stock(world, world.Fixtures.Find(shop.Containers[c])?.Contents, key ^ (ulong)(c + 1) << 20);
        }
    }

    void Stock(World world, Inventory? stock, ulong key)
    {
        if (stock is null) return;

        for (int i = 0; i < stock.Items.Count; i++)
        {
            var item = stock.Items[i];
            if (!Wearing.Eligible(item) || Meitou(world, item) || AffixTag.Rolled(item.ModTag) || AffixTag.Plain(item.ModTag)) continue;
            if (Rolling.BaselineOf(item, combat!) is not { } baseline) continue;

            int level = Rolling.ShopLevel(Rolling.Band(item), settings.ShopBaseLevel, settings.ShopLevelPerBand);
            Roll(world, item, baseline, level, key ^ (ulong)i ^ StableHash(item.Record) << 8);
        }
    }

    void Roll(World world, ItemInstance item, Baseline baseline, float level, ulong entity)
    {
        var rng = new RollStream(world.Seed, entity ^ (ulong)world.Tick << 16, purpose);
        var (ilvl, affixes) = Rolling.Generate(baseline, Rolling.Band(item), level, rng);
        AffixTag.Write(item, ilvl, affixes);
    }

    /// <summary>
    /// A Meitou weapon (its material is the Meitou model) whose template the original lists gets that level-100 profile, replacing any other roll;
    /// true when the item has one.
    /// </summary>
    bool Meitou(World world, ItemInstance item)
    {
        if (item.Type != FcsRecordType.Weapon || item.MaterialId != Profiles.MeitouMaterial || profiles.Meitou(item.Record) is not { } priorities)
            return false;
        if (AffixTag.Profile(item.ModTag) == Profiles.MeitouProfile || Rolling.BaselineOf(item, combat!) is not { } baseline) return true;

        var (ilvl, affixes) = Rolling.Fixed(baseline, 5, 100, priorities);
        AffixTag.Write(item, ilvl, affixes, Profiles.MeitouProfile);
        return true;
    }

    /// <summary>FNV-1a over the id's characters: a hash that is the same in every run (string hash codes are not).</summary>
    static ulong StableHash(string text)
    {
        ulong h = 14695981039346656037UL;
        foreach (char c in text)
        {
            h ^= c;
            h *= 1099511628211UL;
        }

        return h;
    }
}
