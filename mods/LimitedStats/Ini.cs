using System.Globalization;

namespace Meitou.LimitedStats;

/// <summary>
/// The original's ini reading rules (<b>Verified</b>, original source): lines trimmed of blanks and control characters, <c>;</c> and <c>#</c> start a
/// comment line, <c>[name]</c> starts a section (case ignored), <c>key = value</c>; numbers read as C's <c>atof</c> (the leading number, 0 for none).
/// </summary>
static class Ini
{
    public static string Trim(string s)
    {
        int a = 0, b = s.Length;
        while (a < b && s[a] <= ' ') a++;
        while (b > a && s[b - 1] <= ' ') b--;
        return s[a..b];
    }

    /// <summary>The (section, key, value) lines of an ini file, sections lower case; nothing when the file does not exist.</summary>
    public static IEnumerable<(string Section, string Key, string Value)> Read(string path)
    {
        if (!File.Exists(path)) yield break;

        string section = "";
        foreach (string raw in File.ReadLines(path))
        {
            string line = Trim(raw);
            if (line.Length == 0 || line[0] is ';' or '#') continue;

            if (line[0] == '[')
            {
                int close = line.IndexOf(']');
                section = Trim(close < 0 ? line[1..] : line[1..close]).ToLowerInvariant();
                continue;
            }

            int eq = line.IndexOf('=');
            yield return eq < 0 ? (section, line, "") : (section, Trim(line[..eq]), Trim(line[(eq + 1)..]));
        }
    }

    /// <summary>C's <c>atof</c>: the leading number of <paramref name="s"/>, 0 when there is none.</summary>
    public static float Number(string s) => LeadingNumber(s) ?? 0;

    /// <summary>C's <c>atoi</c>.</summary>
    public static int Integer(string s) => (int)Math.Clamp(Math.Truncate((double)Number(s)), int.MinValue, int.MaxValue);

    /// <summary>The number <paramref name="s"/> starts with (after blanks), else null.</summary>
    public static float? LeadingNumber(string s)
    {
        int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        int start = i;
        if (i < s.Length && s[i] is '+' or '-') i++;
        int digits = 0;
        while (i < s.Length && char.IsAsciiDigit(s[i])) { i++; digits++; }
        if (i < s.Length && s[i] == '.')
        {
            i++;
            while (i < s.Length && char.IsAsciiDigit(s[i])) { i++; digits++; }
        }

        if (digits == 0) return null;

        int mantissaEnd = i;
        if (i < s.Length && s[i] is 'e' or 'E')
        {
            int j = i + 1;
            if (j < s.Length && s[j] is '+' or '-') j++;
            int expStart = j;
            while (j < s.Length && char.IsAsciiDigit(s[j])) j++;
            if (j > expStart) mantissaEnd = j;
        }

        return float.TryParse(s.AsSpan(start, mantissaEnd - start), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : null;
    }

    /// <summary>Splits on <paramref name="separator"/> outside round and square brackets.</summary>
    public static List<string> SplitTop(string s, char separator)
    {
        var parts = new List<string>();
        int round = 0, square = 0, start = 0;
        for (int i = 0; i < s.Length; i++)
        {
            switch (s[i])
            {
                case '(': round++; break;
                case ')': round--; break;
                case '[': square++; break;
                case ']': square--; break;
                default:
                    if (s[i] == separator && round == 0 && square == 0)
                    {
                        parts.Add(s[start..i]);
                        start = i + 1;
                    }
                    break;
            }
        }

        parts.Add(s[start..]);
        return parts;
    }

    /// <summary>A switch value: 1, true, yes or on.</summary>
    public static bool Switch(string s) => s.ToLowerInvariant() is "1" or "true" or "yes" or "on";
}
