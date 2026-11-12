using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using HarmonyLib;
using Meitou.Data.Fcs;
using Meitou.Data.Save;
using Meitou.Modding;
using Meitou.Simulation.Items;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Saving;
using Meitou.Simulation.Trading;

namespace Meitou.Brutality;

/// <summary>Writes damage on the native item row, including the JSON rows used by stock shop and building inventories.</summary>
internal static class StockGarmentDamageSave
{
    const string RecordKey = "meitou.brutality garment damage v1", ContainerKey = "BrutalityGarmentDamageV1";
    static readonly JsonSerializerOptions json = new() { IncludeFields = true };
    static readonly ConditionalWeakTable<SaveItem, GarmentDamageMark[]> pending = new();
    static MethodInfo? keyOfContainer;

    sealed class Reading(JsonArray rows)
    {
        public readonly JsonArray Rows = rows;
        public Level? Current;
    }

    sealed class Level(JsonArray? rows)
    {
        public readonly JsonArray? Rows = rows;
        public int Index;
        public JsonArray? Children;
    }

    [ThreadStatic] static Reading? reading;

    internal static void Install(ModContext context)
    {
        try
        {
            var kit = typeof(SavedKit);
            var harmony = context.Harmony;
            harmony.Patch(AccessTools.Method(kit, "FromItem", [typeof(ItemInstance), typeof(string)]), postfix: Patch(nameof(CaptureItem)));
            harmony.Patch(AccessTools.Method(typeof(SaveInventory), "ItemRecord"), postfix: Patch(nameof(WriteItem)));
            harmony.Patch(AccessTools.Method(kit, "ItemOf", [typeof(SaveItem), typeof(ItemFactory)]), postfix: Patch(nameof(LoadItem)));
            harmony.Patch(AccessTools.Method(typeof(ItemInstance), nameof(ItemInstance.WithQuantity)), postfix: Patch(nameof(CopyItem)));
            InstallContainers(harmony, kit.Assembly);
        }
        catch (Exception error)
        {
            context.LogError($"Brutality garment damage persistence unavailable: {error.GetBaseException().Message}");
        }
    }

    static HarmonyMethod Patch(string name) => new(typeof(StockGarmentDamageSave), name);

    static void CaptureItem(ItemInstance i, SaveItem __result)
    {
        if (StockGarmentDamage.MarksOf(i) is { } marks) pending.Add(__result, marks);
    }

    static void WriteItem(SaveItem item, FcsRecord __result)
    {
        if (pending.TryGetValue(item, out var marks)) __result.Strings[RecordKey] = Encode(marks);
    }

    static void CopyItem(ItemInstance __instance, ItemInstance __result)
    {
        if (!ReferenceEquals(__instance, __result) && StockGarmentDamage.MarksOf(__instance) is { } marks)
            StockGarmentDamage.Restore(__result, marks);
    }

    static void LoadItem(SaveItem s, ItemInstance? __result)
    {
        string? container = ReadContainerItem(s);
        if (__result is not { Type: FcsRecordType.Armour }) return;
        string? stored = container ?? s.Source?.Strings.GetValueOrDefault(RecordKey);
        if (stored is not { Length: > 0 and <= 16_384 }) return;
        try
        {
            if (JsonSerializer.Deserialize<GarmentDamageMark[]>(stored, json) is { } marks)
                StockGarmentDamage.Restore(__result, marks);
        }
        catch (JsonException) { }
    }

    static string Encode(GarmentDamageMark[] marks) => JsonSerializer.Serialize(marks, json);

    static void InstallContainers(Harmony harmony, Assembly simulation)
    {
        var container = simulation.GetType("Meitou.Simulation.Locks.ContainerSave", throwOnError: true)!;
        keyOfContainer = AccessTools.Method(container, "KeyOf");
        harmony.Patch(AccessTools.Method(container, "Write"), postfix: Patch(nameof(ContainerWritten)));
        // The two carriers also cover a native Write call inlined before code mods were installed.
        harmony.Patch(AccessTools.Method(typeof(FixtureRegistry), nameof(FixtureRegistry.WriteBuilding)), postfix: Patch(nameof(BuildingWritten)));
        harmony.Patch(AccessTools.Method(simulation.GetType("Meitou.Simulation.Saving.ShopSave", throwOnError: true)!, "Write"),
            postfix: Patch(nameof(ShopWritten)));
        harmony.Patch(AccessTools.Method(container, "Read"), prefix: Patch(nameof(ReadPrefix)), finalizer: Patch(nameof(ReadFinalizer)));
        harmony.Patch(AccessTools.Method(container, "Fill"), prefix: Patch(nameof(FillPrefix)), finalizer: Patch(nameof(FillFinalizer)));
    }

