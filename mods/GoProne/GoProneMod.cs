using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Player;

namespace Meitou.GoProne;

/// <summary>
/// Port of Go Prone (Genpretz, Nexus 1830), rebuilt from its description: Ctrl with the "Toggle prone" key (unbound by default, as the original's,
/// which suggests Ctrl+Z) lays the selected sneaking characters down into the engine's staying-low posture (stealth x2, sight x0.5, crawl speed),
/// or gets them up. The key without Ctrl does nothing.
/// </summary>
public sealed class GoProneMod : IMod
{
    public void Initialize(ModContext context)
    {
        context.AddSystem("prone", _ => new ProneSystem(context.Enqueue), SystemAnchor.End);
        context.AddCombatMove(ProneSystem.LyingDown);
        context.AddKeyAction("toggle", "Toggle prone (Ctrl)", Pressed);
        context.Log("Go Prone initialized");
    }

    static void Pressed(Game.Host.Input.ModKeyContext key)
    {
        if (key.Ctrl) key.Enqueue(new ToggleProneOrder(key.Selected) { Tick = key.Tick });
    }
}

/// <summary>The selected characters go prone, or get up when any of them already is (stop lying down when any of them is).</summary>
public sealed record ToggleProneOrder(IReadOnlyList<CharacterId> Characters) : SimCommand;

/// <summary>
/// Applies the toggles in the serial slow step: down plays <c>squat T</c> forwards (the mirror of the Stand Up command's backwards play) as a
/// combat move, then sets <c>MedicalState.StayingLow</c> (the posture a sneaking character wakes in; the engine clears it on a speed order,
/// sneak off, a leg down or a melee attack); up is the game's own Stand Up command.
/// </summary>
sealed class ProneSystem(Action<SimCommand> enqueue) : ITickSystem
{
    /// <summary>The crouch clip the Stand Up command plays backwards.</summary>
    const string LieDownClip = "squat T";

    /// <summary>How long the lying-down clip plays (1 s at 30 ticks a second; the original's timing is unknown).</summary>
    const long LieDownTicks = 30;

    readonly List<ToggleProneOrder> _orders = [];

    /// <summary>Characters lying down and the tick they started; written only in the serial slow step, read by the combat move source.</summary>
    readonly Dictionary<CharacterId, long> _lowering = [];

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands)
        {
            if (command is ToggleProneOrder order) _orders.Add(order);
        }
    }

    public void SlowWorld(World world)
    {
        foreach (var order in _orders) Toggle(world, order.Characters);
        _orders.Clear();

        FinishLowering(world);
    }

    /// <summary>
    /// The combat move of a character lying down: <c>squat T</c> forwards over <see cref="LieDownTicks"/>. Reads only what the serial step
    /// wrote, so it is deterministic in the animation's parallel step.
    /// </summary>
    internal static CombatMove? LyingDown(World world, CharacterId id, long tick)
    {
        if (Of(world) is not { } system || !system._lowering.TryGetValue(id, out long start)) return null;

        float progress = (tick - start) / (float)LieDownTicks;
        return progress is >= 0 and < 1 ? CombatMove.Clip(LieDownClip, progress) : null;
    }

    static ProneSystem? Of(World world)
    {
        var systems = world.Systems;
        for (int i = 0; i < systems.Count; i++)
        {
            if (systems[i] is ProneSystem system) return system;
        }

        return null;
    }

    /// <summary>Sets the posture of each character whose clip ended, if it still may go prone.</summary>
    void FinishLowering(World world)
    {
        if (_lowering.Count == 0) return;

        var done = new List<CharacterId>();
        foreach (var (id, start) in _lowering)
        {
            if (world.Tick - start < LieDownTicks) continue;

            done.Add(id);
            if (Resolve(world, id) is { } cold && MayGoDown(cold)) cold.Medical!.StayingLow = true;
        }

        foreach (var id in done) _lowering.Remove(id);
    }

    static CharacterCold? Resolve(World world, CharacterId id)
    {
        var table = world.Characters;
        return table.TryResolveNext(id, out int s) && table.Cold(s) is { IsPlayer: true, Medical.Incapacitated: false } cold ? cold : null;
    }

    static bool MayGoDown(CharacterCold cold) =>
        Orders.Has(cold, CharacterOrders.Sneak) && !cold.InCombat && cold.CarriedBy.IsNone && cold.InSomething == Simulation.Locks.HeldState.None;

    void Toggle(World world, IReadOnlyList<CharacterId> ids)
    {
        var characters = new List<(CharacterId Id, CharacterCold Cold)>();
        foreach (var id in ids)
        {
            if (Resolve(world, id) is { } cold) characters.Add((id, cold));
        }

        var prone = characters.Where(c => c.Cold.Medical!.StayingLow).Select(c => c.Id).ToList();
        if (prone.Count > 0)
        {
            enqueue(new StandUpCommand(prone) { Tick = world.Tick + 1 });
            return;
        }

        if (characters.Count(c => _lowering.Remove(c.Id)) > 0) return;

        foreach (var (id, cold) in characters) GoDown(world, id, cold);
    }

    void GoDown(World world, CharacterId id, CharacterCold cold)
    {
        if (!Orders.Has(cold, CharacterOrders.Sneak))
        {
            world.Post($"{cold.Name} must be sneaking to go prone.");
            return;
        }

        if (MayGoDown(cold)) _lowering.TryAdd(id, world.Tick);
    }
}
