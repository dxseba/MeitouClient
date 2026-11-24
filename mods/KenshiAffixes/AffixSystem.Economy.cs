using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Items;
using Meitou.Simulation.Trading;

namespace Meitou.KenshiAffixes;

/// <summary>
/// Rolls as the world makes items (<see cref="IItemCreatedObserver"/>): a fresh NPC's equipped kit at its level, a shop's new stock at its band's
/// level, new container loot at its band's level (the site strength the original reads from garrison templates is not known here: its default),
/// and the player's crafted gear from the crafter's smithing skill. And the original's value premium on affixed gear
/// (<see cref="ITradePriceFilter"/>).
/// </summary>
sealed partial class AffixSystem : IItemCreatedObserver, ITradePriceFilter
{
    /// <summary>The original's site strength when no garrison data is found.</summary>
    const int DefaultSiteStrength = 10;

    /// <summary>The rolls made so far this tick, keying each roll apart (the creation order is the world's own, so the same in every run).</summary>
    long _rollTick = -1;
    ulong _rollsThisTick;

    public void Created(World world, in ItemCreated created)
    {
        var item = created.Item;
        if (!settings.Enabled || combat is null || !Rollable(item) || AffixTag.Rolled(item.ModTag) || AffixTag.Plain(item.ModTag)) return;
        if (Meitou(world, item) || Rolling.BaselineOf(item, combat) is not { } baseline) return;

        if (SourceLevel(world, created, item) is { } level) Roll(world, item, baseline, level, CreationKey(world, created));
    }

    /// <summary>The roll's source level by where the item came from; null for none.</summary>
    float? SourceLevel(World world, in ItemCreated created, ItemInstance item)
    {
        int band = Rolling.Band(item), quality = Rolling.ShopLevel(band, settings.ShopBaseLevel, settings.ShopLevelPerBand);
        return created.Kind switch
        {
            ItemCreatedKind.SpawnEquipment => NpcKitLevel(world, created.Character, item),
            ItemCreatedKind.ShopStock when settings.AutoShops && world.Squads.Find(created.Squad)?.Faction != world.Player.Faction => quality,
            ItemCreatedKind.Loot when settings.AutoContainers => LootLevel(quality),
            ItemCreatedKind.Crafted when settings.AutoCrafting => CraftLevel(world, created.Character, item, quality),
            _ => null,
        };
    }

    /// <summary>The original's loot level: max(quality level, round(0.7 x site strength + 0.3 x quality level)), the strength its default.</summary>
    static float LootLevel(int quality) => Math.Max(quality, MathF.Floor(0.7f * DefaultSiteStrength + 0.3f * quality + 0.5f));

    /// <summary>A fresh NPC's equipped gear at its level; a fixed NPC's is left to its profile (the sweep), the player's gear stays plain.</summary>
    float? NpcKitLevel(World world, CharacterId id, ItemInstance item)
    {
        if (!settings.AutoNpc || !Wearing.Equipped(item) || !world.Characters.TryResolveNext(id, out int slot)) return null;
        if (world.Characters.Cold(slot) is not { IsPlayer: false, Stats: { } stats } cold) return null;

        return profiles.Npc(cold.RecordId, item.Type == Data.Fcs.FcsRecordType.Armour) is null ? Rolling.NpcLevel(stats) : null;
    }

    /// <summary>
    /// The original's crafting level: 0.7 x the player crafter's trained smithing skill (weapon or armour) + 0.3 x the quality's level.
    /// </summary>
    static float? CraftLevel(World world, CharacterId smith, ItemInstance item, int quality)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(smith, out int slot) || table.Cold(slot) is not { IsPlayer: true, Stats: { } stats }) return null;

        var skill = item.Type == Data.Fcs.FcsRecordType.Armour ? StatsEnumerated.SmithingArmour : StatsEnumerated.SmithingWeapon;
        return 0.7f * stats.Stored(skill) + 0.3f * quality;
    }

    ulong CreationKey(World world, in ItemCreated created)
    {
        if (_rollTick != world.Tick)
        {
            _rollTick = world.Tick;
            _rollsThisTick = 0;
        }

        ulong who = created.Character.IsNone ? (ulong)(uint)created.Squad << 32 : Rng.Key(created.Character);
        return who ^ StableHash(created.Item.Record) << 8 ^ (ulong)created.Kind << 60 ^ ++_rollsThisTick << 44;
    }

    /// <summary>The original's premium: x (1 + n x (base% + level% x iLvl / 100) + rares x (20% + 40% x iLvl / 100)), rounded.</summary>
    public long Price(World world, in TradeDeal deal, long price)
    {
        if (!settings.Enabled || !settings.AffixPrices || price <= 0 || deal.Item is not { } item) return price;

        double multiplier = ValueMultiplier(item.ModTag, settings.ValueBasePercent, settings.ValueLevelPercent);
        return multiplier == 1 ? price : (long)Math.Min(long.MaxValue / 2, Math.Floor(price * multiplier + 0.5));
    }

    /// <summary>The value multiplier of a tag's affixes; 1 for none.</summary>
    public static double ValueMultiplier(string tag, int basePercent, int levelPercent)
    {
        var affixes = AffixTag.Read(tag);
        if (affixes.Count == 0) return 1;

        double level = Math.Clamp(AffixTag.Level(tag) ?? 1, 1, 100);
        int rares = affixes.Count(a => a.Kind.Op == AffixOp.Combat), count = Math.Min(affixes.Count, 3);
        double perAffix = Math.Clamp(basePercent, 0, 100) + Math.Clamp(levelPercent, 0, 100) * level / 100;
        return 1 + rares * (20 + 40 * level / 100) / 100 + count * perAffix / 100;
    }
}
