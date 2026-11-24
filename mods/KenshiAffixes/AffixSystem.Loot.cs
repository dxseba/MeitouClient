using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Simulation;
using Meitou.Simulation.Items;

namespace Meitou.KenshiAffixes;

/// <summary>
/// Container loot as the original rolls it (<b>Verified</b>, its <c>KenshiAffixes.cpp</c> <c>siteStrength</c> / <c>templateThreat</c> /
/// <c>lootNativeLevel</c> and <c>AffixRules.h</c> <c>lootQualityLevel</c>): the site's strength, from <c>[LootSiteLevels]</c> or the strongest
/// combat field in its TOWN template's graph, sets the loot level and may lift a new item's grade (<c>ImproveNewLootQuality</c>).
/// </summary>
sealed partial class AffixSystem
{
    /// <summary>The original's site strength when no garrison data is found.</summary>
    const int DefaultSiteStrength = 10;

    /// <summary>The original's walk limits: six links deep, 192 records.</summary>
    const int MostDepth = 6, MostVisited = 192;

    static readonly string[] ThreatFields = ["combat stats", "unarmed stats", "ranged stats", "melee attack", "melee defence"];
    static readonly string[] ThreatLinks = ["residents", "leader", "squad", "characters", "members", "housemates", "stats"];

    /// <summary>Each town template's strength (static data, so the same in every run).</summary>
    readonly Dictionary<string, int> _strengths = new(StringComparer.Ordinal);

    /// <summary>
    /// The strength of town site <paramref name="town"/> (the level's town index): its <c>[LootSiteLevels]</c> entry (1 to 100), else the
    /// strongest of its template's graph, else 10. The building's own resident squad, which the original reads first, is not known here.
    /// </summary>
    int SiteStrength(World world, int town)
    {
        if (town < 0 || town >= data.Sites.Count) return DefaultSiteStrength;

        var site = data.Sites[town];
        string id = site.Replacement?.Id ?? site.Record.Id;
        if (profiles.Ini.SiteLevel(id) is { } configured && configured >= 0) return Math.Clamp(configured, 1, 100);

        if (!_strengths.TryGetValue(id, out int strength))
            _strengths[id] = strength = Threat(data.Db, data.Db.Find(id), new HashSet<string>(StringComparer.Ordinal), 0);

        return strength > 0 ? strength : DefaultSiteStrength;
    }

    /// <summary>The original's <c>templateThreat</c>: the highest combat field (0 to 100) of the record and the records it links, depth first.</summary>
    static int Threat(GameDatabase db, GameRecord? record, HashSet<string> visited, int depth)
    {
        if (record is null || depth > MostDepth || visited.Count >= MostVisited || !visited.Add(record.StringId)) return 0;

        int strength = ThreatFields.Max(field => Math.Clamp(record.GetInt(field), 0, 100));
        foreach (string link in ThreatLinks)
        {
            foreach (var reference in record.GetReferences(link))
                strength = Math.Max(strength, Threat(db, db.Find(reference.TargetStringId), visited, depth + 1));
        }

        return strength;
    }

    /// <summary>The original's loot level: max(quality level, round(0.7 x site strength + 0.3 x quality level)).</summary>
    static float LootLevel(int quality, int strength) =>
        Math.Max(quality, Math.Clamp((7 * Math.Clamp(strength, 1, 100) + 3 * quality + 5) / 10, 1, 100));

    /// <summary>
    /// <c>ImproveNewLootQuality</c>: a new weapon or armour piece of a container's fill, with a roll 0-99 under strength - 20 (0 to 80), is lifted
    /// to at least 20, 40, 60 or 80 for a site of 25, 45, 65 or 85 and up; a higher grade stays.
    /// </summary>
    void ImproveLoot(World world, ItemInstance item, int strength, ulong key)
    {
        if (!settings.ImproveLootQuality || item.Type is not (FcsRecordType.Weapon or FcsRecordType.Armour) || item.Level is < 0 or > 100) return;

        strength = Math.Clamp(strength, 1, 100);
        int roll = new RollStream(world.Seed, key ^ 0x4C4F4F54UL << 32, purpose).Between(0, 99);
        if (roll >= Math.Clamp(strength - 20, 0, 80)) return;

        int floor = strength >= 85 ? 80 : strength >= 65 ? 60 : strength >= 45 ? 40 : strength >= 25 ? 20 : 0;
        if (floor > item.Level)
        {
            log($"LOOT_QUALITY from={item.Level} to={floor} siteLevel={strength}");
            item.Level = floor;
        }
    }
}
