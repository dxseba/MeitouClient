using System.Collections.Frozen;
using System.Text.Json;
using Meitou.Data.Fcs;
using Meitou.Modding;

namespace Meitou.ElementalEnchantment;

/// <summary>
/// The record lists the port does not ship, read once at start: the bosses (CHARACTER records that fight enchanted, with their main element,
/// secondary and level), the ward ITEMs and their element, and the aptitude recalibrator ITEM. Each comes from an override file in the port's
/// own folder (<c>bosses.json</c>, <c>items.json</c>) when there is a valid one, else from the installed original's plugin
/// (<see cref="PluginTables"/>), else none; every fallback is logged once.
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
    /// Reads the override files from the mod's folder and, for what they do not give, the original's plugin.
    /// </summary>
    public static void Load(ModContext context)
    {
        var plugin = new Lazy<PeImage?>(() => OpenPlugin(context));
        FcsRecordType? TypeOf(string id) => context.Data.Find(id)?.Type;

        string bossSource = "bosses.json";
        var bosses = Read(Path.Combine(context.Folder, "bosses.json"), context.Log, ReadBosses);
        if (bosses is null)
        {
            bossSource = "plugin";
            bosses = FromPlugin(plugin, "bosses", context.Log, pe => PluginTables.Bosses(pe, TypeOf));
        }

        Bosses = bosses ?? FrozenDictionary<string, (Element, Element, int)>.Empty;

        string itemSource = "items.json";
        var items = Read(Path.Combine(context.Folder, "items.json"), context.Log, ReadItems);
        if (items is null)
        {
            itemSource = "plugin";
            items = ItemsFromPlugin(plugin, context.Log, TypeOf);
        }

        Wards = items.Wards;
        Recalibrator = items.Recalibrator;
        Summarise(context.Log, bossSource, itemSource);
    }

    static void Summarise(Action<string> log, string bossSource, string itemSource)
    {
        string recalibrator = Recalibrator is null ? "none" : $"set ({itemSource})";
        log($"enchant data: {Bosses.Count} bosses ({bossSource}), {Wards.Count} wards ({itemSource}), recalibrator {recalibrator}");
    }

    /// <summary>
    /// The wards and the recalibrator from the plugin, each none (logged) when it does not read.
    /// </summary>
    static ItemsFile ItemsFromPlugin(Lazy<PeImage?> plugin, Action<string> log, Func<string, FcsRecordType?> typeOf)
    {
        var wards = FromPlugin(plugin, "wards", log, pe => PluginTables.Wards(pe, typeOf));
        string? recalibrator = FromPlugin(plugin, "recalibrator", log, pe => PluginTables.Recalibrator(pe, typeOf));
        return new(wards ?? FrozenDictionary<string, Element>.Empty, recalibrator);
    }

    /// <summary>
    /// One part read from the plugin by <paramref name="read"/>; null (logged) without a plugin or when the part does not match.
    /// </summary>
    static T? FromPlugin<T>(Lazy<PeImage?> plugin, string what, Action<string> log, Func<PeImage, T> read) where T : class
    {
        if (plugin.Value is not { } pe) return null;

        try
        {
            return read(pe);
        }
        catch (FormatException e)
        {
            log($"original plugin: {what} not recognised ({e.Message}): none");
            return null;
        }
    }

    /// <summary>
    /// The original's plugin read as bytes; null (logged) when not installed or not a readable image.
    /// </summary>
    static PeImage? OpenPlugin(ModContext context)
    {
        if (OriginalPlugin.Find(context) is not { } path)
        {
            context.Log("the original mod (Workshop 3832978749) is not installed: no bosses, wards or recalibrator beyond the override files");
            return null;
        }

        try
        {
            return new PeImage(File.ReadAllBytes(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            context.Log($"original plugin not read ({e.Message}): none of its entries");
            return null;
        }
    }

    /// <summary>
    /// One override file parsed by <paramref name="parse"/>; null when absent or invalid (an invalid one logged).
    /// </summary>
    static T? Read<T>(string path, Action<string> log, Func<JsonElement, T> parse) where T : class
    {
        if (!File.Exists(path)) return null;

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
    /// <c>items.json</c> read, or the plugin's wards and recalibrator.
    /// </summary>
    sealed record ItemsFile(FrozenDictionary<string, Element> Wards, string? Recalibrator);
}
