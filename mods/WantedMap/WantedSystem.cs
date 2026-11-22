using System.Numerics;
using System.Text;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Factions;

namespace Meitou.WantedMap;

/// <summary>
/// Where a poster's target is: on the spot (red), its squad away (orange, approximate), at home before its squad first appeared (yellow), a
/// prisoner (blue) or dead recently (grey).
/// </summary>
enum TargetState { Exact, Away, Home, Prisoner, Dead }

/// <summary>
/// A target marker: the player character carrying its poster, the target's id and name, its state and the map point (X, Z).
/// </summary>
sealed record TargetMarker(CharacterId Carrier, string Target, string Name, TargetState State, Vector2 At);

/// <summary>
/// Matches the posters the player's characters carry (their inventory and backpack) to their targets (<see cref="PosterIndex"/>) and publishes
/// their markers every 60th tick: loaded targets where they stand, off-screen ones at their platoon, never-seen ones at their home town, the
/// recently dead where they died. Deaths and the targets seen so far are remembered per playthrough (saved with the world).
/// </summary>
sealed class WantedSystem(PosterIndex posters, TownPlaces towns, WantedSettings settings) : ICharacterDeathObserver, IModSaveData
{
    const int Version = 2;

    /// <summary>
    /// Deaths of targets by CHARACTER id: where and the game hours when.
    /// </summary>
    readonly Dictionary<string, (Vector2 At, double Hours)> _deaths = new(StringComparer.Ordinal);

    /// <summary>
    /// Targets (CHARACTER or SQUAD_TEMPLATE ids) found in the world before (<c>RememberSeen</c>).
    /// </summary>
    readonly SortedSet<string> _seen = new(StringComparer.Ordinal);

    /// <summary>
    /// The markers; refreshed while the world runs.
    /// </summary>
    public volatile IReadOnlyList<TargetMarker> Markers = [];

    public void Died(World world, in CharacterDeath death)
    {
        if (death.Record.Length == 0 || !posters.IsTarget(death.Record)) return;

        var at = DeathPlace(world, death);
        if (at is { } place) _deaths[death.Record] = (place, world.GameHours);
    }

    static Vector2? DeathPlace(World world, in CharacterDeath death)
    {
        var table = world.Characters;
        if (table.TryResolveNext(death.Victim, out int slot))
        {
            var p = table.Next[slot].Position;
            return new(p.X, p.Z);
        }

        int platoon = death.Platoon;
        return world.Platoons.All.FirstOrDefault(p => p.Id == platoon)?.Position;
    }

    public void SlowWorld(World world)
    {
        if (world.Tick % 60 != 0) return;

        double hours = settings.DeadMarkerHours;
        foreach (var id in _deaths.Where(d => world.GameHours - d.Value.Hours > hours).Select(d => d.Key).ToList()) _deaths.Remove(id);
        Markers = settings.Enabled ? Publish(world) : [];
    }

    List<TargetMarker> Publish(World world)
    {
        var markers = new List<TargetMarker>();
        var table = world.Characters;
        var finder = new TargetFinder(world);
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { IsPlayer: true, Inventory: { } inventory }) continue;

            var carrier = table.IdOf(slot);
            var held = inventory.Searched(carriedBags: false).Select(i => i.Record).Distinct(StringComparer.Ordinal);
            foreach (var poster in held.Select(posters.Of).OfType<PosterTargets>())
                AddPoster(markers, carrier, poster, finder);
        }

        return markers;
    }

    void AddPoster(List<TargetMarker> markers, CharacterId carrier, PosterTargets poster, TargetFinder finder)
    {
        foreach (var id in poster.Characters)
            if (Locate(id, finder.Character(id)) is { } m) markers.Add(new(carrier, id, m.Name ?? posters.NameOf(id), m.State, m.At));

        foreach (var id in poster.Squads)
            if (Locate(id, finder.Squad(id)) is { } m) markers.Add(new(carrier, id, posters.NameOf(id), m.State, m.At));
    }

    /// <summary>
    /// Where target <paramref name="id"/> is shown: where it was found; else where it died, for <c>DeadMarkerHours</c>; else, never seen, at its
    /// home town (<c>TownFallback</c>); a target seen before and now nowhere counts as dead and is not shown.
    /// </summary>
    (TargetState State, Vector2 At, string? Name)? Locate(string id, (TargetState State, Vector2 At, string? Name)? found)
    {
        if (found is { } here)
        {
            if (settings.RememberSeen) _seen.Add(id);
            return here;
        }

        if (_deaths.TryGetValue(id, out var death)) return settings.DeadMarkerHours > 0 ? (TargetState.Dead, death.At, null) : null;
        if (settings.RememberSeen && _seen.Contains(id) || !settings.TownFallback) return null;

        return posters.HomesOf(id).Select(towns.Of).OfType<Vector2>().FirstOrDefault() is var home && home != default
            ? (TargetState.Home, home, null)
            : null;
    }

    public void Write(Stream stream)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Version);
        writer.Write(_deaths.Count);
        foreach (var (id, (at, hours)) in _deaths.OrderBy(d => d.Key, StringComparer.Ordinal))
        {
            writer.Write(id);
            writer.Write(at.X);
            writer.Write(at.Y);
            writer.Write(hours);
        }

        writer.Write(_seen.Count);
        foreach (var id in _seen) writer.Write(id);
    }

    public void Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (reader.ReadInt32() != Version) return;

        _deaths.Clear();
        for (int i = reader.ReadInt32(); i > 0; i--)
        {
            string id = reader.ReadString();
            _deaths[id] = (new(reader.ReadSingle(), reader.ReadSingle()), reader.ReadDouble());
        }

        _seen.Clear();
        for (int i = reader.ReadInt32(); i > 0; i--) _seen.Add(reader.ReadString());
    }
}

/// <summary>
/// Finds targets in the world by record: loaded characters (a prisoner apart), the members of stored platoons, and squad templates' platoons.
/// </summary>
sealed class TargetFinder(World world)
{
    public (TargetState State, Vector2 At, string? Name)? Character(string record)
    {
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { IsPlayer: false } cold || cold.RecordId != record) continue;
            if (cold.Medical is { Dead: true }) continue;

            var at = table.Next[slot].Position;
            return (cold.IsImprisoned ? TargetState.Prisoner : TargetState.Exact, new(at.X, at.Z), cold.Name);
        }

        foreach (var platoon in world.Platoons.All)
        {
            if (platoon.Dead || platoon.Stored?.Members.FirstOrDefault(m => m.Cold.RecordId == record) is not { } member) continue;

            bool prisoner = member.Cold.IsImprisoned || platoon.Imprisoned;
            return (prisoner ? TargetState.Prisoner : TargetState.Away, platoon.Position, member.Cold.Name);
        }

        return null;
    }

    public (TargetState State, Vector2 At, string? Name)? Squad(string template)
    {
        foreach (var platoon in world.Platoons.All)
        {
            if (platoon.Dead || platoon.TemplateId != template) continue;
            if (platoon.SquadId >= 0 && Member(platoon.SquadId) is { } at) return (TargetState.Exact, at, null);

            return (platoon.Imprisoned ? TargetState.Prisoner : TargetState.Away, platoon.Position, null);
        }

        return null;
    }

    Vector2? Member(int squad)
    {
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { } cold || cold.SquadId != squad) continue;

            var p = table.Next[slot].Position;
            return new(p.X, p.Z);
        }

        return null;
    }
}
