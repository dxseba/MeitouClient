using System.Collections.Frozen;
using Meitou.Data.Gameplay;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;

namespace Meitou.VitalSense;

public enum Relation : byte { Neutral, Ally, Enemy, Squad }

/// <summary>
/// Publishes, every <see cref="VitalSettings.UpdateMs"/> of game time (the original's <c>update_interval_ms</c>, 150: every 5th tick), the state
/// and total bounty of every character that is down, dead or wanted, and each faction's standing with the player's. Read in the serial slow phase, shown by the interface; presentation only, neither saved nor hashed.
/// </summary>
sealed class VitalSystem(FactionRelations relations, GameConstants? constants, Action<string> log) : ITickSystem
{
    string _lastDiagnostics = "";

    volatile FrozenDictionary<CharacterId, Vital> _vitals = FrozenDictionary<CharacterId, Vital>.Empty;
    volatile FrozenDictionary<int, Relation> _factions = FrozenDictionary<int, Relation>.Empty;

    /// <summary>The characters with a state or a bounty; safe to read from the interface thread.</summary>
    public FrozenDictionary<CharacterId, Vital> Vitals => _vitals;

    /// <summary>Each faction's standing with the player's faction (absent: neutral).</summary>
    public FrozenDictionary<int, Relation> Factions => _factions;

    public void SlowWorld(World world)
    {
        var settings = VitalSenseMod.Settings;
        long every = Math.Max(1, (long)MathF.Round(settings.UpdateMs / 1000f / world.TickSeconds));
        if (world.Tick % every != 0 || !settings.Enabled) return;

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
        if (settings.DebugDiagnostics) Diagnose(vitals.Values);
    }

    /// <summary>
    /// <c>debug_log_diagnostics</c>: the update's counts per state and of wanted characters, logged when they change.
    /// </summary>
    void Diagnose(IEnumerable<Vital> vitals)
    {
        var counts = vitals.GroupBy(v => v.State).OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}");
        string line = $"targets: {string.Join(", ", counts)}; wanted={vitals.Count(v => v.Bounty > 0)}";
        if (line == _lastDiagnostics) return;

        _lastDiagnostics = line;
        log(line);
    }

    Vital? Of(CharacterCold cold)
    {
        int bounty = Math.Max(cold.Bounties?.Total ?? 0, 0);
        var state = cold.Medical is { } med ? VitalRule.Of(med, KoPoint(cold)) : VitalState.None;
        return state != VitalState.None || bounty > 0 ? new Vital(state, bounty, cold.Medical?.Dead ?? false) : null;
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
