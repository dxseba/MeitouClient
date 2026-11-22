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
        context.AddKeyAction("toggle", "Toggle prone (Ctrl)", Pressed);
        context.Log("Go Prone initialized");
    }

    static void Pressed(Game.Host.Input.ModKeyContext key)
    {
        if (key.Ctrl) key.Enqueue(new ToggleProneOrder(key.Selected) { Tick = key.Tick });
    }
}

/// <summary>The selected characters go prone, or get up when any of them already is.</summary>
public sealed record ToggleProneOrder(IReadOnlyList<CharacterId> Characters) : SimCommand;

/// <summary>
/// Applies the toggles in the serial slow step: down sets <c>MedicalState.StayingLow</c> (the posture a sneaking character wakes in; the engine
/// clears it on a speed order, sneak off, a leg down or a melee attack); up is the game's own Stand Up command.
/// </summary>
sealed class ProneSystem(Action<SimCommand> enqueue) : ITickSystem
{
    readonly List<ToggleProneOrder> _orders = [];

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
    }

    void Toggle(World world, IReadOnlyList<CharacterId> ids)
    {
        var table = world.Characters;
        var characters = new List<(CharacterId Id, CharacterCold Cold)>();
        foreach (var id in ids)
        {
            if (table.TryResolveNext(id, out int s) && table.Cold(s) is { IsPlayer: true, Medical.Incapacitated: false } cold)
                characters.Add((id, cold));
        }

        var prone = characters.Where(c => c.Cold.Medical!.StayingLow).Select(c => c.Id).ToList();
        if (prone.Count > 0)
        {
            enqueue(new StandUpCommand(prone) { Tick = world.Tick + 1 });
            return;
        }

        foreach (var (_, cold) in characters) GoDown(world, cold);
    }

    static void GoDown(World world, CharacterCold cold)
    {
        if (!Orders.Has(cold, CharacterOrders.Sneak))
        {
            world.Post($"{cold.Name} must be sneaking to go prone.");
            return;
        }

        if (cold.InCombat || !cold.CarriedBy.IsNone || cold.InSomething != Simulation.Locks.HeldState.None) return;

        cold.Medical!.StayingLow = true;
    }
}
