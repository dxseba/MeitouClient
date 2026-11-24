using System.Text;
using Meitou.Content;
using Meitou.Modding;

namespace Meitou.BodyMorphCap;

/// <summary>
/// The original's Russian settings labels, read at start from the installed original's <c>BodyMorphCap.dll</c> (Workshop 3800922376) and added
/// in memory as the mod's translations; nothing of the original ships with the port. In its read-only data each Russian text is the
/// NUL-terminated string stored just before its English one (README "Facts"); a pair is kept only when the text before holds Cyrillic letters.
/// </summary>
static class OriginalLabels
{
    const string OriginalItem = "3800922376";
    const string OriginalName = "BodyMorphCap";
    const string DllName = "BodyMorphCap.dll";
    const int MaxFileSize = 16 << 20;

    /// <summary>The original's English labels and hints the port's settings use.</summary>
    static readonly string[] Wanted =
    [
        "Turn the whole mod on/off.", "Enabled",
        "Body morph only: render the body as if Strength were at most this. Gameplay is unchanged.", "Strength cap (body)",
        "Body morph only: build also grows with Weapon Smithing. Render as if it were at most this.", "Weapon smith cap (body)",
        "Body morph only: build also grows with Armour Smithing. Render as if it were at most this.", "Armour smith cap (body)",
        "Body morph only: muscle definition (abs) grows with Dexterity/Martial arts/Swimming/Athletics. Render as if those were at most this.",
        "Muscle definition cap (body)", "Also cap the player's own body.", "Apply to player", "Debug log",
    ];

    /// <summary>
    /// Adds the Russian labels when <paramref name="language"/> asks for them: <c>ru</c> always, <c>auto</c> when the game is in Russian, <c>en</c>
    /// never (the original's <c>language</c> key). One log line when the original is missing or unreadable.
    /// </summary>
    public static void Add(ModContext context, string language)
    {
        bool gameRussian = context.Language.StartsWith("ru", StringComparison.OrdinalIgnoreCase);
        if (language == "en" || language != "ru" && !gameRussian) return;

        if (Locate(context) is not { } path)
        {
            context.Log($"the original {DllName} (Workshop {OriginalItem}) is not installed: labels stay English");
            return;
        }

        try
        {
            var texts = Read(path);
            context.AddTranslations(context.Language, texts);
            context.Log($"original Russian labels read: {texts.Count}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            context.Log($"original labels not read ({e.Message}): labels stay English");
        }
    }

    static Dictionary<string, string> Read(string path)
    {
        if (new FileInfo(path).Length > MaxFileSize) throw new InvalidDataException("file too large");

        byte[] bytes = File.ReadAllBytes(path);
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string english in Wanted)
        {
            if (RussianBefore(bytes, english) is { } russian) texts[english] = russian;
        }

        return texts.Count > 0 ? texts : throw new InvalidDataException("no labels found");
    }

    /// <summary>The string stored before <c>NUL english NUL</c>, past the padding NULs between them; null when it is not Russian text.</summary>
    static string? RussianBefore(byte[] bytes, string english)
    {
        byte[] needle = [0, .. Encoding.UTF8.GetBytes(english), 0];
        int at = bytes.AsSpan().IndexOf(needle);
        if (at <= 0) return null;

        int end = at;
        while (end > 0 && bytes[end - 1] == 0) end--;
        int start = end;
        while (start > 0 && bytes[start - 1] != 0 && end - start < 1024) start--;
        if (start == end) return null;

        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(bytes, start, end - start);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }

        return text.Any(c => c is >= 'Ѐ' and <= 'ӿ') ? text : null;
    }

    static string? Locate(ModContext context)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(context.Folder)) is { } parent && File.Exists(Path.Combine(parent, OriginalItem, DllName)))
            return Path.Combine(parent, OriginalItem, DllName);

        return Installed(context) is { } folder && File.Exists(Path.Combine(folder, DllName)) ? Path.Combine(folder, DllName) : null;
    }

    /// <summary>The installed mod named "BodyMorphCap" without Meitou code (the original), not the port's own folder.</summary>
    static string? Installed(ModContext context)
    {
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
}
