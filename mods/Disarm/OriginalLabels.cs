using System.Text;
using Meitou.Content;
using Meitou.Modding;

namespace Meitou.Disarm;

/// <summary>
/// The original's own translations of its labels (menu rows and settings), read at start from the installed original's <c>Disarm.dll</c>
/// (Workshop 3759461630) for the game's language; nothing of the original ships with the port. Its labels are not in a catalogue: each is one
/// call of a nine-language picker whose arguments are string addresses, English first, then de, es, fr, pt, ru, ja, ko, zh (README "Labels").
/// The reader follows each call's argument set-up (<c>lea r64, [rip+disp32]</c> into rdx, r8, r9 or a register stored to the stack slots
/// rsp+0x20 to rsp+0x48) and keeps a label only when all nine are valid UTF-8 strings and every label shares one picker. The texts of the game's
/// language become a gettext catalogue in the temporary folder, added as the mod's translations (they never enter the port's folder).
/// </summary>
static class OriginalLabels
{
    const string OriginalItem = "3759461630";
    const string OriginalName = "Disarm";
    const string DllName = "Disarm.dll";
    const string Domain = "disarm";

    /// <summary>The picker's languages in argument order (the original's locale ids).</summary>
    static readonly string[] Languages = ["en", "de", "es", "fr", "pt", "ru", "ja", "ko", "zh"];

    /// <summary>The stack slots of the picker's fourth to ninth string (the x64 call's arguments 5 to 10).</summary>
    static readonly int[] StackSlots = [0x20, 0x28, 0x30, 0x38, 0x40, 0x48];

    /// <summary>The original's English labels the port shows (its settings labels are the same texts).</summary>
    static readonly string[] Wanted =
    [
        "Disarm", "Kick", "Enable Disarm", "Chance Multiplier", "Destroy Instead Of Drop", "Enable Kick", "Kick Distance",
        "Enable KO Weapon Drop", "KO Drop Chance Multiplier", "Allow Stolen Flag", "Allow On Own Squad", "Allow AI Rearm (Beta)",
    ];

    /// <summary>
    /// Adds the original's labels in the game's language as the mod's translations; nothing for English or without the original (one log line).
    /// </summary>
    public static void Add(ModContext context)
    {
        int language = Array.IndexOf(Languages, LanguagePart(context.Language));
        if (language <= 0) return;

        if (Locate(context) is not { } path)
        {
            context.Log($"the original {DllName} (Workshop {OriginalItem}) is not installed: labels stay English");
            return;
        }

        try
        {
            var texts = Read(path, language);
            string folder = WriteCatalogue(context, texts);
            context.AddTranslations(Domain, folder);
            context.Log($"original labels read: {texts.Count} for {context.Language}");
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            context.Log($"original labels not read ({e.Message}): labels stay English");
        }
    }

    static string LanguagePart(string code) => code.Split('_', '-')[0].ToLowerInvariant();

    /// <summary>
    /// English label -> its text in language <paramref name="language"/>; throws <see cref="InvalidDataException"/> when unexpected.
    /// </summary>
    static Dictionary<string, string> Read(string path, int language)
    {
        var pe = PeImage.Read(path);
        var code = pe.Section(".text") ?? throw new InvalidDataException("no .text section");
        var calls = PickerCalls(pe, code);

        var pickers = calls.Select(c => c.Picker).Distinct().ToList();
        if (pickers.Count != 1) throw new InvalidDataException($"labels found under {pickers.Count} pickers");

        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (_, args) in calls) texts.TryAdd(args[0], args[language]);

        if (!texts.ContainsKey("Disarm") || !texts.ContainsKey("Kick")) throw new InvalidDataException("menu labels not found");

