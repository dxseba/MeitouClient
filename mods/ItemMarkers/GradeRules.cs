using System.Globalization;
using System.Numerics;
using System.Text;
using Meitou.Data.Fcs;

namespace Meitou.ItemMarkers;

/// <summary>
/// Grade profiles: <c>*.ini</c> files in the <c>grades</c> folders (the port's own and the installed original's), as the original's
/// <c>sdk/README.txt</c> and DLL describe them. <c>[Grade.name]</c>: <c>Rank</c> 0..5, <c>Colour</c> #RRGGBB, optional <c>Palette</c> (a role of the
/// interface's palette, Colour the fallback). <c>[Rule.name]</c>: <c>Grade</c> (of the same file), any of <c>ItemID</c>, <c>MaterialID</c>,
/// <c>Type</c> (numeric itemType) as AND conditions (at least one), optional <c>LevelMin</c>/<c>LevelMax</c> (gear only) and <c>Priority</c>
/// -10000..10000. <c>[Profile]</c> <c>Enabled=0</c> leaves a file out. Names and keys are case-sensitive ASCII. A bad file rejects the whole reload
/// and keeps the previous rules. Rules are matched highest priority first, ties by file name, then section order.
/// </summary>
sealed class GradeRules(Action<string> log)
{
    const int MaxFiles = 64, MaxFileBytes = 64 * 1024, MaxGradesPerFile = 128, MaxRules = 512, MaxLine = 511, MaxId = 127, MaxGradeName = 63;
    const int MaxPriority = 10000;

    sealed record Grade(int Rank, Vector4 Colour, string? Palette);

    sealed record Rule(string? Item, string? Material, int? Type, int? LevelMin, int? LevelMax, int Priority, int Order, Grade Grade);

    Rule[] _rules = [];

    /// <summary>The first matching rule's grade; null when none matches.</summary>
    public ItemGrade? Match(in ItemFacts item, FcsRecordType? type, bool gear)
    {
        foreach (var r in _rules)
        {
            if (r.Item is not null && r.Item != item.Record) continue;
            if (r.Material is not null && r.Material != item.Material) continue;
            if (r.Type is { } t && (type is null || (int)type != t)) continue;
            if ((r.LevelMin is not null || r.LevelMax is not null) && !gear) continue;
            if (r.LevelMin is { } min && item.Level < min) continue;
            if (r.LevelMax is { } max && item.Level > max) continue;

            var colour = r.Grade.Palette is { } role ? Grades.Palette(role) ?? r.Grade.Colour : r.Grade.Colour;
            return new(r.Grade.Rank, colour);
        }

        return null;
    }

    /// <summary>Reads every profile of <paramref name="folders"/> again; on any fault the previous rules stay, logged.</summary>
    public void Reload(IEnumerable<string> folders)
    {
        try
        {
            var files = folders.Where(Directory.Exists)
                .SelectMany(f => Directory.EnumerateFiles(f, "*.ini"))
                .OrderBy(Path.GetFileName, StringComparer.Ordinal)
                .ToList();
            if (files.Count > MaxFiles) throw new InvalidDataException($"more than {MaxFiles} profiles");

            var rules = new List<Rule>();
            foreach (string file in files) Read(file, rules);
            if (rules.Count > MaxRules) throw new InvalidDataException("rule capacity");

            _rules = [.. rules.OrderByDescending(r => r.Priority).ThenBy(r => r.Order)];
            if (files.Count > 0) log($"grade rules: {_rules.Length} from {files.Count} profiles");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            log($"grade rules reload failed - keeping previous rules: {e.Message}");
        }
    }

    void Read(string file, List<Rule> rules)
    {
        if (new FileInfo(file).Length > MaxFileBytes) throw new InvalidDataException($"{Path.GetFileName(file)}: profile size");

        var sections = Sections(file);
        if (sections.TryGetValue("Profile", out var profile) && !ProfileEnabled(profile, file)) return;

        var grades = ReadGrades(sections, file);
        foreach (var (name, keys) in sections.Where(s => s.Key.StartsWith("Rule.", StringComparison.Ordinal)))
            rules.Add(ReadRule(name, keys, grades, rules.Count, file));
    }

    static bool ProfileEnabled(Dictionary<string, string> keys, string file)
    {
        foreach (string key in keys.Keys)
        {
            if (key != "Enabled") throw Bad(file, "invalid Profile");
        }

        return !keys.TryGetValue("Enabled", out string? value) || value switch
        {
            "1" => true,
            "0" => false,
            _ => throw Bad(file, "invalid Profile"),
        };
    }

