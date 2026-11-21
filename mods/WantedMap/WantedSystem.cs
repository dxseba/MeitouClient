using System.Numerics;
using System.Text;
using Meitou.Data.Fcs;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Factions;

namespace Meitou.WantedMap;

/// <summary>Where a poster's target is: on the spot, its squad away (approximate), a prisoner, or dead recently.</summary>
enum TargetState { Exact, Away, Prisoner, Dead }

/// <summary>A target marker: the player character carrying its poster, the target's name, its state and the map point (X, Z).</summary>
sealed record TargetMarker(CharacterId Carrier, string Name, TargetState State, Vector2 At);

/// <summary>
/// Matches "WANTED: name" posters in the player characters' inventories to characters of that name and publishes their markers every 60th tick:
/// loaded ones where they stand (prisoners apart), off-screen ones at their platoon, and the recently dead (remembered per playthrough, saved) at
/// the place of death for a game day.
/// </summary>
sealed class WantedSystem : ICharacterDeathObserver, IModSaveData
{
    const int Version = 1;
    const double DeadHours = 24;

    readonly Dictionary<string, (Vector2 At, double Hours)> _deaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The markers; refreshed while the world runs.</summary>
    public volatile IReadOnlyList<TargetMarker> Markers = [];

    public void Died(World world, in CharacterDeath death)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(death.Victim, out int slot) || table.Cold(slot) is not { } cold || cold.IsPlayer) return;

        var at = table.Next[slot].Position;
        _deaths[cold.Name] = (new(at.X, at.Z), world.GameHours);
    }

    public void SlowWorld(World world)
    {
        if (world.Tick % 60 != 0) return;

        foreach (var name in _deaths.Where(d => world.GameHours - d.Value.Hours > DeadHours).Select(d => d.Key).ToList()) _deaths.Remove(name);
        Markers = Publish(world);
    }

    List<TargetMarker> Publish(World world)
    {
        var markers = new List<TargetMarker>();
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { IsPlayer: true, Inventory: { } inventory }) continue;

            var carrier = table.IdOf(slot);
            foreach (var name in inventory.Items.Select(i => Posters.TargetOf(i.Name)).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (Find(world, name) is { } found) markers.Add(new(carrier, name, found.State, found.At));
            }
        }

        return markers;
    }

    (TargetState State, Vector2 At)? Find(World world, string name)
    {
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { IsPlayer: false } cold || !Same(cold.Name, name)) continue;

            var at = table.Next[slot].Position;
            return (cold.IsImprisoned ? TargetState.Prisoner : TargetState.Exact, new(at.X, at.Z));
        }

        foreach (var platoon in world.Platoons.All)
        {
            if (platoon.Stored?.Members.FirstOrDefault(m => Same(m.Cold.Name, name)) is { } member)
                return (member.Cold.IsImprisoned ? TargetState.Prisoner : TargetState.Away, platoon.Position);
        }

        return _deaths.TryGetValue(name, out var death) ? (TargetState.Dead, death.At) : null;
    }

    static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public void Write(Stream stream)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Version);
        writer.Write(_deaths.Count);
        foreach (var (name, (at, hours)) in _deaths.OrderBy(d => d.Key, StringComparer.Ordinal))
        {
            writer.Write(name);
            writer.Write(at.X);
            writer.Write(at.Y);
            writer.Write(hours);
        }
    }

    public void Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (reader.ReadInt32() != Version) return;

        _deaths.Clear();
        for (int i = reader.ReadInt32(); i > 0; i--)
        {
            string name = reader.ReadString();
            _deaths[name] = (new(reader.ReadSingle(), reader.ReadSingle()), reader.ReadDouble());
        }
    }
}

/// <summary>The poster format: an item named <c>WANTED: name</c> (also the full-width colon of the Chinese titles).</summary>
static class Posters
{
    public static string? TargetOf(string itemName)
    {
        if (!itemName.StartsWith("WANTED", StringComparison.OrdinalIgnoreCase)) return null;

        int colon = itemName.IndexOfAny([':', '：']);
        string name = colon >= 0 ? itemName[(colon + 1)..].Trim() : "";
        return name.Length > 0 ? name : null;
    }
}
