using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Player;

namespace Meitou.GoProne;

/// <summary>
/// Port of Go Prone (Genpretz, Nexus 1830): the "Toggle prone" key (unbound by default, as the original's) lays the selected player characters
/// down into the staying-low posture and turns their sneaking on, or, when the selection is already staying low, sets them back to standing
/// (still sneaking). The behaviour follows the original's DLL (README "Facts").
/// </summary>
public sealed class GoProneMod : IMod
{
    public void Initialize(ModContext context)
    {
        context.AddSystem("prone", _ => new ProneSystem(context.Enqueue), SystemAnchor.End);
        context.AddKeyAction("toggle", "Toggle prone", Pressed);
        context.Log("Go Prone initialized");
    }

    static void Pressed(Game.Host.Input.ModKeyContext key) => key.Enqueue(new ToggleProneOrder(key.Selected) { Tick = key.Tick });
}

/// <summary>The selected characters go prone, or stand when any of them is staying low.</summary>
public sealed record ToggleProneOrder(IReadOnlyList<CharacterId> Characters) : SimCommand;

/// <summary>
/// Applies the toggles in the serial slow step, as the original's per-character step does: characters crawling with a leg down, playing dead or
/// out cold are skipped; the others get the staying-low posture (down) or lose it (up), and sneaking is turned on either way.
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
        var characters = new List<(CharacterId Id, CharacterCold Cold)>();
        foreach (var id in ids)
        {
            if (Resolve(world, id) is { } cold) characters.Add((id, cold));
        }

        if (characters.Count == 0) return;

        // The original asks whether the selection is laying low; here that is any of the characters it acts on.
        bool down = !characters.Any(c => c.Cold.Medical!.StayingLow);
        foreach (var (_, cold) in characters) cold.Medical!.StayingLow = down;

        var notSneaking = characters.Where(c => !Orders.Has(c.Cold, CharacterOrders.Sneak)).Select(c => c.Id).ToList();
        if (notSneaking.Count > 0) enqueue(new OrdersCommand(notSneaking, CharacterOrders.Sneak, On: true) { Tick = world.Tick + 1 });
    }

    /// <summary>
    /// A player character at posture 0 or 1 (not crawling, playing dead or out cold); not carried or in a bed, cage or chains (engine choice).
    /// </summary>
    static CharacterCold? Resolve(World world, CharacterId id)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(id, out int s) || table.Cold(s) is not { IsPlayer: true, Medical: { } medical } cold) return null;

        if (medical.Incapacitated || medical.Crawling || medical.PlayingDead) return null;

        return cold.CarriedBy.IsNone && cold.InSomething == Simulation.Locks.HeldState.None ? cold : null;
    }
}
