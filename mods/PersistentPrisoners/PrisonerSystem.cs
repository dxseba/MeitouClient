using System.Collections.Frozen;
using System.Numerics;
using System.Text;
using Meitou.Data;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Population;

namespace Meitou.PersistentPrisoners;

/// <summary>One overview row, with the original's columns: name, faction, cage, Nu (hunger x 100), condition, location and status.</summary>
sealed record PrisonerRow(string Name, string Faction, string Cage, int Nu, string Condition, string Location, string Status, bool Dead, bool Loaded);

/// <summary>
/// Learns the player's cages while they are loaded (fixture ids, saved with the world, as a cage's fixture may be gone while its zone is out),
/// answers the keep test for any thread, keeps a caged corpse whose zone unloads (<see cref="IRemovalVeto"/>), sets the hunger floor the original's
/// <c>getMagicHungerSetting</c> hook gives, and publishes the overview every 30th tick.
/// </summary>
sealed class PrisonerSystem(PrisonerSettings settings, PopulationData population, GameDatabase data) : IRemovalVeto, IModSaveData
{
    const int Version = 2;

    /// <summary>The original's "no free food" floor for a prisoner who must starve or is carried by the player (<b>Verified</b>, DLL rdata -10).</summary>
    const float NoFood = -10;

    readonly HashSet<string> _cages = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> _cageNames = new(StringComparer.Ordinal);
    readonly Dictionary<CharacterId, float> _fed = [];
    volatile FrozenSet<string> _frozen = FrozenSet<string>.Empty;
    World? _world;

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

            bool mine = f.Owner == world.Player.Faction && world.Player.Faction >= 0;
            changed |= mine ? _cages.Add(f.Id) : _cages.Remove(f.Id);
            if (mine) _cageNames[f.Id] = data.Find(f.HostRecordId)?.Name ?? data.Find(f.RecordId)?.Name ?? "";
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

    /// <summary>
    /// A corpse in a player cage stays when its zone unloads (the original keeps "caged corpses ... when you leave the area"); its decay is the
    /// game's.
    /// </summary>
    public bool Keep(World world, in RemovalAsk ask) =>
        ask.Kind == RemovalKind.CorpseUnloaded && settings.Protect && world.Characters.TryResolveNext(ask.Character, out int slot)
        && world.Characters.Cold(slot) is { } cold && InPlayerCage(cold);

    #endregion

    #region Hunger floor

    /// <summary>
    /// The original's hunger floor for a living non-player character of a race that hungers (<b>Verified</b>, original DLL (decompiled)): carried by
    /// a player character, none (it starves); in a player cage, none with starvation allowed, else Minimum Nu / 100, so the game feeds it towards
    /// that level by its own free-food rule. Set through <c>CharacterCold.FedTo</c> and taken back when it no longer applies.
    /// </summary>
    void Feed(World world)
    {
        var table = world.Characters;
        var now = new Dictionary<CharacterId, float>();
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { Medical: { Dead: false } } cold || Floor(world, cold) is not { } floor) continue;

