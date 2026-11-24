using System.Text;
using Meitou.Modding;

namespace Meitou.Mentors;

/// <summary>
/// The original's Russian texts for a Russian game (its locale id starting "ru", as the original's test), read at start from the installed
/// original's <c>Mentors.dll</c>; nothing of the original ships with the port. The original has no catalogue: each text is one call of a
/// two-language picker (rdx the Russian, r8 the English UTF-16 string), and the trade and skill label tables hold both (README "Russian"). The
/// reader keeps the pairs whose call sets both by <c>lea r64, [rip+disp32]</c> under the one picker that takes the training reply, and writes
/// them with the tables' pairs as a gettext catalogue in the temporary folder, added as the mod's translations; the port's texts go through
/// <see cref="MentorsMod.Tr"/>.
/// </summary>
static class OriginalTexts
{
    const string Domain = "mentors";
    const string Reply = "Could you teach me something? [training]";
    const int MaxText = 400;

    /// <summary>
    /// Adds the original's Russian texts as the mod's translations in a Russian game; nothing otherwise (one log line when not read).
    /// </summary>
    public static void Add(ModContext context)
    {
        if (!context.Language.StartsWith("ru", StringComparison.OrdinalIgnoreCase)) return;

        if (OriginalTrades.Locate(context) is not { } path)
        {
            context.Log("the original Mentors.dll is not installed: texts stay English");
            return;
        }

        try
        {
            var pe = PeImage.Read(path);
            var pairs = PickerPairs(pe).Concat(OriginalTrades.RussianPairs(pe)).ToList();
            context.AddTranslations(Domain, WriteCatalogue(context, pairs));
            context.Log($"original Russian texts read: {pairs.Count}");
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            context.Log($"original Russian texts not read ({e.Message}): texts stay English");
        }
    }

    /// <summary>
    /// The (English, Russian) pairs of every call to the picker: the call target whose pairs include the training reply; throws when there is
    /// not exactly one.
    /// </summary>
    static List<(string English, string Russian)> PickerPairs(PeImage pe)
    {
        var code = pe.Section(".text") ?? throw new InvalidDataException("no .text section");
        var byTarget = new Dictionary<ulong, List<(string, string)>>();
        int after = code.RawOffset;
        for (int o = code.RawOffset; o + 5 <= code.RawEnd; o++)
        {
            if (pe.U8(o) != 0xe8) continue;

            ulong target = pe.Va(code, o) + 5 + (ulong)(long)pe.I32(o + 1);
            if (!InCode(pe, code, target)) continue;

            var found = Pair(pe, code, Math.Max(after, o - 0x40), o);
            after = o + 5;
            if (found is not { } pair) continue;

            if (!byTarget.TryGetValue(target, out var list)) byTarget[target] = list = [];

            list.Add(pair);
        }

        var pickers = byTarget.Values.Where(list => list.Any(p => p.Item1 == Reply)).ToList();
        if (pickers.Count != 1) throw new InvalidDataException($"{pickers.Count} text pickers found, expected 1");

        return pickers[0];
    }

    /// <summary>A call's target inside the code section (a stray E8 byte inside another instruction mostly points elsewhere).</summary>
    static bool InCode(PeImage pe, PeSection code, ulong target) =>
        target >= pe.Va(code, code.RawOffset) && target < pe.Va(code, code.RawOffset) + (ulong)code.RawSize;

    /// <summary>
    /// The strings the bytes from <paramref name="start"/> (at most 0x40 back, not before the call before) to the call at <paramref name="call"/>
    /// load into rdx (Russian) and r8 (English); null without both.
    /// </summary>
    static (string, string)? Pair(PeImage pe, PeSection code, int start, int call)
    {
        ulong? rdx = null, r8 = null;
        for (int p = start; p + 7 <= call;)
        {
            byte rex = pe.U8(p), modrm = pe.U8(p + 2);
            if (rex is not (0x48 or 0x4c) || pe.U8(p + 1) != 0x8d || (modrm & 0xc7) != 0x05)
            {
                p++;
                continue;
            }

            int register = ((modrm >> 3) & 7) + (rex == 0x4c ? 8 : 0);
            ulong va = pe.Va(code, p) + 7 + (ulong)(long)pe.I32(p + 3);
            if (register == 2) rdx = va;
            else if (register == 8) r8 = va;

            p += 7;
        }

        if (rdx is not { } ru || r8 is not { } en || pe.Text16(ru, MaxText) is not { } russian) return null;

        return pe.Text16(en, MaxText) is { } english ? (english, russian) : null;
    }

    /// <summary>Writes the pairs as <c>locale/&lt;language&gt;/LC_MESSAGES/mentors.po</c> under a temporary folder; returns that folder.</summary>
    static string WriteCatalogue(ModContext context, List<(string English, string Russian)> pairs)
    {
        string root = Path.Combine(Path.GetTempPath(), "Meitou", "dxseba.mentors");
        string messages = Path.Combine(root, "locale", context.Language, "LC_MESSAGES");
        Directory.CreateDirectory(messages);

        var po = new StringBuilder("msgid \"\"\nmsgstr \"Content-Type: text/plain; charset=UTF-8\\n\"\n");
        foreach (var (english, russian) in pairs.DistinctBy(p => p.English).OrderBy(p => p.English, StringComparer.Ordinal))
            po.Append($"\nmsgid \"{Escape(english)}\"\nmsgstr \"{Escape(russian)}\"\n");

        File.WriteAllText(Path.Combine(messages, Domain + ".po"), po.ToString(), new UTF8Encoding(false));
        return root;
    }

    static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
