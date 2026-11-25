using System.Text.Json;
using Meitou.Content;
using Meitou.Modding;

namespace Meitou.Transmog;

/// <summary>
/// The installed original (Workshop item 3841482484, the port's required item): its Workshop folder beside this mod's, else the mod named
/// "GHOSTs Transmog" without code. Its interface texts (<c>locales/*.json</c>) and its button icon are read from there, never shipped with the
/// port.
/// </summary>
static class OriginalFiles
{
    const string OriginalItem = "3841482484";
    const string OriginalName = "GHOSTs Transmog";

    /// <summary>The original's folder; null (logged) when it is not installed.</summary>
    public static string? Find(ModContext context)
    {
        string own = Path.GetFullPath(context.Folder);
        string? found = Sibling(own) ?? Installed(context, own);
        if (found is null) context.Log($"the original mod (Workshop {OriginalItem}) is not installed: English texts and a text button");

        return found;
    }

    /// <summary>The original's inventory button picture; null without it.</summary>
    public static string? Icon(string? folder) =>
        folder is not null && Path.Combine(folder, "ghosts_transmog_icon.png") is var path && File.Exists(path) ? path : null;

    /// <summary>
    /// The original's texts in the game's language as English to translation pairs: each key's text in <c>locales/en_GB.json</c> and in the
    /// language's file (the same code, else the first of the same language part).
    /// </summary>
    public static void AddTranslations(ModContext context, string? folder)
    {
        if (folder is null || context.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return;

        try
        {
            string locales = Path.Combine(folder, "locales");
            if (Texts(Path.Combine(locales, "en_GB.json")) is not { } english || LanguageFile(locales, context.Language) is not { } file
                    || Texts(file) is not { } translated)
                return;

            var pairs = english.Where(p => translated.ContainsKey(p.Key) && p.Value.Length > 0)
                .Select(p => KeyValuePair.Create(p.Value, translated[p.Key])).DistinctBy(p => p.Key);
            context.AddTranslations(context.Language, pairs);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            context.Log($"the original's texts not read: {e.Message}");
        }
    }

    static string? LanguageFile(string locales, string language)
    {
        string exact = Path.Combine(locales, language + ".json");
        if (File.Exists(exact)) return exact;

        string part = language.Split('_')[0] + "_";
        return Directory.Exists(locales)
            ? Directory.EnumerateFiles(locales, "*.json").FirstOrDefault(f => Path.GetFileName(f).StartsWith(part, StringComparison.OrdinalIgnoreCase))
            : null;
    }

    static Dictionary<string, string>? Texts(string path) =>
        File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) : null;

    static string? Sibling(string own) =>
        Path.GetDirectoryName(own) is { } parent && Path.Combine(parent, OriginalItem) is var path && Directory.Exists(path) ? path : null;

    static string? Installed(ModContext context, string own)
    {
        try
        {
            if (GameInstall.Locate() is not { } install) return null;

            bool IsOriginal(ModInfo m) =>
                m.Name == OriginalName && !m.HasCode && !string.Equals(Path.GetFullPath(m.Folder), own, StringComparison.OrdinalIgnoreCase);

            return ModList.Discover(install).FirstOrDefault(IsOriginal)?.Folder;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            context.Log($"mod folders not read: {e.Message}");
            return null;
        }
    }
}
