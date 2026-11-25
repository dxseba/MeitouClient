using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Crime;
using Meitou.Simulation.Items;
using Meitou.Simulation.Player;
using static Meitou.Data.Gameplay.Bodies.StatsEnumerated;

namespace Meitou.DesolationSystem;

/// <summary>Phantom Pickpocket's key: the selected characters each try to lift an item from the nearest stranger.</summary>
public sealed record Pickpocket(IReadOnlyList<CharacterId> Thieves) : SimCommand;

/// <summary>
/// Phantom Pickpocket (stealth and thieving 80+, sneaking): one unit of a random item from the main inventory of the nearest other-faction person
/// within reach. The chance (from the codex): 80 %, +2.5 % per 10 stealth and per 10 thieving above the gate, -5 % per 10 of the target's
/// perception, held to 15-95 % (99 % for a Shadow Sovereign). A failure is the game's caught theft (<c>CrimeSystem.StealRoll</c> at chance 0).
/// </summary>
sealed partial class DesoSystem
{
    internal CrimeSystem? Crimes { get; init; }

    void Pick(World world, Pickpocket order)
    {
        foreach (var thief in order.Thieves) PickOne(world, thief);
    }

    void PickOne(World world, CharacterId thief)
    {
        var cfg = _cfg;
        var table = world.Characters;
        if (!cfg.P24 || !table.TryResolveNext(thief, out int slot) || table.Cold(slot) is not { IsPlayer: true, Stats: { } s, Inventory: { } bag } cold)
            return;
        if ((cold.Orders & CharacterOrders.Sneak) == 0 || Gates.L(s, Stealth) < cfg.PickpocketLevel || Gates.L(s, Thieving) < cfg.PickpocketLevel)
            return;
        if (Nearest(world, slot, cold, cfg.PickpocketReach) is not { } mark || table.Cold(mark) is not { Inventory: { } pocket } victim) return;

        var victimId = table.IdOf(mark);
        ulong seed = Rng.Hash(world.Seed, Rng.Key(thief), purpose, ((ulong)world.Tick << 8) ^ 0x50);
        if (Rng.Float(seed) >= Chance(cold, s, victim)) Caught(world, thief, victimId, victim.Faction);
        else Lift(world, seed, cold, bag, victim, pocket);
    }

    float Chance(CharacterCold thief, CharacterStats s, CharacterCold victim)
    {
        if (Gates.Reached(thief, _cfg, Gates.Realm.Shadow)) return 0.99f;

        float gate = _cfg.PickpocketLevel;
        float perception = victim.Stats is { } v ? Gates.L(v, Perception) : 0;
        float chance = 0.8f + 0.0025f * (Gates.L(s, Stealth) - gate + Gates.L(s, Thieving) - gate) - 0.005f * perception;
        return Math.Clamp(chance, 0.15f, 0.95f);
    }

    /// <summary>The nearest living, conscious-or-not person of another faction within reach (not an animal, not the player's).</summary>
    static int? Nearest(World world, int slot, CharacterCold thief, float reach)
    {
        var table = world.Characters;
        var at = table.Previous[slot].Position;
        int? best = null;
        float bestDistance = reach * reach;
        for (int k = 0; k < table.HighWater; k++)
        {
            if (k == slot || table.Cold(k) is not { IsPlayer: false, Medical.Dead: false, Inventory: not null } other) continue;
            if (other.Faction == thief.Faction || Gates.IsAnimal(other)) continue;

            float d = Vector3.DistanceSquared(at, table.Previous[k].Position);
            if (d < bestDistance) (best, bestDistance) = (k, d);
        }

        return best;
    }

    void Lift(World world, ulong seed, CharacterCold thief, Inventory bag, CharacterCold victim, Inventory pocket)
    {
        var loose = pocket.Items.Where(i => i.Section == "main" && i.Quantity > 0).ToList();
        if (loose.Count == 0)
        {
            world.Post($"{victim.Name} has nothing to take.");
            return;
        }

        var item = loose[Rng.Int(Rng.Mix(seed), loose.Count)];
        var unit = item.Copy();
        unit.Quantity = 1;
        if (!bag.Add(unit))
        {
            world.Post($"{thief.Name} has no room for it.");
            return;
        }

        pocket.RemoveOne(item);
        world.Post($"{thief.Name} lifted {unit.ShownName()} from {victim.Name}.");
    }

    void Caught(World world, CharacterId thief, CharacterId victim, int faction)
    {
        if (Crimes is { } crimes) crimes.StealRoll(world, thief, victim, 0, faction, victim);
        world.Post("Pickpocket failed.");
    }
}
