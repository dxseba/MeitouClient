using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using HarmonyLib;
using Meitou.Simulation.Items;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Trading;
using Meitou.Data.Fcs;
using Meitou.Data.Save;

namespace Meitou.Brutality;

public sealed partial class CutEquipmentAdapter
{
    internal const string ContainerTokenProperty = "BrutalityItem";
    sealed class ContainerRead(JsonArray entries)
    {
        public readonly JsonArray Entries = entries;
        public ContainerLevel? Level;
    }
    sealed class ContainerLevel(JsonArray? entries)
    {
        public readonly JsonArray? Entries = entries;
        public int Index;
        public JsonArray? Children;
    }
    [ThreadStatic] static ContainerRead? readingContainer;
    internal static bool HasContainerReadScope => readingContainer is not null;
    internal static void BindItemIdentity(ItemInstance item, string key) { itemKeys.Remove(item); itemKeys.Add(item, new(key)); }

    static void InstallContainers(Harmony harmony, Type kit)
    {
        var serializer = kit.Assembly.GetType("Meitou.Simulation.Locks.ContainerSave", throwOnError: true)!;
        containerKey = AccessTools.Method(serializer, "KeyOf");
        // These large carrier methods also cover a previously JIT-inlined ContainerSave.Write.
        harmony.Patch(AccessTools.Method(typeof(FixtureRegistry), nameof(FixtureRegistry.WriteBuilding)), postfix: new HarmonyMethod(typeof(CutEquipmentAdapter), nameof(BuildingContainersWritten)));
        harmony.Patch(AccessTools.Method(kit.Assembly.GetType("Meitou.Simulation.Saving.ShopSave", throwOnError: true)!, "Write"), postfix: new HarmonyMethod(typeof(CutEquipmentAdapter), nameof(ShopContainerWritten)));
        harmony.Patch(AccessTools.Method(serializer, "Write"), postfix: new HarmonyMethod(typeof(CutEquipmentAdapter), nameof(ContainerWritten)));
        harmony.Patch(AccessTools.Method(serializer, "Read"), prefix: new HarmonyMethod(typeof(CutEquipmentAdapter), nameof(ContainerReadPrefix)),
            finalizer: new HarmonyMethod(typeof(CutEquipmentAdapter), nameof(ContainerReadFinalizer)));
        harmony.Patch(AccessTools.Method(serializer, "Fill"), prefix: new HarmonyMethod(typeof(CutEquipmentAdapter), nameof(ContainerFillPrefix)),
            finalizer: new HarmonyMethod(typeof(CutEquipmentAdapter), nameof(ContainerFillFinalizer)));
        harmony.Patch(AccessTools.Method(kit, "ItemOf", [typeof(SaveItem), typeof(ItemFactory)]),
            postfix: new HarmonyMethod(typeof(CutEquipmentAdapter), nameof(ContainerItemLoaded)));
    }
    static System.Reflection.MethodInfo? containerKey;
    static void ShopContainerWritten(FcsRecord platoon, Shop? shop)
    {
        if (shop is null || !platoon.Strings.TryGetValue("meitou shop", out var text)) return;
        string? tagged = text; ContainerWritten(shop.Stock, ref tagged);
        if (tagged is not null) platoon.Strings["meitou shop"] = tagged;
    }
    static void BuildingContainersWritten(string buildingInstance, FcsRecord state, bool __result, FixtureRegistry __instance)
    {
        if (!__result) return;
        var fixtures = __instance.OfBuilding(buildingInstance);
        foreach (var fixture in fixtures)
            if (fixture.Contents is { } inventory && containerKey?.Invoke(null, [fixture, buildingInstance]) is string key && state.Strings.TryGetValue(key, out var text))
            {
                string? tagged = text; ContainerWritten(inventory, ref tagged);
                if (tagged is not null) state.Strings[key] = tagged;
            }
    }
    static void ContainerWritten(Inventory grid, ref string? __result)
    {
        if (__result is null) return;
        JsonArray? entries;
        try { entries = JsonNode.Parse(__result) as JsonArray; }
        catch (JsonException) { return; }
        if (entries is null || !Tag(entries, grid, 0)) return;
        __result = entries.ToJsonString();
    }
    static bool Tag(JsonArray entries, Inventory grid, int depth)
    {
        // Native Entries is exactly grid.Items.Select(...); reject a changed serializer contract rather than attach an identity to another item.
        if (depth > 32 || entries.Count != grid.Items.Count) return false;
        bool changed = false;
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i] is not JsonObject row) continue;
            var item = grid.Items[i];
            if (row["R"]?.GetValue<string>() != item.Record) continue;
            string? key = ItemKey(item);
            var state = capture?.State ?? (itemOwners.TryGetValue(item, out var owner) ? owner.State : null);
            if (state?.ContainerToken(item) is { } owned) key = owned;
            if (WeaponBloodState.IsContainerToken(key)) { row[ContainerTokenProperty] = key; changed = true; }
            if (row["I"] is JsonArray inner && item.Contents is { } bag) changed |= Tag(inner, bag, depth + 1);
        }
        return changed;
    }
    static void ContainerReadPrefix(string text, out ContainerRead? __state)
    {
        __state = readingContainer; readingContainer = null;
        try { if (JsonNode.Parse(text) is JsonArray entries) readingContainer = new(entries); }
        catch (JsonException) { }
    }
    static Exception? ContainerReadFinalizer(Exception? __exception, ContainerRead? __state) { readingContainer = __state; return __exception; }
    static void ContainerFillPrefix(object[] __args, out ContainerLevel? __state)
    {
        __state = readingContainer?.Level;
        if (readingContainer is not { } scope) return;
        var entries = __state is null ? scope.Entries : __state.Children;
        scope.Level = new(entries is not null && __args[0] is IList actual && actual.Count == entries.Count ? entries : null);
    }
    static Exception? ContainerFillFinalizer(Exception? __exception, ContainerLevel? __state)
    {
        if (readingContainer is { } scope) scope.Level = __state;
        return __exception;
    }
    static void ContainerItemLoaded(SaveItem s, ItemInstance? __result)
    {
        if (readingContainer?.Level is not { } level) return;
        int index = level.Index++; level.Children = null;
        if (level.Entries is not { } entries || (uint)index >= (uint)entries.Count || entries[index] is not JsonObject row
            || row["R"]?.GetValue<string>() != s.BaseId) return;
        level.Children = row["I"] as JsonArray;
        if (__result is not null) WeaponBloodState.CaptureLegacyContainerBlood(__result, row);
        if (__result is not null && row[ContainerTokenProperty] is JsonValue value && value.TryGetValue<string>(out var key) && WeaponBloodState.IsContainerToken(key))
            BindItemIdentity(__result, key!);
    }
}
