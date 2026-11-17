using System.Reflection;
using HarmonyLib;
using Meitou.Simulation;
using Meitou.Simulation.Kidnap;

namespace Meitou.Cannibalism;

/// <summary>
/// The engine's captive job list (<c>CaptiveSystem._jobs</c>, private), reached by reflection: <see cref="CaptiveSystem.Jobs"/> is read-only, and
/// a job added here is stepped by the engine as its own (the walk, the <c>eat</c> clip, the bites, the barks, the end).
/// </summary>
sealed class CaptiveJobs(FieldInfo jobs)
{
    public static CaptiveJobs Create()
    {
        var field = AccessTools.Field(typeof(CaptiveSystem), "_jobs") ?? throw new MissingFieldException(nameof(CaptiveSystem), "_jobs");
        if (field.FieldType != typeof(List<CaptiveJob>)) throw new MissingFieldException(nameof(CaptiveSystem), "_jobs as List<CaptiveJob>");

        return new(field);
    }

    /// <summary>The running world's captive system; null when it has none.</summary>
    public static CaptiveSystem? System(World world) => world.Systems.OfType<CaptiveSystem>().FirstOrDefault();

    /// <summary>Puts <paramref name="job"/> in the list, in place of any job the worker had.</summary>
    public void Add(CaptiveSystem system, CaptiveJob job)
    {
        var list = (List<CaptiveJob>)jobs.GetValue(system)!;
        list.RemoveAll(j => j.Worker == job.Worker);
        list.Add(job);
    }
}
