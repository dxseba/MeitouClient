namespace Meitou.SlaverMod;

/// <summary>
/// What slavers and slaves say (the original's <c>SlaverMod_lines.ini</c>, read from the installed original): per key, variants split by '|',
/// from the section of the game's language when it has one, else <c>[en]</c>.
/// </summary>
sealed class SlaveLines(Dictionary<string, string[]> lines)
{
    public static SlaveLines Read(string? folder, string language)
    {
        var ini = Ini.Read(folder is null ? null : Path.Combine(folder, "SlaverMod_lines.ini"));
        string code = language.Length >= 2 ? language[..2] : "en";
        var section = ini.GetValueOrDefault(code) ?? ini.GetValueOrDefault("en") ?? [];
        var english = ini.GetValueOrDefault("en") ?? [];
        var lines = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            // The DLL's built-in caravan call (Observed, decompile strings); the lines file may override it.
            ["CaravanArrive"] = ["Slave caravan! Fresh stock, chained and ready!", "Slavers here! Come see the merchandise!",
                "Slaves for sale, and we're buying too!"],
        };
        foreach (var (key, value) in english.Concat(section)) lines[key] = value.Split('|', StringSplitOptions.RemoveEmptyEntries);

        return new SlaveLines(lines);
    }

    /// <summary>One variant of <paramref name="key"/> by <paramref name="roll"/> (0..1); null when the key has none.</summary>
    public string? Pick(string key, float roll) =>
        lines.TryGetValue(key, out var v) && v.Length > 0 ? v[Math.Min(v.Length - 1, (int)(roll * v.Length))] : null;
}
