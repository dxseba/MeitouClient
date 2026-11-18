using System.Collections.Frozen;
using System.Numerics;
using System.Text;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Population;

namespace Meitou.PersistentPrisoners;

/// <summary>One overview row.</summary>
sealed record PrisonerRow(string Name, string Faction, int Nutrition, string Condition, string Place, bool Loaded, bool OwnSquad);

/// <summary>
/// Learns the player's cages while they are loaded (fixture ids, saved with the world, as a cage's fixture may be gone while its zone is out),
/// answers the keep test for any thread, feeds caged prisoners with starvation off, and publishes the overview every 30th tick.
/// </summary>
sealed class PrisonerSystem(PrisonerSettings settings, PopulationData population) : ITickSystem, IModSaveData
{
    const int Version = 1;

    /// <summary>Hunger units a fed prisoner gains per game hour (the game's scale 0-3), below the minimum.</summary>
    const float FeedPerHour = 0.25f;

    readonly HashSet<string> _cages = new(StringComparer.Ordinal);
    volatile FrozenSet<string> _frozen = FrozenSet<string>.Empty;
    World? _world;
    double _lastHours = double.NaN;

    /// <summary>The overview rows; refreshed while the world runs.</summary>
    public volatile IReadOnlyList<PrisonerRow> Rows = [];

    public void SlowWorld(World world)
    {
        _world = world;
        if (world.Tick % 30 != 0) return;

        LearnCages(world);
        Feed(world);
        Rows = Overview(world);
    }

    #region Cages and the keep test

    void LearnCages(World world)
    {
        bool changed = false;
        foreach (var f in world.Fixtures.All)
        {
            if (f.Kind != FixtureKind.Cage) continue;
            changed |= f.Owner == world.Player.Faction && world.Player.Faction >= 0 ? _cages.Add(f.Id) : _cages.Remove(f.Id);
        }

        if (changed) _frozen = _cages.ToFrozenSet(StringComparer.Ordinal);
    }

    bool InPlayerCage(CharacterCold cold) => cold.IsImprisoned && cold.InWhat is { } cage && _frozen.Contains(cage);

    /// <summary>Whether the platoon holds a prisoner of a player cage, loaded or stored (read-only: safe on the population's worker threads).</summary>
    public bool Protects(Platoon p)
    {
        if (p.Stored?.Members.Any(m => InPlayerCage(m.Cold)) == true) return true;
        if (_world is not { } world || world.Squads.Find(p.SquadId) is not { } squad) return false;

        var table = world.Characters;
        foreach (var id in squad.Members)
        {
            if (table.TryResolveNext(id, out int slot) && table.Cold(slot) is { } cold && InPlayerCage(cold)) return true;
        }

        return false;
    }

    #endregion

    #region Feeding

    void Feed(World world)
    {
        double hours = double.IsNaN(_lastHours) ? 0 : Math.Clamp(world.GameHours - _lastHours, 0, 1);
        _lastHours = world.GameHours;
        if (settings.Starvation || hours <= 0) return;

        float floor = settings.MinimumNutrition / 100f;
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { Medical: { Dead: false } medical } cold || !InPlayerCage(cold)) continue;
            if (medical.Hunger < floor) medical.Hunger = Math.Min(floor, medical.Hunger + FeedPerHour * (float)hours);
        }
    }

    #endregion

    #region Overview

    /// <summary>Prisoners in the player's cages, loaded and stored, and the player's own characters held anywhere.</summary>
    List<PrisonerRow> Overview(World world)
    {
        var rows = new List<PrisonerRow>();
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (table.Cold(slot) is not { } cold || !cold.IsImprisoned) continue;
            if (InPlayerCage(cold) || cold.IsPlayer) rows.Add(Row(cold, table.Next[slot].Position, loaded: true));
        }

        foreach (var p in world.Platoons.All)
        {
            foreach (var m in p.Stored?.Members ?? [])
            {
                if (InPlayerCage(m.Cold)) rows.Add(Row(m.Cold, m.Position, loaded: false));
            }
        }

        return [.. rows.OrderBy(r => r.OwnSquad).ThenBy(r => r.Place, StringComparer.Ordinal).ThenBy(r => r.Name, StringComparer.Ordinal)];
    }

    PrisonerRow Row(CharacterCold cold, Vector3 at, bool loaded)
    {
        var medical = cold.Medical;
        int nutrition = (int)MathF.Round((medical?.Hunger ?? 0) * 100);
        string condition = medical is null ? "?" : medical.Dead ? "Dead" : medical.Unconscious ? "Unconscious" : nutrition < 100 ? "Starving" : "Awake";
        string faction = population.Relations.Contains(cold.Faction) ? population.Relations.Factions[cold.Faction].Name : "Nobody";
        return new(cold.Name, faction, nutrition, condition, Place(at), loaded, cold.IsPlayer);
    }

    /// <summary>The nearest town's name, within 3000 units; else the coordinates.</summary>
    string Place(Vector3 at)
    {
        var near = population.Sites.MinBy(s => Vector2.DistanceSquared(new(s.Position.X, s.Position.Z), new(at.X, at.Z)));
        if (near is not null && Vector2.Distance(new(near.Position.X, near.Position.Z), new(at.X, at.Z)) < 3000) return near.Record.Name;

        return $"{at.X:0}, {at.Z:0}";
    }

    #endregion

    #region Save

    public void Write(Stream stream)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Version);
        writer.Write(_cages.Count);
        foreach (var cage in _cages.Order(StringComparer.Ordinal)) writer.Write(cage);
    }

    public void Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (reader.ReadInt32() != Version) return;

        _cages.Clear();
        for (int i = reader.ReadInt32(); i > 0; i--) _cages.Add(reader.ReadString());
        _frozen = _cages.ToFrozenSet(StringComparer.Ordinal);
    }

    #endregion
}
