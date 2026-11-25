using System.Numerics;
using Meitou.Data.Gameplay.Ai;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Movement;

namespace Meitou.DirectorPanel;

/// <summary>Spawning, relations, research, behaviour, bounties, teleports and the squad heal.</summary>
internal sealed partial class DirectorSystem
{
    /// <summary>The packages each squad had before the panel's first change, for "Restore normal behaviour".</summary>
    readonly SortedDictionary<int, SquadPackage[]> _saved = [];

    /// <summary>Spread between spawned characters, metres (port choice: the original's spacing is not known).</summary>
    const float SpawnSpread = 1.5f;

    /// <summary>World units per metre (Kenshi's scale: 1 m is 10 units).</summary>
    const float UnitsPerMetre = 10;

    int _spawned;

    void ApplyWorld(World world, DirectorCommand command)
    {
        switch (command)
        {
            case Spawn spawn:
                SpawnCharacters(world, spawn);
                break;
            case SetRelation relation:
                Relate(world, relation);
                break;
            case Learn learn:
                LearnOne(world, learn.Research);
                break;
            case SetBehaviour behaviour:
                Behave(world, behaviour);
                break;
            case Teleport teleport:
                Say("Characters moved: %d", CharacterPlacement.Place(world, teleport.Characters, teleport.At));
                break;
            case SetBounty bounty:
                Say(crime?.AssignBounty(world, bounty.Character, bounty.Faction, bounty.Amount) > 0 ? "Bounty assigned" : "Choose a faction first");
                break;
            case ClearBounties clear:
                ClearAll(world, clear.Character);
                break;
            default:
                ApplyBody(world, command);
                break;
        }
    }

    void SpawnCharacters(World world, Spawn spawn)
    {
        if (population is null || Cold(world, spawn.Near, out int slot) is null) return;

        var hot = world.Characters.Next[slot];
        var ahead = new Vector2(MathF.Sin(hot.Yaw), MathF.Cos(hot.Yaw));
        var centre = new Vector2(hot.Position.X, hot.Position.Z) + ahead * spawn.Distance * UnitsPerMetre;
        int made = 0;
        for (int i = 0; i < Math.Clamp(spawn.Count, 1, 100); i++)
        {
            var at = centre + new Vector2(ahead.Y, -ahead.X) * ((i - (spawn.Count - 1) / 2f) * SpawnSpread * UnitsPerMetre);
            if (SpawnOne(world, spawn, at)) made++;
        }

        Say("Characters spawned: %d", made);
    }

    bool SpawnOne(World world, Spawn spawn, Vector2 at)
    {
        if (spawn.JoinSquad) return JoinPlayer(world, spawn.Record, at);

        int? faction = data.Relations.Contains(spawn.Faction) ? spawn.Faction : null;
        var id = population!.SpawnSandbox(world, spawn.Record, at, player: false, variant: ++_spawned, faction: faction, aggro: spawn.Hostile);
        return !id.IsNone;
    }

    /// <summary>
    /// "Join player squad": made as the player's own sandbox spawn (a squad of the player's of its own), then moved into the active squad, the
    /// active squad, selection and known factions the spawn resets put back.
    /// </summary>
    bool JoinPlayer(World world, string record, Vector2 at)
    {
        var player = world.Player;
        int active = player.Squad;
        var known = player.KnownFactions;
        var selection = player.Selection.ToList();
        var id = population!.SpawnSandbox(world, record, at, player: true);
        int made = Cold(world, id, out _)?.SquadId ?? -1;
        (player.Squad, player.KnownFactions) = (active, known);
        player.Selection.Clear();
        player.Selection.AddRange(selection);
        if (id.IsNone || active < 0 || made == active) return !id.IsNone;

        world.Commands.Enqueue(new MoveToSquadCommand(id, active) { Tick = world.Tick });
        world.Commands.Enqueue(new RemoveSquadCommand(made) { Tick = world.Tick });
        return true;
    }

    void Relate(World world, SetRelation set)
    {
        var relations = data.Relations;
        if (!relations.Contains(set.A) || !relations.Contains(set.B) || set.A == set.B)
        {
            Say(set.A == set.B ? "That is the same faction." : "Pick both factions.");
            return;
        }

        float value = Math.Clamp(set.Value, -100, 100);
        var cause = new RelationCause(RelationCauseType.Debug);
        PlayerRelations.Add(world, relations, set.A, set.B, value - relations.Get(set.A, set.B), cause);
        if (set.Both) PlayerRelations.Add(world, relations, set.B, set.A, value - relations.Get(set.B, set.A), cause);
        Say("Relation: %s -> %.0f, back -> %.0f", data.Factions[set.A].Name, relations.Get(set.A, set.B), relations.Get(set.B, set.A));
    }

    /// <summary>The squad runs the package alone (as dialogue's <c>change AI</c>), or gets back what it had before the panel's first change.</summary>
    void Behave(World world, SetBehaviour set)
    {
        if (set.Separate && set.Package is not null) Separate(world, set.Character);

        if (Cold(world, set.Character, out _) is not { } cold || world.Squads.Find(cold.SquadId) is not { } squad)
        {
            Say("The character has no squad");
            return;
        }

        if (set.Package is null)
        {
            Restore(world, squad);
            return;
        }

        if (!_saved.ContainsKey(squad.Id) && squad.Ai is { } board)
            _saved[squad.Id] = [.. board.Packages.Select(p => new SquadPackage(Math.Clamp(p.Slot, 0, SquadPackage.MaxSlot), p.Package.Id))];

        world.Commands.Enqueue(new ChangeAi(squad.Id, set.Package) { Tick = world.Tick });
        Say("Behaviour set: %s", data.Db.Find(set.Package)?.Name ?? set.Package);
    }

    void Restore(World world, Squad squad)
    {
        if (!_saved.Remove(squad.Id, out var packages) || packages.Length == 0)
        {
            Say("Restoring behaviour is unavailable");
            return;
        }

        world.Commands.Enqueue(new SetSquadPackages(squad.Id, packages) { Tick = world.Tick });
        Say("Behaviour restored to normal");
    }

    void ClearAll(World world, CharacterId id)
    {
        if (crime is null || Cold(world, id, out _) is not { Bounties: { } ledger }) return;

        foreach (int faction in ledger.Entries.Select(e => e.Faction).ToList()) crime.ClearBounty(world, id, faction);
        Say("All bounties cleared");
    }

    void LearnOne(World world, string research)
    {
        if (world.Research.IsDone(research)) Say("Already known.");
        else if (world.Research.Complete(data.Db, research)) Say("Learned: %s", data.Db.Find(research)?.Name ?? research);
        else Say("Learning failed. It may be known already.");
    }
}
