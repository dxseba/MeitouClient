using System.Collections.Frozen;
using System.Numerics;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Population;

namespace Meitou.QueenBirthing;

/// <summary>
/// Runs the births in the serial slow step: the queen pays the hunger, the drone is spawned into her squad on a walkable point beside her
/// (<c>PopulationSystem.SpawnIntoPlayerSquad</c>) and knocked out. Publishes the player's queens every 30th tick for the menu.
/// </summary>
sealed class BirthSystem(BirthSettings settings, string? droneRecord) : ITickSystem
{
    static readonly float[] Rings = [12, 20, 30, 45];

    readonly List<GiveBirthOrder> _orders = [];

    /// <summary>The player's living queens; null before the first publish.</summary>
    public static volatile FrozenSet<CharacterId>? Queens;

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands)
        {
            if (command is GiveBirthOrder order) _orders.Add(order);
        }
    }

    public void SlowWorld(World world)
    {
        foreach (var order in _orders) Birth(world, order.Queen);
        _orders.Clear();

        if (world.Tick % 30 == 0) Queens = FindQueens(world);
    }

    static FrozenSet<CharacterId> FindQueens(World world)
    {
        var table = world.Characters;
        var queens = new HashSet<CharacterId>();
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (table.Next[slot].Alive && table.Cold(slot) is { IsPlayer: true } cold && QueenBirthingMod.IsQueen(cold.Race?.Name))
                queens.Add(table.IdOf(slot));
        }

        return queens.ToFrozenSet();
    }

    void Birth(World world, CharacterId queenId)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(queenId, out int slot)) return;
        if (table.Cold(slot) is not { IsPlayer: true, Medical: { Incapacitated: false } medical } queen) return;
        if (droneRecord is null)
        {
            world.Post("No Hive drone record was found in the game data.");
            return;
        }

        float cost = settings.HungerCost / 100f;
        if (medical.Hunger - cost < 1)
        {
            world.Post($"{queen.Name} is too hungry to give birth.");
            return;
        }

        if (Spawn(world, queen.SquadId, table.Next[slot].Position) is not { } drone)
        {
            world.Post($"{queen.Name} cannot give birth here.");
            return;
        }

        medical.Hunger -= cost;
        if (table.TryResolveNext(drone, out int d) && table.Cold(d)?.Medical is { } newborn) newborn.KnockOut(settings.KnockoutSeconds);
        world.Post($"{queen.Name} gave birth to a drone.");
    }

    /// <summary>The newborn on the first walkable point of rings round the queen; null when none takes it (or the squad is full).</summary>
    CharacterId? Spawn(World world, int squad, Vector3 at)
    {
        if (world.Systems.OfType<PopulationSystem>().FirstOrDefault() is not { } population) return null;

        foreach (float radius in Rings)
        {
            for (int k = 0; k < 8; k++)
            {
                float angle = k * MathF.PI / 4;
                var point = new Vector2(at.X + radius * MathF.Cos(angle), at.Z + radius * MathF.Sin(angle));
                var id = population.SpawnIntoPlayerSquad(world, squad, droneRecord!, point);
                if (id != CharacterId.None) return id;
            }
        }

        return null;
    }
}
