using Meitou.Content;
using Meitou.Modding;

namespace Meitou.CharactersEmotions;

/// <summary>
/// The installed original (Workshop item 3791017374, the port's required item): its Workshop folder beside this mod's, else the mod named
/// "CharactersEmotions" without code. Its <c>Plugins</c> ini files and localization are read from there; nothing of it is shipped with the port.
/// </summary>
static class OriginalFiles
{
    const string OriginalItem = "3791017374";
    const string OriginalName = "CharactersEmotions";

    /// <summary>The original's <c>Plugins</c> folder; null when the original is not installed.</summary>
    public static string? Plugins(ModContext context)
    {
        string? folder = Folder(context);
        string? plugins = folder is null ? null : Path.Combine(folder, "Plugins");
        if (plugins is not null && Directory.Exists(plugins)) return plugins;

        context.Log($"the original mod (Workshop {OriginalItem}) is not installed: default expressions, no animation emotions, English labels");
        return null;
    }

    static string? Folder(ModContext context)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(context.Folder)) is { } parent && Directory.Exists(Path.Combine(parent, OriginalItem)))
            return Path.Combine(parent, OriginalItem);

        try
        {
            if (GameInstall.Locate() is not { } install) return null;

            string own = Path.GetFullPath(context.Folder);
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

    /// <summary>
    /// The original's menu texts in the game's language as English-to-translation pairs: each key of <c>localization/en_GB.txt</c> with the same
    /// key's text in <c>localization/&lt;language&gt;.txt</c> (the code, else the first file of the same language part).
    /// </summary>
    public static IEnumerable<KeyValuePair<string, string>> Translations(string plugins, string language)
    {
        string folder = Path.Combine(plugins, "localization");
        var english = Texts(Path.Combine(folder, "en_GB.txt"));
        string? file = Path.Combine(folder, language + ".txt");
        if (!File.Exists(file))
        {
            string part = language.Split('_')[0];
            file = Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder, part + "_*.txt").Order(StringComparer.OrdinalIgnoreCase).FirstOrDefault()
                : null;
        }

        var local = Texts(file);
        foreach (var (key, text) in english)
        {
            if (local.TryGetValue(key, out var translated) && translated != text) yield return new(text, translated);
        }
    }

    static Dictionary<string, string> Texts(string? path)
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        if (path is null || !File.Exists(path)) return texts;

        try
        {
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.TrimStart('﻿');
                int eq = line.IndexOf('=');
                if (line.StartsWith('#') || eq <= 0) continue;

                texts[line[..eq].Trim()] = line[(eq + 1)..].Replace("\\n", "\n");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        return texts;
    }
}
