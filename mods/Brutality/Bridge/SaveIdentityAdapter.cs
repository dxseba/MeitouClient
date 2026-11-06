using System.Reflection;
using HarmonyLib;
using Meitou.Data.Save;
using Meitou.Brutality.Physics;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Saving;

namespace Meitou.Brutality;

/// <summary>Observes the handle allocated by the native writer; never guesses a character from its name or runtime slot.</summary>
public static class SaveIdentityAdapter
{
    public static void Install(ModContext context) => Install(context.Harmony);

    internal static void Install(Harmony harmony)
    {
        CorpseSaveAdapter.Install(harmony);
        var write = typeof(SaveCapture).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(method => method.Name == "WriteCharacter" && method.ReturnType == typeof(SaveCharacter)
                && method.GetParameters().Any(parameter => parameter.ParameterType == typeof(CharacterCold)));
        harmony.Patch(write, postfix: new HarmonyMethod(typeof(SaveIdentityAdapter), nameof(CharacterWritten)));
        harmony.Patch(AccessTools.Method(typeof(SaveGame), nameof(SaveGame.Load), [typeof(SaveFolder), typeof(Action<SaveCamera>)]),
            postfix: new HarmonyMethod(typeof(SaveIdentityAdapter), nameof(LegacyFilesLoaded)));
        harmony.Patch(AccessTools.Method(typeof(SaveGame), nameof(SaveGame.Clone)),
            postfix: new HarmonyMethod(typeof(SaveIdentityAdapter), nameof(LegacyFilesCopied)));
        harmony.Patch(AccessTools.Method(typeof(SaveLoader), nameof(SaveLoader.Load)),
            postfix: new HarmonyMethod(typeof(SaveIdentityAdapter), nameof(WorldLoaded)));
        harmony.Patch(AccessTools.Method(typeof(SaveCapture), nameof(SaveCapture.Capture)),
            prefix: new HarmonyMethod(typeof(SaveIdentityAdapter), nameof(BeforeSidecar)));
        harmony.Patch(AccessTools.Method(typeof(ModHost), nameof(ModHost.CaptureSaveData)),
            prefix: new HarmonyMethod(typeof(SaveIdentityAdapter), nameof(BeforeSidecar)));
        harmony.Patch(AccessTools.Method(typeof(ModHost), nameof(ModHost.RestoreSaveData)),
            prefix: new HarmonyMethod(typeof(SaveIdentityAdapter), nameof(BeforeSidecar)));
    }

    static void BeforeSidecar(World world)
    {
        BrutalityRuntime.For(world)?.BindWorld(world);
        foreach (var scene in world.Systems.OfType<FragmentPhysicsSystem>()) scene.BindSaveWorld(world);
        foreach (var proxies in world.Systems.OfType<BodyFragmentProxySystem>()) proxies.BindSaveWorld(world);
    }
    static void LegacyFilesLoaded(SaveFolder folder, SaveGame __result) => LegacyGoreSave.Loaded(folder, __result);
    static void LegacyFilesCopied(SaveGame __instance, SaveGame __result) => LegacyGoreSave.Copied(__instance, __result);
    static void WorldLoaded(World world, SaveGame save, PopulationData data, SaveLoadOptions? options, LoadedSave __result)
    {
        CorpseSaveAdapter.Materialize(world, save, data, options, __result);
        BrutalityRuntime.For(world)?.ImportLegacySave(world, save);
        foreach (var scene in world.Systems.OfType<FragmentPhysicsSystem>()) scene.ImportLegacySave(world, save);
    }

    static void CharacterWritten(World world, CharacterCold cold, CharacterId id, SaveCharacter __result)
    {
        BrutalityRuntime.For(world)?.CaptureSaveIdentity(world, cold, id, __result.Handle);
        CorpseSaveAdapter.CharacterWritten(world, cold, __result);
        foreach (var scene in world.Systems.OfType<FragmentPhysicsSystem>()) scene.CapturedSaveIdentity(cold);
        foreach (var proxies in world.Systems.OfType<BodyFragmentProxySystem>()) proxies.CapturedSaveIdentity(cold, __result.Handle);
    }

    internal static string? KeyFor(Hand handle) => handle.IsNull ? null
        : FormattableString.Invariant($"hand:v1:{handle.Type}:{handle.C}:{handle.CS}:{handle.I}:{handle.S}");
}
