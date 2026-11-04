using HarmonyLib;
using Meitou.Simulation;
using Meitou.Simulation.Carrying;
using Meitou.Simulation.Items;

namespace Meitou.Brutality;

public sealed partial class BodyFragmentProxySystem
{
    static void InstallLootAttribution(Harmony harmony)
    {
        InstallLootPermissions(harmony);
        harmony.Patch(AccessTools.Method(typeof(CarrySystem), nameof(CarrySystem.Inputs)), postfix: new HarmonyMethod(typeof(BodyFragmentProxySystem), nameof(LootInputsDone)));
        harmony.Patch(AccessTools.Method(typeof(CarrySystem), nameof(CarrySystem.SlowWorld)),
            prefix: new HarmonyMethod(typeof(BodyFragmentProxySystem), nameof(LootSlowPrefix)), postfix: new HarmonyMethod(typeof(BodyFragmentProxySystem), nameof(LootSlowDone)));
        harmony.Patch(AccessTools.Method(typeof(World), nameof(World.ApplyPausedInterfaceCommands)), postfix: new HarmonyMethod(typeof(BodyFragmentProxySystem), nameof(PausedLootUpdated)));
    }
    static void LootInputsDone(World world, IReadOnlyList<SimCommand> commands) { if (commands.Count > 0) LootUpdated(world); }
    static void LootSlowPrefix(CarrySystem __instance, out int __state) => __state = __instance.Loots.Count;
    static void LootSlowDone(CarrySystem __instance, World world, int __state) { if (__instance.Loots.Count != __state) LootUpdated(world); }
    static void LootUpdated(World world) => ForWorld(world)?.NormalizeLootMarks(world);
    static void PausedLootUpdated(World __instance) => LootUpdated(__instance);
    void NormalizeLootMarks(World value)
    {
        if (halves.Count == 0) return;
        var aliases = new Dictionary<CharacterId, StolenMark>();
        var gameplay = BrutalityRuntime.For(value);
        foreach (var half in halves.Values)
        {
            CharacterId owner = half.Owner; CharacterCold source = half.Source;
            if (!value.Characters.TryResolveNext(owner, out int slot) || !ReferenceEquals(value.Characters.Cold(slot), source))
            {
                // Streaming may change slots. Only the original's full durable identity can identify a replacement.
                if (gameplay?.SaveKeyOf(source) is not { } key || gameplay.ResolveSaveKey(value, key) is not { } resolved
                    || !value.Characters.TryResolveNext(resolved, out slot) || value.Characters.Cold(slot) is not { } restored) continue;
                owner = resolved; source = restored;
            }
            aliases[half.Proxy] = source.IsPlayer ? StolenMark.None : StolenMark.Of(source, owner);
        }
        if (aliases.Count == 0) return;
        void Item(ItemInstance item)
        {
            if (aliases.TryGetValue(item.Owner, out var owner)) item.Mark = owner;
            if (item.Contents is { } bag) Inventory(bag, 0);
        }
        void Inventory(Inventory inventory, int depth)
        {
            if (depth > 4) return;
            foreach (var item in inventory.Items)
            {
                if (aliases.TryGetValue(item.Owner, out var owner)) item.Mark = owner;
                if (item.Contents is { } bag) Inventory(bag, depth + 1);
            }
        }
        for (int slot = 0; slot < value.Characters.HighWater; slot++)
            if (value.Characters.Cold(slot)?.Inventory is { } inventory) Inventory(inventory, 0);
        if (value.Player.Held is { } held) Item(held.Item);
        foreach (var ground in value.GroundItems.All) Item(ground.Item);
        foreach (var squad in value.Squads.All) if (squad.Shop is { } shop) Inventory(shop.Stock, 0);
        foreach (var fixture in value.Fixtures.All) if (fixture.Contents is { } inventory) Inventory(inventory, 0);
    }
}
