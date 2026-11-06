using HarmonyLib;
using Meitou.Brutality.Physics;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.Brutality;

/// <summary>Lets a supported custom limb replace the native loose item for the same medical loss.</summary>
internal static class LimbDropAdapter
{
    readonly record struct NativeFallback(BrutalityGameplaySystem Gameplay, CharacterId Owner, LimbCutState Cut, long Revision);

    internal static void Install(ModContext context) =>
        context.Harmony.Patch(AccessTools.Method(typeof(PopulationSystem), "DropSeveredLimb"),
            prefix: new HarmonyMethod(typeof(LimbDropAdapter), nameof(BeforeDrop)),
            postfix: new HarmonyMethod(typeof(LimbDropAdapter), nameof(AfterDrop)));

    static bool BeforeDrop(World world, int slot, CharacterCold cold, int limb, out NativeFallback? __state)
    {
        __state = null;
        if (limb is < 0 or > 3 || BrutalityRuntime.For(world) is not { } gameplay) return true;
        var owner = world.Characters.IdOf(slot);
        // Population consumes the medical event in Inputs after the hit's SlowWorld built the cut.
        if (gameplay.NativeLimbCut(owner, limb, world.Tick - 1) is not { } cut) return true;
        if (world.Systems.OfType<FragmentPhysicsSystem>().FirstOrDefault()?.OwnsLimbDrop(owner, cold, cut) == true) return false;
        __state = new(gameplay, owner, cut, world.GroundItems.Revision);
        return true;
    }

    static void AfterDrop(World world, NativeFallback? __state)
    {
        if (__state is { } fallback && fallback.Revision != world.GroundItems.Revision)
            fallback.Gameplay.KeepNativeLimb(fallback.Owner, fallback.Cut);
    }
}