    static Dictionary<string, Grade> ReadGrades(Dictionary<string, Dictionary<string, string>> sections, string file)
    {
        var grades = new Dictionary<string, Grade>(StringComparer.Ordinal);
        foreach (var (name, keys) in sections.Where(s => s.Key.StartsWith("Grade.", StringComparison.Ordinal)))
        {
            if (name.Length - 6 is 0 or > MaxGradeName || grades.Count >= MaxGradesPerFile) throw Bad(file, "invalid Grade");
            if (keys.Keys.Any(k => k is not ("Rank" or "Colour" or "Palette"))) throw Bad(file, "unknown section/key");

            int rank = Int(keys, "Rank", 0, 5, file) ?? throw Bad(file, "invalid Grade");
            var colour = Grades.Hex(keys.GetValueOrDefault("Colour")) ?? throw Bad(file, "invalid Grade");
            string? palette = keys.GetValueOrDefault("Palette");
            if (palette is not null && !IsRole(palette)) throw Bad(file, "invalid palette role");

            grades[name[6..]] = new(rank, colour, palette);
        }

        return grades;
    }

    static bool IsRole(string role) => role is "ItemGrade0" or "ItemGrade1" or "ItemGrade2" or "ItemGrade3" or "ItemGrade4" or "ItemGrade5"
        or "ItemGradeMeitou";

    static Rule ReadRule(string name, Dictionary<string, string> keys, Dictionary<string, Grade> grades, int order, string file)
    {
        string[] known = ["ItemID", "MaterialID", "Type", "LevelMin", "LevelMax", "Priority", "Grade"];
        if (keys.Keys.Any(k => !known.Contains(k))) throw Bad(file, "unknown section/key");
        if (!grades.TryGetValue(keys.GetValueOrDefault("Grade") ?? "", out var grade)) throw Bad(file, $"invalid Rule {name}");

        string? item = Id(keys, "ItemID", file), material = Id(keys, "MaterialID", file);
        int? type = Int(keys, "Type", 0, 1000, file);
        if (item is null && material is null && type is null) throw Bad(file, "unsafe/unbounded Rule");

        int priority = Int(keys, "Priority", -MaxPriority, MaxPriority, file) ?? 0;
        return new(item, material, type, Int(keys, "LevelMin", 0, 100, file), Int(keys, "LevelMax", 0, 100, file), priority, order, grade);
    }

    static string? Id(Dictionary<string, string> keys, string key, string file)
    {
        if (!keys.TryGetValue(key, out string? id)) return null;
        if (id.Length is 0 or > MaxId) throw Bad(file, $"invalid {key}");

        return id;
    }

    static int? Int(Dictionary<string, string> keys, string key, int min, int max, string file)
    {
        if (!keys.TryGetValue(key, out string? text)) return null;
        if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value) || value < min || value > max)
            throw Bad(file, $"invalid {key}");

        return value;
    }

    /// <summary>The file's sections and keys; a duplicate, an empty key, a line too long or anything else than a section or key=value throws.</summary>
    static Dictionary<string, Dictionary<string, string>> Sections(string file)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        Dictionary<string, string>? current = null;
        foreach (string raw in File.ReadLines(file, Encoding.UTF8))
        {
            if (Encoding.UTF8.GetByteCount(raw) > MaxLine) throw Bad(file, "line exceeds 511 bytes");

            string line = raw.Trim().TrimStart('﻿');
            if (line.Length == 0 || line[0] is ';' or '#') continue;

            current = line[0] == '[' ? Section(line, sections, file) : current;
            if (line[0] != '[') AddKey(line, current, file);
        }

        return sections;
    }

    static Dictionary<string, string> Section(string line, Dictionary<string, Dictionary<string, string>> sections, string file)
    {
        if (line[^1] != ']') throw Bad(file, "expected section/key=value");

        string name = line[1..^1].Trim();
        bool known = name == "Profile" || name.StartsWith("Grade.", StringComparison.Ordinal) || name.StartsWith("Rule.", StringComparison.Ordinal);
        if (!known || !IsAscii(name)) throw Bad(file, "unknown section/key");
        if (sections.ContainsKey(name)) throw Bad(file, "duplicate section");

        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        sections[name] = keys;
        return keys;
    }

    static void AddKey(string line, Dictionary<string, string>? section, string file)
    {
        int eq = line.IndexOf('=');
        if (section is null || eq <= 0) throw Bad(file, "expected section/key=value");

        string key = line[..eq].Trim(), value = line[(eq + 1)..].Trim();
        if (key.Length == 0 || !IsAscii(key) || !section.TryAdd(key, value)) throw Bad(file, "duplicate/empty key");
    }

    static bool IsAscii(string s) => s.All(c => c is > ' ' and < '\u007f');

    static InvalidDataException Bad(string file, string why) => new($"grade profile '{Path.GetFileName(file)}' refused: {why}");
}
