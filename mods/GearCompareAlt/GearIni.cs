using System.Globalization;

namespace Meitou.GearCompareAlt;

/// <summary>
/// The original's settings file as defaults: its <c>GearCompare.ini</c> (the player's settings) in the installed original's folder, else its
/// <c>GearCompare.defaults.ini</c>; section <c>[GearCompare]</c>, <c>key=value</c> lines, <c>;</c> comments. A key that is missing or does not
/// read keeps the port's default (the defaults file's own values).
/// </summary>
static class GearIni
{
    /// <summary>Sets <paramref name="settings"/> from the file; returns the file read, null when there is none.</summary>
    public static string? Apply(string? originalFolder, GearSettings settings, Action<string> log)
    {
        if (originalFolder is null) return null;

        string? path = new[] { "GearCompare.ini", "GearCompare.defaults.ini" }
            .Select(f => Path.Combine(originalFolder, f))
            .FirstOrDefault(File.Exists);
        if (path is null) return null;

        try
        {
            var values = Read(File.ReadAllLines(path));
            Set(settings, values);
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log($"{Path.GetFileName(path)} not read: {e.Message}");
            return null;
        }
    }

    /// <summary>The <c>[GearCompare]</c> section's values by key (case ignored).</summary>
    static Dictionary<string, string> Read(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool inSection = false;
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#') continue;

            if (line[0] == '[')
            {
                inSection = line.Equals("[GearCompare]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            int eq = line.IndexOf('=');
            if (inSection && eq > 0) values[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }

        return values;
    }

    static void Set(GearSettings s, Dictionary<string, string> v)
    {
        s.Enabled = Bool(v, "Enabled", s.Enabled);
        s.HoldKey = v.TryGetValue("HoldKey", out string? key) && key.Length <= 32 ? key : s.HoldKey;
        s.CompareEmptySlot = Bool(v, "CompareEmptySlot", s.CompareEmptySlot);
        s.CraftCompare = Bool(v, "CraftCompare", s.CraftCompare);
        s.ShowTotal = Bool(v, "ShowTotal", s.ShowTotal);
        s.TotalPerProperty = Int(v, "TotalPerProperty", s.TotalPerProperty);
        s.TotalPerKg = Int(v, "TotalPerKg", s.TotalPerKg);
        s.ColourBetter = Colour(v, "ColourBetter", s.ColourBetter);
        s.ColourWorse = Colour(v, "ColourWorse", s.ColourWorse);
        s.Debug = Bool(v, "Debug", s.Debug);
    }

    static bool Bool(Dictionary<string, string> v, string key, bool fallback) =>
        v.TryGetValue(key, out string? text) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n != 0 : fallback;

    static int Int(Dictionary<string, string> v, string key, int fallback) =>
        v.TryGetValue(key, out string? text) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
            ? Math.Clamp(n, 0, GearSettings.MaxWeight)
            : fallback;

    static string Colour(Dictionary<string, string> v, string key, string fallback) =>
        v.TryGetValue(key, out string? text) && text.Length is > 0 and <= 7 ? text : fallback;
}
