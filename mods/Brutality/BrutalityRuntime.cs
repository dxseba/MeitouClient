using System.Runtime.CompilerServices;
using Meitou.Simulation;

namespace Meitou.Brutality;

/// <summary>World-scoped access for native adapters; nothing is shared between a new game and a loaded world.</summary>
public static class BrutalityRuntime
{
    sealed class Control { public bool Physics; }
    static readonly ConditionalWeakTable<CharacterCold, Control> controls = new();

    public static BrutalityGameplaySystem? For(World world)
    {
        foreach (var item in world.Systems)
            if (item is BrutalityGameplaySystem system) return system;
        return null;
    }

    internal static bool ControlsPhysics(CharacterCold cold) => controls.TryGetValue(cold, out var value) && value.Physics;
    internal static void ControlPhysics(CharacterCold cold, bool enabled) => controls.GetValue(cold, static _ => new()).Physics = enabled;
}
