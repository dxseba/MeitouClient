using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Simulation;
using Meitou.Simulation.Crime;

namespace Meitou.Brutality;

public sealed partial class BodyFragmentProxySystem
{
    static readonly ConditionalWeakTable<CharacterCold, CharacterCold> lootOwners = new();
    static void RegisterLootOwner(CharacterCold proxy, CharacterCold owner) { lootOwners.Remove(proxy); lootOwners.Add(proxy, owner); }
    static CharacterCold CanonicalLootOwner(CharacterCold target) => lootOwners.TryGetValue(target, out var owner) ? owner : target;
    static void InstallLootPermissions(Harmony harmony)
    {
        foreach (string method in new[] { nameof(CrimeSystem.LootAllowed), nameof(CrimeSystem.IsLootTheft), nameof(CrimeSystem.MarksTheft) })
            harmony.Patch(AccessTools.Method(typeof(CrimeSystem), method), prefix: new HarmonyMethod(typeof(BodyFragmentProxySystem), nameof(PermissionVictim)));
        harmony.Patch(AccessTools.Method(typeof(CrimeSystem), nameof(CrimeSystem.TakeCode)), prefix: new HarmonyMethod(typeof(BodyFragmentProxySystem), nameof(PermissionOwner)));
        // Large entry points cover callers that JIT-inlined a small permission helper before the mod was installed.
        harmony.Patch(AccessTools.Method(typeof(CrimeSystem), nameof(CrimeSystem.Loot)), prefix: new HarmonyMethod(typeof(BodyFragmentProxySystem), nameof(CrimeLootPrefix)));
        harmony.Patch(AccessTools.Method(typeof(CrimeSystem), nameof(CrimeSystem.Pickpocket)), prefix: new HarmonyMethod(typeof(BodyFragmentProxySystem), nameof(CrimePickpocketPrefix)));
    }
    static void PermissionVictim(ref CharacterCold victim) => victim = CanonicalLootOwner(victim);
    static void PermissionOwner(ref CharacterCold owner) => owner = CanonicalLootOwner(owner);
    static bool CrimeLootPrefix(World world, int looter, int victim)
    {
        if (world.Characters.Cold(victim) is not { } target) return true;
        var owner = CanonicalLootOwner(target);
        return ReferenceEquals(owner, target) || world.Characters.Cold(looter) is not { } taker || CrimeSystem.IsLootTheft(world, taker, owner);
    }
    static bool CrimePickpocketPrefix(World world, int thief, int owner, CrimeSystem.TakeKind kind, ref bool __result)
    {
        if (kind == CrimeSystem.TakeKind.Give || world.Characters.Cold(owner) is not { } target) return true;
        var original = CanonicalLootOwner(target);
        if (ReferenceEquals(original, target) || world.Characters.Cold(thief) is not { } taker || CrimeSystem.TakeCode(world, taker, original, kind) is not null) return true;
        // A dead proxy has no watcher or sub-.98 steal roll: with no pending crime, its native result is simply false.
        __result = false; return false;
    }
}
