using Meitou.Simulation.Population;
using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Simulation;

namespace Meitou.Disarm;

/// <summary>Runs <see cref="DisarmOrder"/> in the serial input step: checks reach and state again, then drops the weapons.</summary>
sealed class DisarmSystem(EquipmentRefresh refresh, RngPurpose purpose) : ITickSystem
{
    /// <summary>World units (decimetres) a disarmer may stand from the target.</summary>
    public const float Reach = 40;
    static readonly string[] WeaponSections = ["hip", "back"];

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands)
        {
            if (command is DisarmOrder order) Run(world, order);
        }
    }

    static bool Downed(World world, int slot) => world.Characters.Cold(slot)?.Medical is { Unconscious: true, Dead: false };

    void Run(World world, DisarmOrder order)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(order.Target, out int target) || !Downed(world, target) || table.Cold(target) is not { Inventory: { } inventory } cold) return;

        var at = table.Next[target].Position;
        bool inReach = order.Disarmers.Any(d => table.TryResolveNext(d, out int s) && table.Cold(s) is { IsPlayer: true }
            && Vector2.Distance(new(at.X, at.Z), new(table.Next[s].Position.X, table.Next[s].Position.Z)) <= Reach);
        if (!inReach)
        {
            world.Post("Move closer to disarm.");
            return;
        }

        var weapons = inventory.Items.Where(i => !i.LockedOn && WeaponSections.Contains(i.Section)).ToList();
        if (weapons.Count == 0)
        {
            world.Post($"{cold.Name} has no weapon to take.");
            return;
        }

        Drop(world, order.Target, at, inventory, weapons);
        cold.DrawnWeapon = Meitou.Data.Gameplay.WeaponKinds.None;
        refresh.Refresh(cold);
        world.Post($"{cold.Name} has been disarmed.");
    }

    void Drop(World world, CharacterId target, Vector3 at, Meitou.Simulation.Items.Inventory inventory, List<Meitou.Simulation.Items.ItemInstance> weapons)
    {
        ulong n = 0;
        foreach (var item in weapons)
        {
            inventory.Items.Remove(item);
            item.Section = "main";

            float angle = Rng.Float(Rng.Hash(world.Seed, Rng.Key(target), purpose, (ulong)world.Tick * 4 + n++)) * MathF.Tau;
            var spot = at + new Vector3(MathF.Sin(angle), 0, MathF.Cos(angle)) * 8;
            spot.Y = world.Walkability.GroundHeight(spot.X, spot.Z, at.Y);
            world.GroundItems.Drop(item, spot, angle);
        }
    }
}

/// <summary>The engine's private equipment refresh (fighter and look from the inventory), reached by reflection as the captive strip uses it.</summary>
sealed class EquipmentRefresh(object assembly, MethodInfo refresh)
{
    public static EquipmentRefresh Create(PopulationData data)
    {
        var type = AccessTools.TypeByName("Meitou.Simulation.Bodies.CharacterAssembly") ?? throw new TypeLoadException("Meitou.Simulation.Bodies.CharacterAssembly");
        var method = AccessTools.Method(type, "RefreshEquipment") ?? throw new MissingMethodException(type.FullName, "RefreshEquipment");
        return new(Activator.CreateInstance(type, data)!, method);
    }

    public void Refresh(CharacterCold cold) => refresh.Invoke(assembly, [cold]);
}
