using Meitou.Simulation;
using static Meitou.WorkForCats.WorkForCatsMod;

namespace Meitou.WorkForCats;

/// <summary>
/// The employee's operator slot: the original's employee runs the native operate order, which holds one of the machine's operator slots, so the
/// town's workers leave that station. Here each sweep holds a slot (<c>AiSystem.HoldWorkSlot</c>) for an employee whose job runs (walking to the
/// station or working it) and lets it go when the job stops, is quit or removed. The engine does not save holds: the first sweep after a load holds
/// again.
/// </summary>
internal sealed partial class WorkSystem
{
    /// <summary>
    /// The workplace each employee holds a slot of (written in the serial sweep, read by the job's checks).
    /// </summary>
    readonly Dictionary<CharacterId, string> _held = [];

    /// <summary>
    /// Holds a slot of the job's workplace while the mod job runs, else lets it go.
    /// </summary>
    void HoldSlot(World world, CharacterId id, CharacterCold cold, string site)
    {
        if (cold.Running is not { Row: JobType })
        {
            ReleaseSlot(world, id);
            return;
        }

        if (_held.GetValueOrDefault(id) == site || _ai is null) return;

        if (_ai.HoldWorkSlot(world, site, id)) _held[id] = site;
        else ReleaseSlot(world, id);
    }

    void ReleaseSlot(World world, CharacterId id)
    {
        if (_held.Remove(id)) _ai?.ReleaseWorkSlot(world, id);
    }

    /// <summary>
    /// Drops the records of employees no longer in the world (the engine has released their slots).
    /// </summary>
    void ForgetGone(World world)
    {
        foreach (var id in _held.Keys.Where(id => !world.Characters.TryResolveNext(id, out _)).ToList()) _held.Remove(id);
    }

    /// <summary>
    /// Whether every operator slot of <paramref name="site"/> is held by others (the AI's <c>WorkSlots</c>; false when the AI does not know it, or
    /// when <paramref name="worker"/> holds one of them). Serial phases.
    /// </summary>
    internal bool StationFull(string site, CharacterId worker) =>
        _held.GetValueOrDefault(worker) != site && _ai?.WorkSlots(site) is { Free: false };
}
