using Meitou.Data.Gameplay.Ai;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.FirstAid;
using Meitou.Simulation.Production;

namespace Meitou.MedicSplintRigging;

/// <summary>
/// Port of Medic Button Splint Rigging (workshop 3781354452, <c>MedicButtonSplintRigging.dll</c>): Shift with the HUD's Medic button gives the
/// characters the Splint Rigging job too, after the Medic (and Robotics) jobs the button gives.
/// </summary>
public sealed class MedicSplintRiggingMod : IMod
{
    public void Initialize(ModContext context)
    {
        // At the end, so the engine's first-aid system has added the button's 58 (and 57) jobs when the splint job follows them.
        context.AddSystem("splint-rigging", _ => new SplintRiggingSystem(), SystemAnchor.End);
        context.Log("Medic Button Splint Rigging initialized");
    }
}

/// <summary>
/// Reads the engine's own Medic button command (<see cref="MedicJobCommand"/>; <c>Job</c> is the button with Shift, docs/game/ui-input.md): for
/// such a press each of its characters (the selection when it names none, as the first-aid system takes it) is given the job a Splinting order
/// (249) becomes, Splint Rigging (250), as a Shift order is (<see cref="PermaJobs.Give"/>: a second one is refused, an animal takes none).
/// </summary>
sealed class SplintRiggingSystem : ITickSystem
{
    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands)
        {
            if (command is MedicJobCommand { Job: true } press) GiveSplintJob(world, press);
        }
    }

    static void GiveSplintJob(World world, MedicJobCommand press)
    {
        var table = world.Characters;
        IReadOnlyList<CharacterId> who = press.Characters.Count > 0 ? press.Characters : world.Player.Selection.ToArray();
        foreach (var id in who)
        {
            if (!table.TryResolveNext(id, out int slot) || table.Cold(slot) is not { } cold) continue;

            PermaJobs.Give(cold, TaskType.SplintOrder, PermaJob.Untargeted(TaskType.SplintOrder));
        }
    }
}
