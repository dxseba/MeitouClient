using HarmonyLib;
using Meitou.Simulation;

namespace Meitou.Brutality;

public sealed partial class BodyFragmentProxySystem
{
    [ThreadStatic] static World? corpseUpdate;
    internal static void InstallRetention(Harmony harmony)
    {
        InstallLootAttribution(harmony);
        harmony.Patch(AccessTools.Method(typeof(BodySystem), nameof(BodySystem.SlowWorld)),
            prefix: new HarmonyMethod(typeof(BodyFragmentProxySystem), nameof(CorpseUpdatePrefix)),
            finalizer: new HarmonyMethod(typeof(BodyFragmentProxySystem), nameof(CorpseUpdateFinalizer)));
        harmony.Patch(AccessTools.Method(typeof(CharacterTable), nameof(CharacterTable.Remove)),
            prefix: new HarmonyMethod(typeof(BodyFragmentProxySystem), nameof(CorpseRemovePrefix)));
    }
    static void CorpseUpdatePrefix(World world, out World? __state)
    { __state = corpseUpdate; corpseUpdate = ForWorld(world) is not null ? world : null; }
    static Exception? CorpseUpdateFinalizer(Exception? __exception, World? __state) { corpseUpdate = __state; return __exception; }
    static bool CorpseRemovePrefix(CharacterTable __instance, CharacterId id, ref bool __result)
    {
        if (corpseUpdate is not { } value || !ReferenceEquals(value.Characters, __instance)
            || !__instance.TryResolveNext(id, out int slot) || __instance.Cold(slot)?.Burst is not null
            || ForWorld(value) is not { } proxies || !proxies.KeepHeldHalf(value, id)) return true;
        __result = false; return false;
    }
    bool KeepHeldHalf(World value, CharacterId id)
    {
        foreach (var half in halves.Values)
        {
            if (id != half.Owner && id != half.Proxy) continue;
            if (!value.Characters.TryResolveNext(half.Proxy, out int proxy) || !ReferenceEquals(value.Characters.Cold(proxy), half.Cold)
                || !value.Characters.TryResolveNext(half.Cold.CarriedBy, out int carrier)) continue;
            if (value.Characters.Cold(carrier)?.Carrying == half.Proxy) return true;
        }
        return false;
    }
}
