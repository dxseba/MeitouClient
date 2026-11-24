using System.Text;

namespace Meitou.OrganizeCraftingStations;

/// <summary>
/// The original's text match: query and row name are both lower-cased, every run of characters that are neither letters nor digits becomes one
/// space and the ends are trimmed; a row matches when its name contains the query (an empty query matches every row).
/// </summary>
static class SearchText
{
    /// <summary>
    /// <paramref name="text"/> normalized for matching.
    /// </summary>
    public static string Normalize(string text)
    {
        var result = new StringBuilder(text.Length);
        bool gap = false;
        foreach (char c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                if (gap && result.Length > 0) result.Append(' ');
                result.Append(char.ToLowerInvariant(c));
                gap = false;
            }
            else gap = true;
        }

        return result.ToString();
    }

    /// <summary>
    /// Whether <paramref name="name"/> matches the normalized <paramref name="query"/>.
    /// </summary>
    public static bool Matches(string name, string query) =>
        query.Length == 0 || Normalize(name).Contains(query, StringComparison.Ordinal);
}
