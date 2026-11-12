using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meitou.Data.Fcs;
using Meitou.Data.Save;
using Meitou.Simulation.Items;

namespace Meitou.Brutality;

public sealed partial class WeaponBloodState
{
    sealed class LegacyBlood(Vector4 value) { public readonly Vector4 Value = value; }
    static readonly ConditionalWeakTable<ItemInstance, LegacyBlood> legacyBlood = new();
    bool authoritativeSidecar;
    internal static void CaptureLegacyContainerBlood(ItemInstance item, JsonObject row)
    {
        if (row["B"] is not JsonArray { Count: 4 } values) return;
        var blood = Vector4.Zero;
        for (int i = 0; i < 4; i++)
            if (values[i] is JsonValue value && value.TryGetValue<float>(out float component)) blood[i] = component;
            else return;
        if (CleanLegacy(blood) is not { W: > 0 } valid) return;
        legacyBlood.Remove(item); legacyBlood.Add(item, new(valid));
    }
    static Vector4 CleanLegacy(Vector4 blood) => Finite(new(blood.X, blood.Y, blood.Z)) && float.IsFinite(blood.W)
        ? Vector4.Clamp(blood, Vector4.Zero, Vector4.One) : default;
    void ImportLegacyItem(ItemInstance item)
    {
        if (authoritativeSidecar || item.Type != FcsRecordType.Weapon || stains.ContainsKey(item)) return;
        var colour = legacyBlood.TryGetValue(item, out var container) ? container.Value
            : item.Saved?.Vector4S.GetValueOrDefault("meitou weapon blood") ?? default;
        var blood = CleanLegacy(colour); if (blood.W <= 0) return;
        stains[item] = new() { Blood = blood, Revision = ++revision }; dirty = true; CutEquipmentAdapter.TrackItem(item, this);
    }
    internal void ImportLegacySave(SaveGame save)
    {
        if (authoritativeSidecar) return;
        foreach (var (_, file) in save.AllFiles())
            foreach (var record in file.Records)
            {
                if (record.RecordType is FcsRecordType.InventoryItemState or FcsRecordType.Item
                    && record.Vector4S.TryGetValue("meitou weapon blood", out var value)
                    && CleanLegacy(value) is { W: > 0 } blood && CutEquipmentAdapter.RecordKey(record) is { } key) durable.TryAdd(key, blood);
                // Migrate the actual saved entries too, before lazy NPC/container materialization can discard the pin's unknown B field.
                foreach (var (name, text) in record.Strings.ToArray())
                {
                    if (name != "meitou shop" && !name.StartsWith("meitou container ", StringComparison.Ordinal)) continue;
                    try
                    {
                        if (JsonNode.Parse(text) is JsonArray entries && LegacyEntries(entries, 0)) record.Strings[name] = entries.ToJsonString();
                    }
                    catch (JsonException) { }
                }
            }
    }
    bool LegacyEntries(JsonArray entries, int depth)
    {
        if (depth > 32) return false;
        bool changed = false;
        foreach (var entry in entries)
        {
            if (entry is not JsonObject row) continue;
            if (row["B"] is JsonArray { Count: 4 } values)
            {
                var blood = Vector4.Zero; bool valid = true;
                for (int i = 0; i < 4; i++)
                    if (values[i] is JsonValue component && component.TryGetValue<float>(out float value)) blood[i] = value;
                    else valid = false;
                if (valid && CleanLegacy(blood) is { W: > 0 } clean)
                {
                    string? key = row[CutEquipmentAdapter.ContainerTokenProperty] is JsonValue token && token.TryGetValue<string>(out var saved) && IsContainerToken(saved) ? saved : null;
                    if (key is null)
                    {
                        if (containerSerial == long.MaxValue) continue;
                        key = ContainerTokenPrefix + containerNamespace + ":" + (++containerSerial).ToString(System.Globalization.CultureInfo.InvariantCulture);
                        row[CutEquipmentAdapter.ContainerTokenProperty] = key; changed = true;
                    }
                    durable.TryAdd(key, clean); ObserveContainerSerial(key);
                }
            }
            if (row["I"] is JsonArray children) changed |= LegacyEntries(children, depth + 1);
        }
        return changed;
    }
}
