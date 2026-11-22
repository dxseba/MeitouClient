using System.Text;
using Meitou.Data;
using Meitou.Simulation.Items;

namespace Meitou.BetterLooting;

/// <summary>
/// The fork's "Always take these items" list (<c>BetterLooting.ini</c> <c>[Rules] RequiredItems</c>): exact item names or record ids that skip
/// the category, minimum value and value-per-kg rules (never the theft rule, storage protection, range or room). Entries are split by <c>;</c>,
/// <c>|</c> or a new line; <c>name:</c> matches the shown or base name ignoring ASCII case, <c>id:</c> the record id exactly, an entry without a
/// prefix either. At most 128 entries; a value over 8191 UTF-8 bytes disables the list.
/// </summary>
public sealed class RequiredItems
{
    /// <summary>
    /// The original's limits on the list.
    /// </summary>
    const int MaxEntries = 128, MaxBytes = 8191;

    /// <summary>
    /// The empty list.
    /// </summary>
    public static readonly RequiredItems None = new([], []);

    /// <summary>
    /// Entries matched against a name (<c>name:</c> or no prefix).
    /// </summary>
    readonly HashSet<string> _names;

    /// <summary>
    /// Entries matched against a record id (<c>id:</c> or no prefix).
    /// </summary>
    readonly HashSet<string> _ids;

    RequiredItems(HashSet<string> names, HashSet<string> ids)
    {
        _names = names;
        _ids = ids;
    }

    /// <summary>
    /// Whether the list is empty.
    /// </summary>
    public bool IsEmpty => _names.Count == 0 && _ids.Count == 0;

    /// <summary>
    /// The list from a <c>RequiredItems</c> value; <see cref="None"/> for an empty or oversized one.
    /// </summary>
    public static RequiredItems Parse(string? value, Action<string> log)
    {
        if (string.IsNullOrWhiteSpace(value)) return None;
        if (Encoding.UTF8.GetByteCount(value) > MaxBytes)
        {
            log("RequiredItems exceeds 8191 UTF-8 bytes; whitelist disabled");
            return None;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        int count = 0;
        foreach (var raw in value.Split([';', '|', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (count == MaxEntries)
            {
                log("RequiredItems limited to 128 entries");
                break;
            }

            Add(raw, names, ids);
            count++;
        }

        return new(names, ids);
    }

    /// <summary>
    /// One entry into the name and id sets by its prefix.
    /// </summary>
    static void Add(string entry, HashSet<string> names, HashSet<string> ids)
    {
        if (entry.StartsWith("name:", StringComparison.OrdinalIgnoreCase)) names.Add(entry[5..].Trim());
        else if (entry.StartsWith("id:", StringComparison.OrdinalIgnoreCase)) ids.Add(entry[3..].Trim());
        else
        {
            names.Add(entry);
            ids.Add(entry);
        }
    }

    /// <summary>
    /// Whether the item is on the list: its record id, its record's shown name or base name, or the item's own name.
    /// </summary>
    public bool Matches(ItemInstance item, GameDatabase db)
    {
        if (IsEmpty) return false;
        if (_ids.Contains(item.Record) || _names.Contains(item.Name)) return true;

        return db.Find(item.Record) is { } record && (_names.Contains(record.Name) || _names.Contains(record.BaseName));
    }

    /// <summary>
    /// The <c>RequiredItems</c> value of <c>BetterLooting.ini</c> in the mod folder (<c>[Rules]</c>); null without the file or key.
    /// </summary>
    public static string? ReadIni(string folder)
    {
        var path = Path.Combine(folder, "BetterLooting.ini");
        if (!File.Exists(path)) return null;

        string? section = null;
        foreach (var line in File.ReadLines(path))
        {
            var text = line.Trim();
            if (text.StartsWith('[') && text.EndsWith(']')) section = text[1..^1].Trim();
            else if (string.Equals(section, "Rules", StringComparison.OrdinalIgnoreCase) && Value(text, "RequiredItems") is { } value) return value;
        }

        return null;
    }

    /// <summary>
    /// The value of a <c>key=value</c> line for that key; null for another line.
    /// </summary>
    static string? Value(string line, string key)
    {
        int eq = line.IndexOf('=');
        return eq > 0 && string.Equals(line[..eq].Trim(), key, StringComparison.OrdinalIgnoreCase) ? line[(eq + 1)..].Trim() : null;
    }
}
