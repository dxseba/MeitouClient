using System.Numerics;
using Meitou.Simulation;

namespace Meitou.ForceAwakening;

/// <summary>
/// The four masters (<b>Verified</b>, the DLL's master table and its world-loop check): Jedi Master (squad <c>5-</c>, character <c>2-</c>) and Grey
/// Master (<c>20-</c>, <c>17-</c>) at The Hub (2639, -51181), Sith Lord (<c>10-</c>, <c>7-</c>) at Mongrel (-37504, -83004), Veteran Hunter
/// (<c>15-</c>, <c>12-</c>) at Squin (15956, -61876). When a player character comes within 1500 of a master's place and no character of that record
/// lives, its squad is made there (30 off the place on each axis) for the faction <c>51707-rebirth.mod</c>, homed at the nearest town, as the
/// original does. Spawned platoons carry this mod's tag, so a master is not made twice.
/// </summary>
sealed partial class ForceSystem
{
    const float MasterReach = 1500, MasterOffset = 30;
    const string MasterFaction = "51707-rebirth.mod";

    static readonly (string Key, string Squad, string Character, Vector2 At)[] Masters =
    [
        ("jedi", "5-TheForceAwakening.mod", "2-TheForceAwakening.mod", new(2639, -51181)),
        ("sith", "10-TheForceAwakening.mod", "7-TheForceAwakening.mod", new(-37504, -83004)),
        ("cacador", "15-TheForceAwakening.mod", "12-TheForceAwakening.mod", new(15956, -61876)),
        ("cinzento", "20-TheForceAwakening.mod", "17-TheForceAwakening.mod", new(2639, -51181)),
    ];

    void SpawnMasters(World world)
    {
        if (populationSystem is null || population.FactionIndex(MasterFaction) is var faction && faction < 0) return;

        foreach (var (key, squad, character, at) in Masters)
        {
            string tag = "master-" + key;
            if (world.Platoons.FindByTag(mod.ModId, tag) is not null || Lives(world, character) || !PlayerNear(world, at)) continue;
            if (Nearest(at) is not { } site) continue;

            var place = at + new Vector2(MasterOffset, MasterOffset);
            int id = populationSystem.SpawnPlatoon(world, faction, squad, site, place, 0, MasterOffset);
            if (world.Platoons.Find(id) is { } platoon) platoon.ModTags[mod.ModId] = tag;
        }
    }

    static bool Lives(World world, string record)
    {
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (table.Next[slot].Alive && table.Cold(slot) is { Medical.Dead: false } cold && cold.RecordId == record) return true;
        }

        return false;
    }

    static bool PlayerNear(World world, Vector2 at, float reach = MasterReach)
    {
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { IsPlayer: true, Medical.Dead: false }) continue;

            var p = table.Next[slot].Position;
            if (Vector2.Distance(new Vector2(p.X, p.Z), at) < reach) return true;
        }

        return false;
    }

    int? Nearest(Vector2 at)
    {
        int? best = null;
        float bestD = float.MaxValue;
        foreach (var site in population.Sites)
        {
            float d = Vector2.Distance(new Vector2(site.Position.X, site.Position.Z), at);
            if (d < bestD) (best, bestD) = (site.Index, d);
        }

        return best;
    }
}
