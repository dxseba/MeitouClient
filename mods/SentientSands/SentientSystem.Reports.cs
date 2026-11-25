using System.Collections.Concurrent;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;

namespace Meitou.SentientSands;

/// <summary>
/// The game events the requests carry (the plugin's hooks, server <c>core/world_events.py</c>): the attacks of the player's faction, and the
/// knockouts, wake-ups, deaths and imprisonments of everyone near the player, each with the game time; and the player's renames of their
/// characters (<c>/squad_rename</c>). The serial step fills <see cref="Events"/> and <see cref="Renamed"/>; the interface sends them.
/// </summary>
sealed partial class SentientSystem : ICharacterDeathObserver, ICombatHitObserver
{
    const int MaxEvents = 500;

    /// <summary>Game events waiting for the next request.</summary>
    internal static readonly ConcurrentQueue<Dictionary<string, object?>> Events = new();

    /// <summary>The player's characters renamed since the last look (their contexts), for <c>/squad_rename</c>.</summary>
    internal static readonly ConcurrentQueue<CharacterId> Renamed = new();

    readonly Dictionary<CharacterId, (bool Down, bool Caged)> _states = [];
    readonly Dictionary<CharacterId, string> _names = [];
    readonly Dictionary<(CharacterId, CharacterId), int> _attacks = [];

    public void Died(World world, in CharacterDeath death)
    {
        var party = Party(world, death.Victim)
            ?? new() { ["id"] = ServerMessage.NpcId(death.Victim), ["name"] = "", ["template_id"] = death.Record, ["player"] = false };
        Add(world, new() { ["kind"] = "death", ["party"] = party });
    }

    /// <summary>An attack of the player's faction, once per attacker, target and game hour.</summary>
    public void HitResolved(World world, in CombatHit hit)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(hit.Attacker, out int a) || table.Cold(a) is not { IsPlayer: true } attacker) return;

        int hour = (int)world.GameHours;
        if (_attacks.TryGetValue((hit.Attacker, hit.Victim), out int last) && last == hour) return;

        _attacks[(hit.Attacker, hit.Victim)] = hour;
        if (_attacks.Count > 2000) _attacks.Clear();

        var by = new Dictionary<string, object?> { ["id"] = ServerMessage.NpcId(hit.Attacker), ["name"] = attacker.Name, ["player"] = true };
        Add(world, new() { ["kind"] = "attack", ["target"] = ServerMessage.NpcId(hit.Victim), ["attacker"] = by });
    }

    /// <summary>Knockouts, wake-ups and imprisonments of the characters near the player, by their change since the last look.</summary>
    void WatchStates(World world)
    {
        var table = world.Characters;
        foreach (var facts in Facts.Characters.Values)
        {
            if (!table.TryResolveNext(facts.Id, out int s) || table.Cold(s) is not { } c) continue;

            var now = (c.Medical is { Incapacitated: true, Dead: false }, c.IsImprisoned);
            if (_states.TryGetValue(facts.Id, out var was)) Changed(world, facts.Id, c, was, now);

            _states[facts.Id] = now;
        }

        if (_states.Count > 4000) _states.Clear();
    }

    void Changed(World world, CharacterId id, CharacterCold c, (bool Down, bool Caged) was, (bool Down, bool Caged) now)
    {
        string key = ServerMessage.NpcId(id);
        if (now.Down != was.Down)
        {
            Add(world, new() { ["kind"] = now.Down ? "knockout" : "up", ["id"] = key, ["party"] = Party(world, id),
                ["carried"] = !c.CarriedBy.IsNone });
        }

        if (now.Caged && !was.Caged) Add(world, new() { ["kind"] = "imprisonment", ["party"] = Party(world, id) });
    }

    void WatchNames(World world)
    {
        foreach (var facts in Facts.Characters.Values.Where(f => f.IsPlayer))
        {
            if (_names.TryGetValue(facts.Id, out string? was) && was != facts.Name) Renamed.Enqueue(facts.Id);

            _names[facts.Id] = facts.Name;
        }
    }

    Dictionary<string, object?>? Party(World world, CharacterId id)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(id, out int s) || table.Cold(s) is not { } c) return null;

        var relations = population.Relations;
        var faction = relations.Contains(c.Faction) ? relations.Factions[c.Faction] : null;
        return new()
        {
            ["id"] = ServerMessage.NpcId(id), ["name"] = c.Name, ["template_id"] = c.RecordId, ["player"] = c.IsPlayer,
            ["faction"] = faction?.Name ?? "", ["factionID"] = faction?.Id ?? "",
        };
    }

    static void Add(World world, Dictionary<string, object?> e)
    {
        int minutes = (int)Math.Floor(world.GameHours * 60);
        (e["day"], e["hour"], e["minute"]) = (minutes / 1440, minutes / 60 % 24, minutes % 60);
        Events.Enqueue(e);
        while (Events.Count > MaxEvents) Events.TryDequeue(out _);
    }
}
