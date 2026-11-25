using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json;

namespace Meitou.Transmog;

/// <summary>An outfit as JSON: in the save's mod data and in the presets file.</summary>
static class OutfitJson
{
    public static void Write(Utf8JsonWriter json, Outfit outfit)
    {
        json.WriteBoolean("enabled", outfit.On);
        json.WriteBoolean("weapons_enabled", outfit.WeaponsOn);
        json.WriteStartObject("slots");
        foreach (var (slot, layers) in outfit.Slots.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            json.WriteStartArray(slot);
            foreach (var layer in layers) WriteLayer(json, layer);
            json.WriteEndArray();
        }

        json.WriteEndObject();
    }

    static void WriteLayer(Utf8JsonWriter json, Layer layer)
    {
        json.WriteStartObject();
        json.WriteString("item", layer.Item);
        json.WriteNumber("dye1", layer.Dye1);
        json.WriteNumber("dye2", layer.Dye2);
        json.WriteStartArray("offset");
        foreach (float v in (float[])[layer.Position.X, layer.Position.Y, layer.Position.Z, layer.Rotation.X, layer.Rotation.Y, layer.Rotation.Z,
                     layer.Scale])
        {
            json.WriteNumberValue(v);
        }

        json.WriteEndArray();
        if (!layer.Rendered) json.WriteBoolean("rendered", false);
        if (!layer.OriginalFinish) json.WriteBoolean("original_finish", false);
        if (layer.Finish.Length > 0) json.WriteString("finish", layer.Finish);
        json.WriteEndObject();
    }

    public static Outfit Read(JsonElement c)
    {
        var outfit = Outfit.Empty with
        {
            On = Bool(c, "enabled", false),
            WeaponsOn = Bool(c, "weapons_enabled", false),
        };
        if (!c.TryGetProperty("slots", out var slots)) return outfit;

        foreach (var slot in slots.EnumerateObject())
        {
            var layers = slot.Value.EnumerateArray().Select(ReadLayer).Take(Outfit.MaxLayers).ToImmutableList();
            outfit = outfit.With(slot.Name, layers);
        }

        return outfit;
    }

    static bool Bool(JsonElement e, string name, bool fallback) =>
        e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : fallback;

    static Layer ReadLayer(JsonElement l)
    {
        float[] o = l.TryGetProperty("offset", out var offset) ? [.. offset.EnumerateArray().Select(v => v.GetSingle())] : [];
        float At(int i, float fallback) => i < o.Length ? o[i] : fallback;
        return new Layer(l.GetProperty("item").GetString() ?? "")
        {
            Dye1 = l.TryGetProperty("dye1", out var d1) ? d1.GetInt32() : -1,
            Dye2 = l.TryGetProperty("dye2", out var d2) ? d2.GetInt32() : -1,
            Position = new Vector3(At(0, 0), At(1, 0), At(2, 0)),
            Rotation = new Vector3(At(3, 0), At(4, 0), At(5, 0)),
            Scale = At(6, 1),
            Rendered = Bool(l, "rendered", true),
            OriginalFinish = Bool(l, "original_finish", true),
            Finish = l.TryGetProperty("finish", out var f) ? f.GetString() ?? "" : "",
        };
    }
}
