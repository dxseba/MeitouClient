using System.Globalization;
using System.Numerics;

namespace Meitou.VitalSense;

/// <summary>
/// The relation and bounty tier colours and the tier bounds, from the settings (the original's <c>*_color_hex</c> and <c>bounty_tier_*_max</c>):
/// a colour not #RRGGBB or #RRGGBBAA takes its default, as do the bounds when they are not strictly ascending (the DLL's rules). Parsed values
/// are kept per text, so a frame parses nothing new.
/// </summary>
static class VitalColours
{
    /// <summary>
    /// Neutral, ally, enemy, squad (<see cref="Relation"/> order), the defaults of <c>mod-config.defaults.json</c>.
    /// </summary>
    static readonly string[] RelationDefaults = ["#FFA64D", "#DEE85A", "#FF3333", "#40FF40"];

    /// <summary>
    /// Trivial to legendary, with their alpha.
    /// </summary>
    static readonly string[] TierDefaults = ["#B0B0B0C8", "#C9A57BD4", "#9FBD69E0", "#6FA6D9EC", "#FFC247F5", "#FF8A2BFA", "#E03131FF"];

    static readonly int[] BoundDefaults = [1999, 4999, 9999, 19999, 39999, 74999];

    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Vector4?> Parsed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// <paramref name="relation"/>'s colour.
    /// </summary>
    public static Vector4 Of(Relation relation)
    {
        var s = VitalSenseMod.Settings;
        string text = relation switch
        {
            Relation.Ally => s.AllyColour,
            Relation.Enemy => s.EnemyColour,
            Relation.Squad => s.SquadColour,
            _ => s.NeutralColour,
        };

        return Colour(text, RelationDefaults[(int)relation]);
    }

    /// <summary>
    /// Tier <paramref name="tier"/>'s colour (0 trivial to 6 legendary).
    /// </summary>
    public static Vector4 Tier(int tier)
    {
        var s = VitalSenseMod.Settings;
        string[] texts = [s.TrivialColour, s.LowColour, s.ModestColour, s.NotableColour, s.HighValueColour, s.EliteColour, s.LegendaryColour];
        int i = Math.Clamp(tier, 0, texts.Length - 1);
        return Colour(texts[i], TierDefaults[i]);
    }

    /// <summary>
    /// The tiers' upper bounds, trivial to elite; the defaults when the settings' are not strictly ascending.
    /// </summary>
    public static int[] Bounds()
    {
        var s = VitalSenseMod.Settings;
        int[] bounds = [s.TrivialMax, s.LowMax, s.ModestMax, s.NotableMax, s.HighValueMax, s.EliteMax];
        for (int i = 1; i < bounds.Length; i++)
        {
            if (bounds[i] <= bounds[i - 1]) return BoundDefaults;
        }

        return bounds;
    }

    static Vector4 Colour(string? text, string fallback) => Parse(text) ?? Parse(fallback)!.Value;

    /// <summary>
    /// #RRGGBB (alpha 1) or #RRGGBBAA; null otherwise.
    /// </summary>
    static Vector4? Parse(string? text) => text is null ? null : Parsed.GetOrAdd(text.Trim(), ParseNew);

    static Vector4? ParseNew(string text)
    {
        if (text.Length is not (7 or 9) || text[0] != '#') return null;
        if (!uint.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value)) return null;

        uint rgba = text.Length == 7 ? value << 8 | 0xFF : value;
        return new Vector4(((rgba >> 24) & 0xFF) / 255f, ((rgba >> 16) & 0xFF) / 255f, ((rgba >> 8) & 0xFF) / 255f, (rgba & 0xFF) / 255f);
    }
}
