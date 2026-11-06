using System.Numerics;

namespace Meitou.Brutality.Rendering;

internal static class StockTissueColour
{
    internal static (Vector3 Min, Vector3 Max) Bounds(IEnumerable<Vector3> vertices)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var vertex in vertices)
        {
            min = Vector3.Min(min, vertex);
            max = Vector3.Max(max, vertex);
        }
        return (min, max);
    }

    internal static Vector4 Recolour(Vector4 source, uint blood)
    {
        blood &= 0xFFFFFF;
        if (blood == 0x800000) return source;
        var tint = new Vector3(blood >> 16 & 255, blood >> 8 & 255, blood & 255) / 128;
        float chroma = MathF.Max(source.X - MathF.Min(source.Y, source.Z), 0);
        return new(Vector3.Clamp(new Vector3(source.X, source.Y, source.Z) + (tint - Vector3.UnitX) * chroma, Vector3.Zero, Vector3.One), source.W);
    }
}
