using Meitou.Data.Gameplay.Ai;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.WorkForCats;

/// <summary>
/// Port of "Work for Cats - Get Hired at NPC Workbenches" (workshop 3802290991; facts from its GPL source): a player character takes a paid
/// job at another faction's workbench or production building, works it like a town worker, earns an hourly wage by skill and relation, trains the
/// station's skill and slowly raises the employer's relation. The employment is a mod job (<see cref="JobType"/>) in the character's job list.
/// </summary>
public sealed class WorkForCatsMod : IMod
{
    /// <summary>
    /// The mod job's task type (a mod code: <c>SimulationExtensions.FirstModCode</c> and up).
    /// </summary>
    internal const TaskType JobType = (TaskType)4290;

    internal static readonly WorkSettings Settings = new();

    /// <summary>
    /// The running world's system, for the employment window; null before a world starts.
    /// </summary>
    internal static volatile WorkSystem? Running;

    public void Initialize(ModContext context)
    {
        context.AddSettings(Settings);
        context.AddJobType(JobType, "Paid work");
        context.AddSystem("work", _ => Running = new WorkSystem(Settings, context.Population), SystemAnchor.End);
        context.AddUi(new EmploymentWindow(context));
        context.Log("Work for Cats initialized");
    }
}

/// <summary>
/// <paramref name="Actor"/> takes a job at the workplace <paramref name="Site"/> (a building key, as the HUD inspects it).
/// </summary>
public sealed record HireOrder(CharacterId Actor, string Site) : SimCommand;

/// <summary>
/// <paramref name="Actor"/> quits its job; the unfinished wage hour is lost.
/// </summary>
public sealed record QuitOrder(CharacterId Actor) : SimCommand;
