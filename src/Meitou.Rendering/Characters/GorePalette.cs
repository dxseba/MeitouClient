using System.Numerics;

namespace Meitou.Rendering.Characters;

/// <summary>Blood-soaked soft tissue follows the race's fluid; neutral bone and fat keep their own palettes.</summary>
public static class GorePalette
{
    public const uint DefaultBlood = 0x800000;

    public static Vector3 Recolour(Vector3 source, uint bloodColour = DefaultBlood)
    {
        bloodColour &= 0xFFFFFF;
        if (bloodColour == DefaultBlood) return source;
        var blood = new Vector3(bloodColour >> 16 & 255, bloodColour >> 8 & 255, bloodColour & 255) / 128;
        float chroma = MathF.Max(source.X - MathF.Min(source.Y, source.Z), 0);
        return Vector3.Clamp(source + (blood - Vector3.UnitX) * chroma, Vector3.Zero, Vector3.One);
    }

    public static Vector4 Recolour(Vector4 source, uint bloodColour = DefaultBlood) =>
        new(Recolour(new Vector3(source.X, source.Y, source.Z), bloodColour), source.W);
}
