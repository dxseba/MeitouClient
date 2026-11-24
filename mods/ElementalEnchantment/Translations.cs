using System.Text;
using Meitou.Data.Fcs;
using Meitou.Modding;

namespace Meitou.ElementalEnchantment;

/// <summary>
/// The original's translations, read at start from the installed original (its required item); nothing of them is shipped.
/// <list type="bullet">
/// <item>The plugin's interface texts (<see cref="PluginTexts"/>, nine languages): the game language's ones are written as a gettext catalogue
/// in the user's temporary folder (<c>Meitou/&lt;mod id&gt;</c>, outside the mod and any source tree) at each start and added with
/// <see cref="ModContext.AddTranslations"/>, so the window, menu row, panel line and the settings whose English is the plugin's show
/// translated; composed texts go through <see cref="T"/>.</item>
/// <item>The localisation patch's data (<c>plugin/raid-language/&lt;locale&gt;.eepatch</c>, a data file in the <c>.mod</c> format that the
/// original's second plugin loads right after <c>Elemental Enchantment.mod</c> in the game's language): merged in the data phase
/// (<see cref="Patch"/>), only while that mod is loaded.</item>
/// </list>
/// </summary>
static class Translations
{
    const string Domain = "elemental_enchantment";
    const string MainMod = "Elemental Enchantment.mod";

    static ModContext? s_context;

    /// <summary>A text through the catalogues (the plugin's, then the game's); unchanged without them. Any thread.</summary>
    public static string T(string english) => s_context?.Tr(english) ?? english;

    /// <summary>Adds the plugin's texts in the game's language (in <c>Initialize</c>).</summary>
    public static void Add(ModContext context)
    {
        s_context = context;
        if (context.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return;

        if (Texts(context) is not { Count: > 0 } texts)
        {
            context.Log($"no plugin texts in {context.Language}: the interface stays in English");
            return;
        }

        string root = Path.Combine(Path.GetTempPath(), "Meitou", context.Id);
        try
        {
            Write(Path.Combine(root, "locale", context.Language, "LC_MESSAGES", Domain + ".po"), texts);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            context.Log($"plugin texts not written ({e.Message}): the interface stays in English");
            return;
        }

        if (context.AddTranslations(Domain, root)) context.Log($"{texts.Count} plugin texts in {context.Language}");
    }

    static Dictionary<string, string>? Texts(ModContext context)
    {
        if (OriginalPlugin.Find(context) is not { } path) return null;

        try
        {
            return PluginTexts.For(new PeImage(File.ReadAllBytes(path)), context.Language);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            context.Log($"plugin texts not read ({e.Message})");
            return null;
        }
    }

    /// <summary>The texts as a gettext catalogue (UTF-8, no plural forms).</summary>
    static void Write(string path, Dictionary<string, string> texts)
    {
        var po = new StringBuilder("msgid \"\"\nmsgstr \"Content-Type: text/plain; charset=UTF-8\\n\"\n");
        foreach (var (english, text) in texts.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            po.Append("\nmsgid \"").Append(Escape(english)).Append("\"\nmsgstr \"").Append(Escape(text)).Append("\"\n");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, po.ToString(), new UTF8Encoding(false));
    }

    static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");

    /// <summary>
    /// The data phase: the localisation patch of the game's language merged after the loaded data, when <c>Elemental Enchantment.mod</c> is
    /// loaded and the patch exists (as the original, which loads it only after that file); its records are applied as they are.
    /// </summary>
    public static void Patch(ModDataContext context)
    {
        if (!context.Data.Files.Any(f => string.Equals(Path.GetFileName(f), MainMod, StringComparison.OrdinalIgnoreCase))) return;

        if (PatchFile(context) is not { } path) return;

        FcsFile patch;
        try
        {
            patch = FcsReader.ReadFile(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FcsFormatException)
        {
            context.Log($"language patch not read ({e.Message})");
            return;
        }

        foreach (var record in patch.Records.Where(r => r.StringId.Length > 0)) context.Apply(record);
        context.Log($"language patch {Path.GetFileName(path)}: {patch.Records.Count} records");
    }

    static string? PatchFile(ModDataContext context)
    {
        if (OriginalPlugin.Find(context.Folder, context.Log) is not { } plugin) return null;

        string code = Localisation.Loc.Language.Code;
        string path = Path.Combine(Path.GetDirectoryName(plugin)!, "raid-language", code + ".eepatch");
        return File.Exists(path) ? path : null;
    }
}
