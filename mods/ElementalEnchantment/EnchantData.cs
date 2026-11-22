using System.Collections.Frozen;
using System.Text.Json;

namespace Meitou.ElementalEnchantment;

/// <summary>
/// The record lists the port does not ship, read once at start from optional files in its own mod folder: <c>bosses.json</c> (CHARACTER records
/// that fight enchanted, with their main element, secondary and level) and <c>items.json</c> (the ward ITEMs and their element, and the aptitude
/// recalibrator ITEM). A missing or invalid file means none of its entries, logged once.
/// </summary>
static class EnchantData
{
    /// <summary>
    /// The enchanted bosses by CHARACTER record id.
    /// </summary>
    public static FrozenDictionary<string, (Element Main, Element Second, int Level)> Bosses { get; private set; } =
        FrozenDictionary<string, (Element, Element, int)>.Empty;

    /// <summary>
    /// The wards by ITEM record id, and the element each holds.
    /// </summary>
    public static FrozenDictionary<string, Element> Wards { get; private set; } = FrozenDictionary<string, Element>.Empty;

    /// <summary>
    /// The aptitude recalibrator's ITEM record id; null without one.
    /// </summary>
    public static string? Recalibrator { get; private set; }

    /// <summary>
    /// Reads both files from <paramref name="folder"/>.
    /// </summary>
    public static void Load(string folder, Action<string> log)
    {
        Bosses = Read(Path.Combine(folder, "bosses.json"), log, ReadBosses) ?? FrozenDictionary<string, (Element, Element, int)>.Empty;
        var items = Read(Path.Combine(folder, "items.json"), log, ReadItems);
        Wards = items?.Wards ?? FrozenDictionary<string, Element>.Empty;
        Recalibrator = items?.Recalibrator;
        log($"enchant data: {Bosses.Count} bosses, {Wards.Count} wards, recalibrator {(Recalibrator is null ? "none" : "set")}");
    }

    /// <summary>
    /// One file parsed by <paramref name="parse"/>; null (logged) when absent or invalid.
    /// </summary>
    static T? Read<T>(string path, Action<string> log, Func<JsonElement, T> parse) where T : class
    {
        if (!File.Exists(path))
        {
            log($"{Path.GetFileName(path)} not found: none of its entries");
            return null;
        }

        try
        {
            var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
            using var doc = JsonDocument.Parse(File.ReadAllText(path), options);
            return parse(doc.RootElement);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException)
        {
            log($"{Path.GetFileName(path)} invalid, ignored: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// <c>{ "record id": ["Fire", "Lightning", 80], ... }</c>: main, secondary (or "None"), level 0 to 100.
    /// </summary>
    static FrozenDictionary<string, (Element, Element, int)> ReadBosses(JsonElement root)
    {
        var map = new Dictionary<string, (Element, Element, int)>(StringComparer.Ordinal);
        foreach (var entry in root.EnumerateObject())
        {
            var row = entry.Value;
            if (row.GetArrayLength() != 3) throw new FormatException($"boss {entry.Name}: want [main, secondary, level]");

            map[entry.Name] = (ElementOf(row[0]), ElementOf(row[1]), Math.Clamp(row[2].GetInt32(), 0, Elements.MaxLevel));
        }

        return map.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>
    /// <c>{ "wards": { "record id": "Fire", ... }, "recalibrator": "record id" }</c>; both optional.
    /// </summary>
    static ItemsFile ReadItems(JsonElement root)
    {
        var wards = new Dictionary<string, Element>(StringComparer.Ordinal);
        if (root.TryGetProperty("wards", out var list))
        {
            foreach (var entry in list.EnumerateObject()) wards[entry.Name] = ElementOf(entry.Value);
        }

        string? recalibrator = root.TryGetProperty("recalibrator", out var r) && r.GetString() is { Length: > 0 } id ? id : null;
        return new(wards.ToFrozenDictionary(StringComparer.Ordinal), recalibrator);
    }

    /// <summary>
    /// An element by name (case ignored).
    /// </summary>
    static Element ElementOf(JsonElement value) =>
        Enum.TryParse<Element>(value.GetString(), ignoreCase: true, out var e) && Enum.IsDefined(e)
            ? e
            : throw new FormatException($"unknown element {value}");

    /// <summary>
    /// <c>items.json</c> read.
    /// </summary>
    sealed record ItemsFile(FrozenDictionary<string, Element> Wards, string? Recalibrator);
}
