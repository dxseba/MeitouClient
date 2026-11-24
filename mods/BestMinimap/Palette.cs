using System.Globalization;
using System.Numerics;

namespace Meitou.BestMinimap;

/// <summary>
/// Colours: the settings' <c>#RRGGBB</c> or <c>auto</c>, and the "auto" ones. Auto character and town colours are the big map's (MarkerPlayer,
/// MarkerAlly, MarkerNeutral, MarkerEnemy of <c>kenshi_colours.xml</c>, as Meitou's world map reads them); the auto frame colour is the
/// original's built-in fallback <c>#AFA68B</c> (<b>Observed</b>, a DLL string beside the defaults).
/// </summary>
static class Palette
{
    public static readonly Vector4 Player = Rgb(0x8dcdff), Ally = Rgb(0x5cb473), Neutral = Rgb(0xdfdfdf), Enemy = Rgb(0xe65139),
        Movement = Rgb(0x79b44b), Selected = new(0.652f, 0.903f, 1, 1), Frame = Rgb(0xafa68b), White = Vector4.One;

    /// <summary><paramref name="text"/> as a colour (<c>#RRGGBB</c>, the # optional); <paramref name="auto"/> for "auto" or a bad value.</summary>
    public static Vector4 Parse(string? text, Vector4 auto)
    {
        string s = (text ?? "").Trim().TrimStart('#');
        if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb)) return auto;

        return Rgb(rgb);
    }

    public static Vector4 Rgb(int rgb) => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1);

    public static Vector4 Faded(Vector4 colour, float alpha) => colour with { W = colour.W * alpha };
}
