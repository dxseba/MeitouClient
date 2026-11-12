using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Fcs;
using Meitou.Data.Save;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Items;
using Meitou.Simulation.Saving;

namespace Meitou.Brutality;

public sealed partial class CutEquipmentAdapter
{
    sealed class ItemIdentity(string key) { public string Key = key; }
    sealed class ItemOwner(WeaponBloodState state) { public WeaponBloodState State = state; }
    sealed class Capture(WeaponBloodState state) { public WeaponBloodState State = state; public readonly List<(SaveItem Saved, ItemInstance Item)> Items = []; }
    static readonly ConditionalWeakTable<FcsRecord, ItemIdentity> recordKeys = new();
    static readonly ConditionalWeakTable<ItemInstance, ItemIdentity> itemKeys = new();
    static readonly ConditionalWeakTable<ItemInstance, ItemOwner> itemOwners = new();
    [ThreadStatic] static Capture? capture;
    internal static string? ItemKey(ItemInstance item) => itemKeys.TryGetValue(item, out var assigned) ? assigned.Key
        : item.Saved is { } record && recordKeys.TryGetValue(record, out var loaded) ? loaded.Key : null;
    internal static void TrackItem(ItemInstance item, WeaponBloodState state) { itemOwners.Remove(item); itemOwners.Add(item, new(state)); }

    internal static void Install(Harmony harmony) => InstallPersistence(harmony);

    static void InstallPersistence(Harmony harmony)
    {
        var kit = typeof(SaveCapture).Assembly.GetType("Meitou.Simulation.Saving.SavedKit", throwOnError: true)!;
        InstallContainers(harmony, kit);
        harmony.Patch(AccessTools.Method(kit, "FromItem", [typeof(ItemInstance), typeof(string)]),
            postfix: new HarmonyMethod(typeof(CutEquipmentAdapter), nameof(ItemWritten)));
        harmony.Patch(AccessTools.Method(typeof(ItemInstance), nameof(ItemInstance.WithQuantity)),
            postfix: new HarmonyMethod(typeof(CutEquipmentAdapter), nameof(ItemCopied)));
        harmony.Patch(AccessTools.Method(typeof(SaveCapture), nameof(SaveCapture.Capture)),
            prefix: new HarmonyMethod(typeof(CutEquipmentAdapter), nameof(CapturePrefix)),
            postfix: new HarmonyMethod(typeof(CutEquipmentAdapter), nameof(CapturePostfix)),
            finalizer: new HarmonyMethod(typeof(CutEquipmentAdapter), nameof(CaptureFinalizer)));
        harmony.Patch(AccessTools.Method(typeof(SaveLoader), nameof(SaveLoader.Load)),
            prefix: new HarmonyMethod(typeof(CutEquipmentAdapter), nameof(LoadPrefix)));
        harmony.Patch(AccessTools.Method(typeof(SaveGame), nameof(SaveGame.Clone)),
            postfix: new HarmonyMethod(typeof(CutEquipmentAdapter), nameof(Cloned)));
    }
    static void CapturePrefix(World world, out Capture? __state)
    {
        __state = capture;
        var state = WeaponBloodState.For(world); state?.BindWorld(world);
        capture = state is not null ? new(state) : null;
    }
    static void ItemWritten(ItemInstance i, SaveItem __result) => capture?.Items.Add((__result, i));
    static void ItemCopied(ItemInstance __instance, ItemInstance __result)
    {
        if (itemOwners.TryGetValue(__instance, out var owner)) owner.State.Copy(__instance, __result);
    }
    static void CapturePostfix(SaveGame __result)
    {
        RegisterRecords(__result);
        if (capture is not { } current) return;
        foreach (var (saved, item) in current.Items)
        {
            if (saved.Written is not { } record || !recordKeys.TryGetValue(record, out var identity)) continue;
            current.State.TryGet(item, out _);
            BindItemIdentity(item, identity.Key);
            current.State.AssignIdentity(item, identity.Key);
        }
    }
    static Exception? CaptureFinalizer(Exception? __exception, Capture? __state) { capture = __state; return __exception; }
    static void LoadPrefix(World world, SaveGame save)
    {
        RegisterRecords(save);
        if (WeaponBloodState.For(world) is not { } state) return;
        ulong seed = save.Camera.Record.Strings.GetValueOrDefault(SaveLoader.SeedKey) is { } text
            && ulong.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var savedSeed) ? savedSeed : world.Seed;
        state.ReserveContainerNamespaces(save); state.BindWorld(world, seed); state.ImportLegacySave(save);
    }
    static void Cloned(SaveGame __result) => RegisterRecords(__result);

    /// <summary>Records are file-local. A base item record or an inventory position is never an instance identity.</summary>
    internal static string? RecordKey(FcsRecord record) => recordKeys.TryGetValue(record, out var identity) ? identity.Key : null;
    internal static void RegisterRecords(SaveGame save)
    {
        foreach (var (name, file) in save.AllFiles())
            foreach (var record in file.Records)
            {
                if (record.RecordType is not (FcsRecordType.InventoryItemState or FcsRecordType.Item) || record.StringId.Length == 0) continue;
                string key = "item-record:v1:" + Uri.EscapeDataString(name.Replace('\\', '/')) + ":" + Uri.EscapeDataString(record.StringId);
                recordKeys.Remove(record); recordKeys.Add(record, new(key));
            }
    }
}
