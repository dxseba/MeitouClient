using System.Text.Json;
using Meitou.Data.Fcs;
using Meitou.Modding;

namespace Meitou.BowMod;

/// <summary>
/// The original's translations (<c>Localization/&lt;language&gt;.json</c> of the installed original: <c>records</c>, a record id to its name and
/// description, and <c>ui</c>, the skill's texts by key), read at start; nothing of them is shipped. The game's language picks the file by its
/// first two letters (en, es, ja, ko, pt, ru, zh), as the original follows the game's language. Nothing is written.
/// </summary>
static class BowTexts
{
    /// <summary>The skill's texts by the original's key when its <c>en.json</c> is missing (the port's own wording).</summary>
    public static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>
    {
        ["BOW_LABEL"] = "Bows",
        ["BOW_DESCRIPTION"] = "Skill with bows, kept apart from Crossbows. Raises bow accuracy and drawing speed, slowly up to level 60 and "
            + "faster from 61.",
        ["ACCURACY"] = "-Accuracy:",
        ["RELOAD"] = "-Drawing / reload speed:",
        ["TRAIN_BOW"] = "-Attack targets with a bow",
        ["SHARED_PRECISION"] = "-Precision Shooting is shared with crossbows",
    };

    sealed record Catalogue(Dictionary<string, string[]> Records, Dictionary<string, string> Ui);

    /// <summary>
    /// The skill's texts by key in the game's language: the original's file of that language over its <c>en.json</c> over the port's wording.
    /// The stats window shows a code mod's pane texts as given, so they are taken in the game's language here.
    /// </summary>
    public static Dictionary<string, string> Ui(ModContext context)
    {
        var texts = English.ToDictionary();
        foreach (string language in new[] { "en", Language(context.Language) }.Distinct())
        {
            if (Read(context.Folder, language, context.Log) is not { } catalogue) continue;

            foreach (var (key, text) in catalogue.Ui) texts[key] = text;
        }

        return texts;
    }

    /// <summary>The data phase: the game language's record names and descriptions, on the records the loaded data has.</summary>
    public static void Rename(ModDataContext context)
    {
        if (Read(context.Folder, Language(Localisation.Loc.Language.Code), context.Log) is not { } catalogue) return;

        int renamed = 0;
        foreach (var (id, texts) in catalogue.Records)
        {
            if (texts.Length < 1 || context.Data.Find(id) is not { } record || texts[0].Length == 0) continue;

            bool name = record.Name != texts[0];
            bool description = texts.Length > 1 && texts[1].Length > 0 && record.GetString("description") != texts[1];
            if (!name && !description) continue;

            var edit = context.Edit(id);
            edit.Name = texts[0];
            if (name) edit.Flags |= 2;   // RENAMED (docs/formats/fcs-mod.md)
            if (description) edit.Strings["description"] = texts[1];
            renamed++;
        }

        if (renamed > 0) context.Log($"{renamed} records in {Localisation.Loc.Language.Code}");
    }

    static string Language(string code) => code.Length >= 2 ? code[..2].ToLowerInvariant() : "en";

    static Catalogue? Read(string folder, string language, Action<string> log)
    {
        if (OriginalFiles.Find(folder, Path.Combine("Localization", language + ".json"), log) is not { } path) return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return new Catalogue(Records(doc.RootElement), Strings(doc.RootElement));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            log($"{Path.GetFileName(path)} not read ({e.Message})");
            return null;
        }
    }

    static Dictionary<string, string[]> Records(JsonElement root)
    {
        var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!root.TryGetProperty("records", out var records) || records.ValueKind != JsonValueKind.Object) return result;

        foreach (var p in records.EnumerateObject())
        {
            if (p.Value.ValueKind == JsonValueKind.Array) result[p.Name] = [.. p.Value.EnumerateArray().Select(v => v.GetString() ?? "")];
        }

        return result;
    }

    static Dictionary<string, string> Strings(JsonElement root)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!root.TryGetProperty("ui", out var ui) || ui.ValueKind != JsonValueKind.Object) return result;

        foreach (var p in ui.EnumerateObject())
        {
            if (p.Value.ValueKind == JsonValueKind.String) result[p.Name] = p.Value.GetString()!;
        }

        return result;
    }
}
