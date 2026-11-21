using Meitou.Data.Gameplay.Ai;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Movement;
using Meitou.Simulation.Population;

namespace Meitou.SleepFix;

/// <summary>
/// Port of SleepFix (workshop 3787375792), rebuilt from its description: a wounded player character that is idle or working a job, out of combat
/// and bandaged, gets the player's own bed order (<c>World.GiveOrder</c>, <c>TaskType.UseBed</c>) to the best free bed the engine's bed search
/// finds; once healed it gets up (a move order to where it lies) and its jobs take it back.
/// </summary>
public sealed class SleepFixMod : IMod
{
    public void Initialize(ModContext context)
    {
        var settings = new SleepSettings();
        context.AddSettings(settings);
        context.AddSystem("sleepfix", _ => new SleepSystem(settings, context.Population), SystemAnchor.End);
        context.Log("SleepFix initialized");
    }
}

public sealed class SleepSettings
{
    [Setting("Send wounded to bed", Hint = "The original's autoBedOrder.")]
    public bool Enabled { get; set; } = true;

    [Setting("Healed at", Hint = "Every part at this share of its health counts as healed (1 = 100%).", Min = 0.5, Max = 1, Step = 0.01)]
    public float HealThreshold { get; set; } = 1;

    [Setting("Wounded below", Hint = "A part under this share of its health sends the character to bed.", Min = 0.1, Max = 1, Step = 0.01)]
    public float WoundThreshold { get; set; } = 0.9f;

    [Setting("Wait for treatment", Hint = "Bleeding characters are not sent until bandaged (unbandaged cuts do not heal in bed).")]
    public bool WaitForTreatment { get; set; } = true;

    [Setting("Get up when healed", Hint = "Off: they stay in bed until you move them (the original's stayInBed only keeps them in until healed).")]
    public bool GetUp { get; set; } = true;
}

/// <summary>
/// Every 90th tick in the serial slow step: sends the wounded to bed and gets the healed ones this mod sent up. Which characters it sent is
/// runtime state (not saved): after a load they are not got up for you.
/// </summary>
sealed class SleepSystem(SleepSettings settings, PopulationData population) : ITickSystem
{
    /// <summary>Ticks before a character the bed search failed for is tried again.</summary>
    const long RetryTicks = 900;

    readonly HashSet<CharacterId> _sent = [];
    readonly Dictionary<CharacterId, long> _failed = [];

    public void SlowWorld(World world)
    {
        if (world.Tick % 90 != 0 || !settings.Enabled) return;

        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { IsPlayer: true, Medical: { Incapacitated: false } medical } cold) continue;

            var id = table.IdOf(slot);
            if (cold.InSomething == HeldState.Bed) GetUp(world, table, slot, id, medical);
            else if (Wounded(medical) && Free(table, slot, cold)) Send(world, slot, id, medical);
        }

        _sent.RemoveWhere(id => !table.IsAlive(id));
    }

    bool Wounded(MedicalState medical) => medical.Parts.Any(p => p.Limb == LimbState.Original && p.MaxHp > 0 && p.Fraction < settings.WoundThreshold);

    bool Healed(MedicalState medical) => medical.Parts.All(p => p.Limb != LimbState.Original || p.MaxHp <= 0 || p.Fraction >= settings.HealThreshold);

    /// <summary>Idle or on one of its jobs, not fighting, carrying or carried, not held in anything.</summary>
    static bool Free(CharacterTable table, int slot, CharacterCold cold) =>
        !cold.InCombat && cold.CarriedBy.IsNone && cold.Carrying.IsNone && cold.InSomething == HeldState.None
        && (table.Next[slot].Task == (byte)CharacterTask.Idle || cold.Running is { Source: TaskSource.Job });

    void Send(World world, int slot, CharacterId id, MedicalState medical)
    {
        if (settings.WaitForTreatment && medical.Bleeding > 0) return;
        if (_failed.TryGetValue(id, out long at) && world.Tick - at < RetryTicks) return;

        if (world.Fixtures.FindBed(world, slot, population.Relations) is not { } bed)
        {
            _failed[id] = world.Tick;
            return;
        }

        world.GiveOrder(id, TaskType.UseBed, site: bed.Id);
        _sent.Add(id);
        _failed.Remove(id);
    }

    void GetUp(World world, CharacterTable table, int slot, CharacterId id, MedicalState medical)
    {
        if (!settings.GetUp || !_sent.Contains(id) || !Healed(medical)) return;

        world.GiveOrder(id, TaskType.MoveCusOrdered, point: table.Next[slot].Position);
        _sent.Remove(id);
    }
}