    static void BuildingWritten(FixtureRegistry __instance, string buildingInstance, FcsRecord state, bool __result)
    {
        if (!__result) return;
        foreach (var fixture in __instance.OfBuilding(buildingInstance))
            if (fixture.Contents is { } inventory && keyOfContainer?.Invoke(null, [fixture, buildingInstance]) is string key)
                TagCarrier(inventory, state, key);
    }

    static void ShopWritten(FcsRecord platoon, Shop? shop)
    {
        if (shop is not null) TagCarrier(shop.Stock, platoon, "meitou shop");
    }

    static void TagCarrier(Inventory inventory, FcsRecord carrier, string key)
    {
        if (!carrier.Strings.TryGetValue(key, out string? value)) return;
        ContainerWritten(inventory, ref value);
        if (value is not null) carrier.Strings[key] = value;
    }

    static void ContainerWritten(Inventory grid, ref string? __result)
    {
        if (__result is null || !ContainsDamage(grid, 0)) return;
        try
        {
            if (JsonNode.Parse(__result) is JsonArray entries && Tag(entries, grid, 0)) __result = entries.ToJsonString();
        }
        catch (JsonException) { }
    }

    static bool ContainsDamage(Inventory grid, int depth)
    {
        if (depth > 32) return false;
        foreach (var item in grid.Items)
            if (StockGarmentDamage.MarksOf(item) is not null || item.Contents is { } bag && ContainsDamage(bag, depth + 1))
                return true;
        return false;
    }

    static bool Tag(JsonArray entries, Inventory grid, int depth)
    {
        if (depth > 32 || entries.Count != grid.Items.Count) return false;
        bool changed = false;
        for (int i = 0; i < entries.Count; i++)
        {
            var item = grid.Items[i];
            if (entries[i] is not JsonObject row || row["R"] is not JsonValue record
                || !record.TryGetValue<string>(out var id) || id != item.Record) continue;
            if (StockGarmentDamage.MarksOf(item) is { } marks)
            {
                row[ContainerKey] = Encode(marks);
                changed = true;
            }
            if (row["I"] is JsonArray inner && item.Contents is { } bag) changed |= Tag(inner, bag, depth + 1);
        }
        return changed;
    }

    static void ReadPrefix(string text, out Reading? __state)
    {
        __state = reading;
        reading = null;
        if (!text.Contains(ContainerKey, StringComparison.Ordinal)) return;
        try
        {
            if (JsonNode.Parse(text) is JsonArray rows) reading = new(rows);
        }
        catch (JsonException) { }
    }

    static Exception? ReadFinalizer(Exception? __exception, Reading? __state)
    {
        reading = __state;
        return __exception;
    }

    static void FillPrefix(object[] __args, out Level? __state)
    {
        __state = reading?.Current;
        if (reading is not { } scope) return;
        var rows = __state is null ? scope.Rows : __state.Children;
        scope.Current = new(rows is not null && __args[0] is IList actual && rows.Count == actual.Count ? rows : null);
    }

    static Exception? FillFinalizer(Exception? __exception, Level? __state)
    {
        if (reading is { } scope) scope.Current = __state;
        return __exception;
    }

    static string? ReadContainerItem(SaveItem item)
    {
        if (reading?.Current is not { } level) return null;
        int index = level.Index++;
        level.Children = null;
        if (level.Rows is not { } rows || (uint)index >= (uint)rows.Count || rows[index] is not JsonObject row
            || row["R"] is not JsonValue record || !record.TryGetValue<string>(out var id) || id != item.BaseId) return null;
        level.Children = row["I"] as JsonArray;
        return row[ContainerKey] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }
}
