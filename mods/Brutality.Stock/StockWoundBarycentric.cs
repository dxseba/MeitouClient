using System.Numerics;

namespace Meitou.Brutality.Physics;

internal sealed partial class RagdollSkinSurface
{
    internal static Vector3 ClosestBarycentric(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var ab = b - a; var ac = c - a; var ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return Vector3.UnitX;
        var bp = p - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return Vector3.UnitY;
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) { float t = d1 / (d1 - d3); return new(1 - t, t, 0); }
        var cp = p - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return Vector3.UnitZ;
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) { float t = d2 / (d2 - d6); return new(1 - t, 0, t); }
        float va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) { float t = (d4 - d3) / ((d4 - d3) + (d5 - d6)); return new(0, 1 - t, t); }
        float inv = 1 / (va + vb + vc), v = vb * inv, w = vc * inv;
        return new(1 - v - w, v, w);
    }
}