        return texts;
    }

    /// <summary>Every call whose argument set-up names one of <see cref="Wanted"/> in rdx with all nine strings valid.</summary>
    static List<(ulong Picker, string[] Args)> PickerCalls(PeImage pe, PeSection code)
    {
        var found = new List<(ulong, string[])>();
        var lastCall = new Dictionary<ulong, int>();
        for (int o = code.RawOffset; o + 5 <= code.RawEnd; o++)
        {
            if (pe.U8(o) != 0xe8) continue;

            ulong target = pe.Va(code, o) + 5 + (ulong)(long)pe.I32(o + 1);
            int start = Math.Max(code.RawOffset, Math.Max(o - 0xa0, lastCall.GetValueOrDefault(target)));
            lastCall[target] = o + 5;
            if (Arguments(pe, code, start, o) is { } args && Wanted.Contains(args[0])) found.Add((target, args));
        }

        return found;
    }

    /// <summary>
    /// The nine string arguments set up between <paramref name="start"/> and the call at <paramref name="call"/> (file offsets), or null when one
    /// is missing or not a valid string.
    /// </summary>
    static string[]? Arguments(PeImage pe, PeSection code, int start, int call)
    {
        var registers = new Dictionary<int, ulong>();
        var slots = new Dictionary<int, ulong>();
        for (int p = start; p < call;)
            p += Step(pe, code, p, registers, slots);

        ulong?[] addresses = [Get(registers, 2), Get(registers, 8), Get(registers, 9), .. StackSlots.Select(s => Get(slots, s))];
        var args = new string[addresses.Length];
        for (int i = 0; i < args.Length; i++)
        {
            if (addresses[i] is not { } va || pe.Utf8(va, 200) is not { } text) return null;

            args[i] = text;
        }

        return args;
    }

    static ulong? Get(Dictionary<int, ulong> map, int key) => map.TryGetValue(key, out var value) ? value : null;

    /// <summary>
    /// One pattern at <paramref name="p"/>: <c>lea r64, [rip+disp32]</c> (REX.W 48/4C, 8D, ModRM 00 reg 101) sets the register;
    /// <c>mov [rsp+disp8], r64</c> (REX.W, 89, ModRM 01 reg 100, SIB 24) stores a set register into a slot; else one byte on. Returns the bytes
    /// passed.
    /// </summary>
    static int Step(PeImage pe, PeSection code, int p, Dictionary<int, ulong> registers, Dictionary<int, ulong> slots)
    {
        byte rex = pe.U8(p), op = pe.U8(p + 1), modrm = pe.U8(p + 2);
        if (rex is not (0x48 or 0x4c)) return 1;

        int register = ((modrm >> 3) & 7) + (rex == 0x4c ? 8 : 0);
        if (op == 0x8d && (modrm & 0xc7) == 0x05)
        {
            registers[register] = pe.Va(code, p) + 7 + (ulong)(long)pe.I32(p + 3);
            return 7;
        }

        if (op == 0x89 && (modrm & 0xc7) == 0x44 && pe.U8(p + 3) == 0x24)
        {
            if (registers.TryGetValue(register, out var value)) slots[pe.U8(p + 4)] = value;

            return 5;
        }

        return 1;
    }

    /// <summary>Writes the texts as <c>locale/&lt;language&gt;/LC_MESSAGES/disarm.po</c> under a temporary folder; returns that folder.</summary>
    static string WriteCatalogue(ModContext context, Dictionary<string, string> texts)
    {
        string root = Path.Combine(Path.GetTempPath(), "Meitou", "dxseba.disarm");
        string messages = Path.Combine(root, "locale", context.Language, "LC_MESSAGES");
        Directory.CreateDirectory(messages);

        var po = new StringBuilder("msgid \"\"\nmsgstr \"Content-Type: text/plain; charset=UTF-8\\n\"\n");
        foreach (var (english, text) in texts.OrderBy(t => t.Key, StringComparer.Ordinal))
            po.Append($"\nmsgid \"{Escape(english)}\"\nmsgstr \"{Escape(text)}\"\n");

        File.WriteAllText(Path.Combine(messages, Domain + ".po"), po.ToString(), new UTF8Encoding(false));
        return root;
    }

    static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");

    static string? Locate(ModContext context)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(context.Folder)) is { } parent && File.Exists(Path.Combine(parent, OriginalItem, DllName)))
            return Path.Combine(parent, OriginalItem, DllName);

        return Installed(context) is { } folder && File.Exists(Path.Combine(folder, DllName)) ? Path.Combine(folder, DllName) : null;
    }

    /// <summary>The installed mod named "Disarm" without Meitou code (the original), not the port's own folder.</summary>
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
