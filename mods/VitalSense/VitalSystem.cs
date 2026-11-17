using System.Collections.Frozen;
using Meitou.Data.Gameplay;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;

namespace Meitou.VitalSense;

public enum Relation : byte { Neutral, Ally, Enemy, Squad }

/// <summary>
/// Publishes, every 5th tick (the original's 150 ms refresh), the state and total bounty of every character that is down, dead or wanted, and each
/// faction's standing with the player's. Read in the serial slow phase, shown by the interface; presentation only, neither saved nor hashed.
/// </summary>
sealed class VitalSystem(FactionRelations relations, GameConstants? constants) : ITickSystem
{
    volatile FrozenDictionary<CharacterId, Vital> _vitals = FrozenDictionary<CharacterId, Vital>.Empty;
    volatile FrozenDictionary<int, Relation> _factions = FrozenDictionary<int, Relation>.Empty;

    /// <summary>The characters with a state or a bounty; safe to read from the interface thread.</summary>
    public FrozenDictionary<CharacterId, Vital> Vitals => _vitals;

    /// <summary>Each faction's standing with the player's faction (absent: neutral).</summary>
    public FrozenDictionary<int, Relation> Factions => _factions;

    public void SlowWorld(World world)
    {
        if (world.Tick % 5 != 0 || !VitalSenseMod.Settings.Enabled) return;

        var vitals = new Dictionary<CharacterId, Vital>();
        var factions = new Dictionary<int, Relation>();
        var table = world.Characters;

        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { } cold) continue;

            if (Of(cold) is { } vital) vitals[table.IdOf(slot)] = vital;
            AddFaction(world, cold.Faction, factions);
        }

        _vitals = vitals.ToFrozenDictionary();
        _factions = factions.ToFrozenDictionary();
    }

    Vital? Of(CharacterCold cold)
    {
        int bounty = Math.Max(cold.Bounties?.Total ?? 0, 0);
        var state = cold.Medical is { } med ? VitalRule.Of(med, KoPoint(cold)) : VitalState.None;
        return state != VitalState.None || bounty > 0 ? new Vital(state, bounty) : null;
    }

    float? KoPoint(CharacterCold cold) =>
        constants is not null && cold.Stats is { } stats ? MedicalState.KoPoint(constants, stats.ScaledToughness) : null;

    void AddFaction(World world, int faction, Dictionary<int, Relation> map)
    {
        if (faction < 0 || map.ContainsKey(faction) || !world.Player.Exists) return;

        int mine = world.Player.Faction;
        map[faction] = faction == mine ? Relation.Squad
            : relations.IsAlly(mine, faction) ? Relation.Ally
            : relations.IsHostile(mine, faction) ? Relation.Enemy
            : Relation.Neutral;
    }
}
