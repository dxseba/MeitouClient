using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Production;
using static Meitou.WorkForCats.WorkForCatsMod;

namespace Meitou.WorkForCats;

/// <summary>
/// The saved state: each employee by name (ids are not stable across a load) with its workplace and unpaid hour, and the relation progress by
/// faction string id. The save keeps the mod job itself too (<c>meitou mjob</c>); a load whose list lacks it gets it back from here.
/// </summary>
internal sealed partial class WorkSystem : IModSaveData
{
    const int Version = 1;

    /// <summary>
    /// A saved employee waiting to be matched to a loaded character.
    /// </summary>
    sealed record Saved(string Name, string Site, double Hours, double Cats);

    List<Saved>? _loaded;

    public void Write(Stream stream)
    {
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(Version);

        var employees = _employees.OrderBy(e => e.Key.Slot).Select(e => e.Value).ToList();
        writer.Write(employees.Count);
        foreach (var e in employees)
        {
            writer.Write(e.Name);
            writer.Write(e.Site);
            writer.Write(e.Hours);
            writer.Write(e.Cats);
        }

        writer.Write(_relationHours.Count);
        foreach (var (faction, hours) in _relationHours)
        {
            writer.Write(faction);
            writer.Write(hours);
        }
    }

    public void Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        if (reader.ReadInt32() != Version) throw new InvalidDataException("Unknown Work for Cats save version");

        var loaded = new List<Saved>();
        for (int count = reader.ReadInt32(), i = 0; i < count; i++)
            loaded.Add(new Saved(reader.ReadString(), reader.ReadString(), reader.ReadDouble(), reader.ReadDouble()));

        _relationHours.Clear();
        for (int count = reader.ReadInt32(), i = 0; i < count; i++) _relationHours[reader.ReadString()] = reader.ReadDouble();

        _employees.Clear();
        _loaded = loaded;
    }

    /// <summary>
    /// After a load: each saved employee takes the first player character of its name not matched yet, its job back on the list when the load
    /// dropped it.
    /// </summary>
    void Restore(World world)
    {
        var table = world.Characters;
        foreach (var saved in _loaded!)
        {
            for (int slot = 0; slot < table.HighWater; slot++)
            {
                if (!table.Next[slot].Alive || table.Cold(slot) is not { IsPlayer: true } cold || cold.Name != saved.Name) continue;
                if (_employees.ContainsKey(table.IdOf(slot))) continue;

                Rehire(table, slot, cold, saved);
                break;
            }
        }

        _loaded = null;
    }

    void Rehire(CharacterTable table, int slot, CharacterCold cold, Saved saved)
    {
        string? site = JobSite(cold);
        if (site is null)
        {
            cold.Jobs.Insert(0, new PermaJob(JobType, saved.Site));
            DecisionSystem.Request(cold);
        }

        bool same = site is null || site == saved.Site;
        _employees[table.IdOf(slot)] = new Employee
        {
            Name = cold.Name,
            Site = site ?? saved.Site,
            Hours = same ? saved.Hours : 0,
            Cats = same ? saved.Cats : 0,
        };
    }

    public void Hash(ref StateHasher hasher)
    {
        hasher.Add(_employees.Count);
        foreach (var (id, e) in _employees.OrderBy(e => e.Key.Slot))
        {
            hasher.Add(id);
            hasher.Add(Rng.StableHash(e.Site));
            hasher.Add(e.Hours);
            hasher.Add(e.Cats);
            hasher.Add(e.Fails);
            hasher.Add(e.RetryTick);
        }

        hasher.Add(_relationHours.Count);
        foreach (var (faction, hours) in _relationHours)
        {
            hasher.Add(Rng.StableHash(faction));
            hasher.Add(hours);
        }
    }
}