            var id = table.IdOf(slot);
            if (cold.FedTo is null || _fed.TryGetValue(id, out float mine) && cold.FedTo == mine)
            {
                cold.FedTo = floor;
                now[id] = floor;
            }
        }

        foreach (var (id, mine) in _fed)
        {
            if (!now.ContainsKey(id) && table.TryResolveNext(id, out int slot) && table.Cold(slot) is { } cold && cold.FedTo == mine) cold.FedTo = null;
        }

        _fed.Clear();
        foreach (var (id, floor) in now) _fed[id] = floor;
    }

    float? Floor(World world, CharacterCold cold)
    {
        if (cold.IsPlayer || cold.Race is not { HungerRate: > 0 }) return null;
        if (CarriedByPlayer(world, cold)) return NoFood;
        if (!InPlayerCage(cold)) return null;

        return settings.Starvation ? NoFood : Math.Clamp(settings.MinimumNutrition, 1, 300) / 100f;
    }

    static bool CarriedByPlayer(World world, CharacterCold cold) =>
        !cold.CarriedBy.IsNone && world.Characters.TryResolveNext(cold.CarriedBy, out int slot) && world.Characters.Cold(slot) is { IsPlayer: true };

    #endregion

    #region Overview

    /// <summary>The occupants of the player's cages, loaded and stored, dead or alive, and the player's own characters held anywhere.</summary>
    List<PrisonerRow> Overview(World world)
    {
        var rows = new List<PrisonerRow>();
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { IsImprisoned: true } cold) continue;
            if (InPlayerCage(cold) || cold.IsPlayer) rows.Add(Row(cold, table.Next[slot].Position, loaded: true));
        }

        foreach (var p in world.Platoons.All)
        {
            foreach (var m in p.Stored?.Members ?? [])
            {
                if (InPlayerCage(m.Cold)) rows.Add(Row(m.Cold, m.Position, loaded: false));
            }
        }

        return [.. rows.OrderBy(r => r.Status, StringComparer.Ordinal).ThenBy(r => r.Location, StringComparer.Ordinal).ThenBy(r => r.Name, StringComparer.Ordinal)];
    }

    PrisonerRow Row(CharacterCold cold, Vector3 at, bool loaded)
    {
        var medical = cold.Medical;
        int nu = (int)MathF.Round((medical?.Hunger ?? 0) * 100);
        bool dead = medical?.Dead ?? cold.DiedTick >= 0;
        string faction = population.Relations.Contains(cold.Faction) ? population.Relations.Factions[cold.Faction].Name : "Unknown";
        string cage = cold.InWhat is { } id ? _cageNames.GetValueOrDefault(id, "") : "";
        return new(cold.Name, faction, cage, nu, Condition(cold, medical, nu), Location(at), Status(cold, loaded), dead, loaded);
    }

    /// <summary>
    /// The original's condition words: Dead, Dying, Unconscious or Alive, with "/ Starving" (or Starving alone) for a race that hungers at Nu below
    /// 100. Dying: the original asks <c>MedicalSystem::isProbablyDying</c> or a pair of medical fields; the port has no such test and reads no blood
    /// and still bleeding (<b>Unknown</b> whether that is what the original's fields mean).
    /// </summary>
    static string Condition(CharacterCold cold, MedicalState? medical, int nu)
    {
        if (medical is null) return "Alive";
        if (medical.Dead) return "Dead";

        bool starving = cold.Race is { HungerRate: > 0 } && nu < 100;
        bool dying = medical.Blood <= 0 && medical.Bleeding > 0;
        if (dying) return starving ? "Dying / Starving" : "Dying";
        if (medical.Unconscious) return starving ? "Unconscious / Starving" : "Unconscious";
        return starving ? "Starving" : "Alive";
    }

    /// <summary>The original's status words: Player squad, Player / Unloaded, Unloaded, else Protected.</summary>
    static string Status(CharacterCold cold, bool loaded) => cold.IsPlayer
        ? loaded ? "Player squad" : "Player / Unloaded"
        : loaded ? "Protected" : "Unloaded";

    /// <summary>The nearest town's name within 3000 units, else "Unknown location".</summary>
    string Location(Vector3 at)
    {
        var near = population.Sites.MinBy(s => Vector2.DistanceSquared(new(s.Position.X, s.Position.Z), new(at.X, at.Z)));
        if (near is not null && Vector2.Distance(new(near.Position.X, near.Position.Z), new(at.X, at.Z)) < 3000) return near.Record.Name;

        return "Unknown location";
    }

    #endregion

    #region Save

    public void Write(Stream stream)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Version);
        writer.Write(_cages.Count);
        foreach (var cage in _cages.Order(StringComparer.Ordinal))
        {
            writer.Write(cage);
            writer.Write(_cageNames.GetValueOrDefault(cage, ""));
        }
    }

    public void Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        int version = reader.ReadInt32();
        if (version is not (1 or 2)) return;

        _cages.Clear();
        _cageNames.Clear();
        for (int i = reader.ReadInt32(); i > 0; i--)
        {
            string cage = reader.ReadString();
            _cages.Add(cage);
            if (version >= 2) _cageNames[cage] = reader.ReadString();
        }

        _frozen = _cages.ToFrozenSet(StringComparer.Ordinal);
    }

    #endregion
}
