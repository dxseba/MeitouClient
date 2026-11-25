namespace Meitou.CharactersEmotions;

/// <summary>
/// A plain INI file: <c>[section]</c> headers and <c>key=value</c> lines, <c>;</c> and <c>#</c> comments; sections and keys found ignoring case
/// (the original lower-cases both), sections kept in file order.
/// </summary>
sealed class IniFile
{
    readonly List<(string Name, Dictionary<string, string> Values)> _sections = [];

    /// <summary>The sections in file order, a repeated header merged into its first.</summary>
    public IEnumerable<(string Name, IReadOnlyDictionary<string, string> Values)> Sections =>
        _sections.Select(s => (s.Name, (IReadOnlyDictionary<string, string>)s.Values));

    /// <summary>Reads <paramref name="path"/>; null when it cannot be read.</summary>
    public static IniFile? Read(string? path)
    {
        if (path is null || !File.Exists(path)) return null;

        try
        {
            return Parse(File.ReadAllLines(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    static IniFile Parse(IEnumerable<string> lines)
    {
        var ini = new IniFile();
        Dictionary<string, string>? current = null;
        foreach (string raw in lines)
        {
            string line = raw.Trim().TrimStart('﻿');
            if (line.Length == 0 || line[0] is ';' or '#') continue;

            if (line[0] == '[' && line.EndsWith(']'))
            {
                current = ini.Section(line[1..^1].Trim());
                continue;
            }

            int eq = line.IndexOf('=');
            if (eq <= 0 || current is null) continue;

            current[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }

        return ini;
    }

    Dictionary<string, string> Section(string name)
    {
        foreach (var (n, values) in _sections)
        {
            if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return values;
        }

        var added = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _sections.Add((name, added));
        return added;
    }

    /// <summary>The section's values; null when the file has none of that name.</summary>
    public IReadOnlyDictionary<string, string>? Get(string section) =>
        _sections.FirstOrDefault(s => string.Equals(s.Name, section, StringComparison.OrdinalIgnoreCase)).Values;
}
