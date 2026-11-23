using System.Numerics;
using System.Text.Json;

namespace Meitou.MapMarkers;

/// <summary>
/// The original's per-save file (<b>Verified</b>, its DLL strings and README): <c>&lt;save&gt;/Map-markers.json</c>,
/// <c>{"version": 3, "markers": [{"id", "x", "y", "type", "label"}]}</c>, x and y the 0..1 map point, the type a kind id (<c>note</c>,
/// <c>danger</c>, <c>stash</c>, <c>ruin</c>, <c>mine</c>, <c>base</c>, <c>trader</c>, <c>safe_spot</c>, <c>quest</c>, <c>todo</c>; the DLL also
/// holds <c>trader_shop</c> and <c>safe</c>, read here as aliases, and its loader takes integer types).
/// </summary>
static class MarkerFile
{
    public const string Name = "Map-markers.json";
    const int Version = 3;

    static readonly string[] Ids = ["note", "danger", "stash", "ruin", "mine", "base", "trader", "safe_spot", "quest", "todo"];

    /// <summary>The file's markers; null when there is none or it cannot be read (logged).</summary>
    public static List<Marker>? Read(string path, Action<string> log)
    {
        if (!File.Exists(path)) return null;

        try
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(path));
            var markers = json.RootElement.GetProperty("markers").EnumerateArray().Select(ReadMarker).ToList();
            log($"{markers.Count} markers read from {path}");
            return markers;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException
            or KeyNotFoundException or FormatException)
        {
            log($"{path} not read: {e.Message}");
            return null;
        }
    }

    static Marker ReadMarker(JsonElement m)
    {
        var at = Vector2.Clamp(new(m.GetProperty("x").GetSingle(), m.GetProperty("y").GetSingle()), Vector2.Zero, Vector2.One);
        string label = m.TryGetProperty("label", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString() ?? "" : "";
        return new Marker(m.GetProperty("id").GetInt32(), at, KindOf(m), MarkerKinds.Clean(label, trimEnd: true));
    }

    static MarkerKind KindOf(JsonElement m)
    {
        if (!m.TryGetProperty("type", out var type)) return MarkerKind.Note;
        if (type.ValueKind == JsonValueKind.Number) return MarkerKinds.FromIndex(type.GetInt32());

        string id = type.GetString() ?? "";
        int index = Array.IndexOf(Ids, id);
        return index >= 0 ? (MarkerKind)index : id switch { "trader_shop" => MarkerKind.Trader, "safe" => MarkerKind.SafeSpot, _ => MarkerKind.Note };
    }

    /// <summary>Writes <paramref name="markers"/> in the original's format (logged when it cannot).</summary>
    public static void Write(string path, IReadOnlyList<Marker> markers, Action<string> log)
    {
        try
        {
            using var stream = File.Create(path);
            using var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            json.WriteStartObject();
            json.WriteNumber("version", Version);
            json.WriteStartArray("markers");
            foreach (var marker in markers) WriteMarker(json, marker);

            json.WriteEndArray();
            json.WriteEndObject();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log($"{path} not written: {e.Message}");
        }
    }

    static void WriteMarker(Utf8JsonWriter json, Marker marker)
    {
        json.WriteStartObject();
        json.WriteNumber("id", marker.Id);
        json.WriteNumber("x", marker.At.X);
        json.WriteNumber("y", marker.At.Y);
        json.WriteString("type", Ids[(int)marker.Kind]);
        json.WriteString("label", marker.Label);
        json.WriteEndObject();
    }
}
