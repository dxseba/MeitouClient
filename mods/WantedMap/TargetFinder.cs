using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Factions;

namespace Meitou.WantedMap;

/// <summary>
/// Where a target was found: a loaded character, or a platoon (a stored member's, or a squad template's).
/// </summary>
readonly record struct TargetHandle(CharacterId Character, int Platoon, bool Squad);

/// <summary>
/// A target found: its state, map point, name (null for a squad) and where it was found.
/// </summary>
readonly record struct FoundTarget(TargetState State, Vector2 At, string? Name, TargetHandle Handle);

/// <summary>
/// Finds targets in the world by record: loaded characters (a prisoner apart), the members of stored platoons, and squad templates' platoons. A
/// world scan (<c>WorldScanSeconds</c>) searches everything and remembers where each target was found; between them only the positions of the
/// targets found are refreshed, as the original does, and a target not found stays so until the next world scan.
/// </summary>
sealed class TargetFinder(World world, Dictionary<string, TargetHandle?> found, bool worldScan)
{
    public FoundTarget? Character(string record) => Find(record, () => SearchCharacter(record), h => RefreshCharacter(record, h));

    public FoundTarget? Squad(string template) => Find(template, () => SearchSquad(template), RefreshSquad);

    FoundTarget? Find(string id, Func<FoundTarget?> search, Func<TargetHandle, FoundTarget?> refresh)
    {
        if (!worldScan && found.TryGetValue(id, out var handle)) return handle is { } h ? refresh(h) : null;

        var target = search();
        found[id] = target?.Handle;
        return target;
    }

    FoundTarget? SearchCharacter(string record)
    {
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { IsPlayer: false } cold || cold.RecordId != record) continue;
            if (cold.Medical is { Dead: true }) continue;

            return Loaded(slot, cold);
        }

        foreach (var platoon in world.Platoons.All)
        {
            if (platoon.Dead || platoon.Stored?.Members.FirstOrDefault(m => m.Cold.RecordId == record) is not { } member) continue;

            bool prisoner = member.Cold.IsImprisoned || platoon.Imprisoned;
            var handle = new TargetHandle(CharacterId.None, platoon.Id, false);
            return new(prisoner ? TargetState.Prisoner : TargetState.Away, platoon.Position, member.Cold.Name, handle);
        }

        return null;
    }

    FoundTarget Loaded(int slot, CharacterCold cold)
    {
        var at = world.Characters.Next[slot].Position;
        var state = cold.IsImprisoned ? TargetState.Prisoner : TargetState.Exact;
        return new(state, new(at.X, at.Z), cold.Name, new(world.Characters.IdOf(slot), -1, false));
    }

    /// <summary>
    /// A found character where it is now: the same loaded character (alive, not dead), else the stored platoon it was found in.
    /// </summary>
    FoundTarget? RefreshCharacter(string record, TargetHandle handle)
    {
        var table = world.Characters;
        if (handle.Platoon < 0)
        {
            if (!table.TryResolveNext(handle.Character, out int slot) || table.Cold(slot) is not { } cold || cold.RecordId != record) return null;

            return cold.Medical is { Dead: true } ? null : Loaded(slot, cold);
        }

        if (PlatoonOf(handle.Platoon) is not { Dead: false } platoon) return null;
        if (platoon.Stored?.Members.FirstOrDefault(m => m.Cold.RecordId == record) is not { } member) return null;

        bool prisoner = member.Cold.IsImprisoned || platoon.Imprisoned;
        return new(prisoner ? TargetState.Prisoner : TargetState.Away, platoon.Position, member.Cold.Name, handle);
    }

    FoundTarget? SearchSquad(string template)
    {
        foreach (var platoon in world.Platoons.All)
        {
            if (!platoon.Dead && platoon.TemplateId == template) return OfPlatoon(platoon);
        }

        return null;
    }

    FoundTarget? RefreshSquad(TargetHandle handle) => PlatoonOf(handle.Platoon) is { Dead: false } platoon ? OfPlatoon(platoon) : null;

    FoundTarget OfPlatoon(Platoon platoon)
    {
        var handle = new TargetHandle(CharacterId.None, platoon.Id, true);
        if (platoon.SquadId >= 0 && Member(platoon.SquadId) is { } at) return new(TargetState.Exact, at, null, handle);

        return new(platoon.Imprisoned ? TargetState.Prisoner : TargetState.Away, platoon.Position, null, handle);
    }

    Platoon? PlatoonOf(int id) => world.Platoons.All.FirstOrDefault(p => p.Id == id);

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
