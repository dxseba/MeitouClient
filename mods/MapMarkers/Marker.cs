using System.Numerics;
using System.Text;

namespace Meitou.MapMarkers;

/// <summary>A marker's kind: its glyph, colour and name (the original's ten, in its cycle order; the numbers are its saved indices).</summary>
public enum MarkerKind
{
    Note = 0,
    Danger = 1,
    Stash = 2,
    Ruin = 3,
    Mine = 4,
    Base = 5,
    Trader = 6,
    SafeSpot = 7,
    Quest = 8,
    Todo = 9,
}

/// <summary>One marker: its id (1 and up), its point on the map image (0..1 each way, +X right, +Y down), its kind and its label.</summary>
public sealed class Marker(int id, Vector2 at, MarkerKind kind, string label)
{
    public int Id { get; } = id;

    public Vector2 At { get; set; } = at;

    public MarkerKind Kind { get; set; } = kind;

    public string Label { get; set; } = label;
}

/// <summary>What each kind looks like and how labels are cleaned (the original's tables, MapMarkersData.cpp).</summary>
static class MarkerKinds
{
    /// <summary>The longest label, in characters.</summary>
    public const int LabelMax = 48;

    static readonly string[] Glyphs = ["n", "!", "s", "r", "m", "b", "$", "+", "q", "t"];

    static readonly string[] Names =
    [
        "Note", "Danger", "Stash", "Ruin", "Mine / Resource", "Base / Outpost", "Trader / Shop", "Safe Spot / Bed / Recovery", "Quest", "Todo",
    ];

    static readonly Vector4[] Normal =
    [
        new(.18f, .78f, .20f, 1), new(.88f, .18f, .18f, 1), new(.18f, .42f, .95f, 1), new(.92f, .82f, .18f, 1), new(.95f, .54f, .16f, 1),
        new(.84f, .84f, .84f, 1), new(.18f, .84f, .84f, 1), new(.52f, .90f, .40f, 1), new(.60f, .30f, .88f, 1), new(.54f, .54f, .54f, 1),
    ];

    static readonly Vector4[] Selected =
    [
        new(.58f, 1, .58f, 1), new(1, .45f, .40f, 1), new(.44f, .70f, 1, 1), new(1, .96f, .38f, 1), new(1, .72f, .30f, 1),
        new(1, 1, 1, 1), new(.48f, 1, 1, 1), new(.78f, 1, .68f, 1), new(.82f, .52f, 1, 1), new(.78f, .78f, .78f, 1),
    ];

    /// <summary>
    /// The fork's Colour row in order (<b>Verified</b>, original DLL rdata table): "" is the "-" button that removes the tag, then white, red,
    /// orange, yellow, green, cyan, blue, purple, pink, grey.
    /// </summary>
    public static readonly string[] Swatches =
        ["", "#FFFFFF", "#FF5A4E", "#FF9F45", "#FFD84D", "#7BE07B", "#5CD6E8", "#6FA8FF", "#C38BFF", "#FF7FC8", "#A0A0A0"];

    public static uint SwatchRgba(int i) => Convert.ToUInt32(Swatches[i][1..], 16) << 8 | 0xFF;

    /// <summary>
    /// A swatch press: the label's leading <c>#RRGGBB</c> tag (7 characters, not <c>##</c>) replaced by the swatch's, or dropped for "-"
    /// (<b>Verified</b>, original DLL (decompiled)); MyGUI draws the label in that colour.
    /// </summary>
    public static string WithColour(string label, int swatch) => Swatches[swatch] + StripTag(label);

    static string StripTag(string label) =>
        label.Length > 6 && label[0] == '#' && label[1] != '#' && label.AsSpan(1, 6).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0
            ? label[7..]
            : label;

    /// <summary>The text without MyGUI colour tags, for measuring.</summary>
    public static string WithoutTags(string text)
    {
        var plain = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '#' && i + 1 < text.Length && text[i + 1] == '#')
            {
                plain.Append('#');
                i++;
                continue;
            }

            if (text[i] == '#' && i + 6 < text.Length && text.AsSpan(i + 1, 6).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0)
            {
                i += 6;
                continue;
            }

            plain.Append(text[i]);
        }

        return plain.ToString();
    }

    public static string Glyph(MarkerKind kind) => Glyphs[Index(kind)];

    public static string Name(MarkerKind kind) => Names[Index(kind)];

    public static Vector4 Colour(MarkerKind kind, bool selected) => (selected ? Selected : Normal)[Index(kind)];

    /// <summary>The next kind in the type button's cycle (Todo wraps to Note).</summary>
    public static MarkerKind Next(MarkerKind kind) => (MarkerKind)((Index(kind) + 1) % Glyphs.Length);

    /// <summary>A saved index as a kind; one out of range is a Note.</summary>
    public static MarkerKind FromIndex(int index) => index is >= 0 and <= 9 ? (MarkerKind)index : MarkerKind.Note;

    static int Index(MarkerKind kind) => kind is >= MarkerKind.Note and <= MarkerKind.Todo ? (int)kind : 0;

    /// <summary>"Kind: label", or the kind alone without a label (the hover caption).</summary>
    public static string HoverCaption(Marker marker) =>
        marker.Label.Length == 0 ? Name(marker.Kind) : Name(marker.Kind) + ": " + marker.Label;

    /// <summary>"Marker N: label", or "Marker N" without a label (the editor's header).</summary>
    public static string Header(Marker marker) =>
        marker.Label.Length == 0 ? $"Marker {marker.Id}" : $"Marker {marker.Id}: {marker.Label}";

    /// <summary>
    /// A label as the original keeps it: tabs and line breaks become spaces, other control characters go, square and curly brackets become round
    /// ones, no leading or doubled spaces, at most <see cref="LabelMax"/> characters; trailing spaces only go on <paramref name="trimEnd"/>.
    /// </summary>
    public static string Clean(string text, bool trimEnd)
    {
        var clean = new StringBuilder(Math.Min(text.Length, LabelMax));
        foreach (char raw in text)
        {
            char c = Replace(raw);
            if (c == '\0' || c == ' ' && (clean.Length == 0 || clean[^1] == ' ')) continue;

            clean.Append(c);
            if (clean.Length >= LabelMax) break;
        }

        return trimEnd ? clean.ToString().TrimEnd(' ') : clean.ToString();
    }

    static char Replace(char c) => c switch
    {
        '\r' or '\n' or '\t' => ' ',
        '[' or '{' => '(',
        ']' or '}' => ')',
        _ when char.IsControl(c) => '\0',
        _ => c,
    };
}
