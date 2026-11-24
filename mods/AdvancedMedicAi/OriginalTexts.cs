using Meitou.Modding;

namespace Meitou.AdvancedMedicAi;

/// <summary>
/// The original's Ukrainian and Russian texts, read at start from the installed original's <c>AdvancedMedicAI.dll</c> (Workshop 3742288543, the
/// port's required item) and added in memory as the mod's translations; nothing of the original ships with the port. Each of its texts is a row
/// of three string pointers in the DLL's data (English, Ukrainian, Russian, picked by the game's language: README "Texts"). The reader takes a
/// row only when its first pointer is one of the English texts below and all three are valid UTF-8.
/// </summary>
static class OriginalTexts
{
    const string OriginalItem = OriginalFolder.Item;
    const string DllName = "AdvancedMedicAI.dll";
    const int MaxText = 512;

    static readonly string[] Wanted =
    [
        "Player characters", "Allies", "Neutrals", "Enemies", "Wild animals", "Whole base in reach", "Field radius, m", "Robot repair radius, m",
        "Combat medic: treat own and allies nearby during fights", "Combat medic radius, m", "Avoid acid rain, gas and fire",
        "Respect manual orders, s", "Show notifications", "Faction rules...", "Advanced Medic AI: faction rules",
        "Saved rules for factions not in this game:", "Close", "Use general rule", "Always heal", "Never heal",
        "Medic AI: medics sent to give first aid: ",
    ];

    /// <summary>
    /// Adds the original's texts of the game's language (Ukrainian or Russian); nothing otherwise or without the original (one log line).
    /// </summary>
    public static void Add(ModContext context)
    {
        int column = Column(context.Language);
        if (column == 0) return;

        if (Locate(context) is not { } path)
        {
            context.Log($"the original {DllName} (Workshop {OriginalItem}) is not installed: texts stay English");
            return;
        }

        try
        {
            var texts = Read(path, column);
            context.AddTranslations(context.Language, texts);
            context.Log($"original texts read: {texts.Count} for {context.Language}");
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            context.Log($"original texts not read ({e.Message}): texts stay English");
        }
    }

    /// <summary>
    /// The row column of a locale: 1 Ukrainian, 2 Russian, 0 for any other (English).
    /// </summary>
    static int Column(string language) => language.Split('_', '-')[0].ToLowerInvariant() switch
    {
        "uk" => 1,
        "ru" => 2,
        _ => 0,
    };

    static Dictionary<string, string> Read(string path, int column)
    {
        var pe = PeImage.Read(path);
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var section in pe.Sections.Where(s => s.Name is ".rdata" or ".data"))
        {
            for (int at = section.RawOffset; at + 24 <= section.RawEnd; at += 8) TakeRow(pe, at, column, texts);
        }

        if (!texts.ContainsKey("Close") || !texts.ContainsKey("Never heal")) throw new InvalidDataException("text rows not found");

        return texts;
    }

    static void TakeRow(PeImage pe, int at, int column, Dictionary<string, string> texts)
    {
        if (pe.Utf8(pe.U64(at), MaxText) is not { } english || Array.IndexOf(Wanted, english) < 0 || texts.ContainsKey(english)) return;

        string? uk = pe.Utf8(pe.U64(at + 8), MaxText), ru = pe.Utf8(pe.U64(at + 16), MaxText);
        if (uk is null || ru is null) return;

        texts[english] = column == 1 ? uk : ru;
    }

    static string? Locate(ModContext context) =>
        OriginalFolder.Find(context) is { } folder && File.Exists(Path.Combine(folder, DllName)) ? Path.Combine(folder, DllName) : null;
}
