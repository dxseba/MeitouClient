using System.Numerics;

namespace Meitou.Rendering.Characters;

static partial class TorsoCutSurface
{
    static bool SectionInside(Vector2 point, Vector2[] outline)
    {
        bool inside = false;
        for (int i = 0, j = outline.Length - 1; i < outline.Length; j = i++)
        {
            var a = outline[i]; var b = outline[j]; var step = b - a;
            float length = step.LengthSquared();
            float t = length < 1e-12f ? 0 : Math.Clamp(Vector2.Dot(point - a, step) / length, 0, 1);
            if (Vector2.DistanceSquared(point, a + step * t) <= 0.002f * 0.002f) return true;
            if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }
}
