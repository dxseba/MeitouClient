using Meitou.Content;
using Meitou.Modding;

namespace Meitou.DesolationSystem;

/// <summary>One codex entry: the perk's id, name and "requirement|effect" description, English and Chinese.</summary>
sealed record CodexEntry(int Id, string Name, string Description, string ChineseName, string ChineseDescription);

/// <summary>
/// The original's perk codex, read at start from the installed original's <c>DesolationSystem.dll</c> (Workshop 3796024680); nothing of it ships
/// with the port. The DLL holds one 0x58-byte record per perk (<b>Verified</b>, read from the file): +0 and +4 the id twice, +8 / +0x10 the
/// Chinese / English name, +0x48 / +0x50 the Chinese / English "requirement|effect" text, all pointers to UTF-8 strings. The reader scans
/// <c>.rdata</c> for records of the known ids whose English text has the "|"; without the original the panel shows the port's own short names.
/// </summary>
static class PerkCodex
{
    const string OriginalItem = "3796024680";
    const string DllName = "DesolationSystem.dll";
    const int Stride = 0x58;

    internal static IReadOnlyDictionary<string, CodexEntry> Entries { get; private set; } = new Dictionary<string, CodexEntry>();

    internal static void Load(ModContext context)
    {
        if (Locate(context) is not { } path)
        {
            context.Log($"the original {DllName} (Workshop {OriginalItem}) is not installed: the panel shows short names only");
            return;
        }

        try
        {
            Entries = Read(PeImage.Read(path));
            context.Log($"original codex read: {Entries.Count} perks");
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            context.Log($"original codex not read ({e.Message})");
        }
    }

    static Dictionary<string, CodexEntry> Read(PeImage pe)
    {
        var data = pe.Section(".rdata") ?? throw new InvalidDataException("no .rdata section");
        var found = new Dictionary<string, CodexEntry>();
        for (int at = data.RawOffset; at + Stride <= data.RawEnd; at += 8)
        {
            if (Entry(pe, at) is { } entry) found.TryAdd(entry.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), entry);
        }

        if (found.Count < 20) throw new InvalidDataException($"only {found.Count} codex records found");
        return found;
    }

    static CodexEntry? Entry(PeImage pe, int at)
    {
        int id = pe.I32(at);
        if (id != pe.I32(at + 4) || !Texts.Known(id)) return null;

        string? en = pe.Utf8(pe.U64(at + 0x10), 128), desc = pe.Utf8(pe.U64(at + 0x50), 2048);
        if (en is null || desc is null || !desc.Contains('|')) return null;

        return new CodexEntry(id, en, desc, pe.Utf8(pe.U64(at + 0x08), 256) ?? en, pe.Utf8(pe.U64(at + 0x48), 4096) ?? desc);
    }

    static string? Locate(ModContext context)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(context.Folder)) is { } parent && File.Exists(Path.Combine(parent, OriginalItem, DllName)))
            return Path.Combine(parent, OriginalItem, DllName);

        try
        {
            if (GameInstall.Locate() is not { } install) return null;

            string own = Path.GetFullPath(context.Folder);
            var original = ModList.Discover(install).FirstOrDefault(m =>
                !m.HasCode && File.Exists(Path.Combine(m.Folder, DllName)) && !string.Equals(Path.GetFullPath(m.Folder), own, StringComparison.OrdinalIgnoreCase));
            return original is null ? null : Path.Combine(original.Folder, DllName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            context.Log($"mod folders not read: {e.Message}");
            return null;
        }
    }
}
