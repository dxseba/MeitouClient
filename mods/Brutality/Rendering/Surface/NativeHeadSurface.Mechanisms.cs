// Adapted from the integrated GPL-3.0-or-later renderer at 3a15eeb9f5523b9d106b40fd0ec99ccdb2658efa.
namespace Meitou.Brutality.Rendering.Surface;

static partial class NativeHeadSurface
{
    const float ThrustLength = 0.20f, ThrustWidth = 0.095f, ThrustHeavyLength = 0.24f, ThrustHeavyWidth = 0.12f;
    const float CrushLength = 0.30f, CrushWidth = 0.19f, CrushHeavyLength = 0.36f, CrushHeavyWidth = 0.23f;

    static (float HalfLength, float HalfWidth) Dimensions(int profile, int mechanism)
    {
        bool heavy = profile >= 3;
        if (mechanism == 2) return heavy ? (ThrustHeavyLength, ThrustHeavyWidth) : (ThrustLength, ThrustWidth);
        if (mechanism == 3) return heavy ? (CrushHeavyLength, CrushHeavyWidth) : (CrushLength, CrushWidth);
        var preset = Frame(profile);
        return (preset.HalfLength, preset.HalfWidth);
    }

    static float ShapeWidth(float u, float length, float width, int mechanism) => mechanism switch
    {
        2 => width * MathF.Pow(MathF.Max(0, 1 - MathF.Abs(u) / length), 0.8f) * (1 + 0.08f * MathF.Sin(u * 69)),
        3 => width * MathF.Pow(MathF.Max(0, 1 - MathF.Abs(u) / length), 0.55f) * (1 + 0.20f * MathF.Sin(u * 51) + 0.12f * MathF.Sin(u * 97) + 0.08f * MathF.Cos(u * 39)),
        _ => Width(u, length, width),
    };
    static float ShapeBend(float u, int mechanism) => mechanism switch
    {
        2 => 0.003f * MathF.Sin(u * 53),
        3 => 0.025f * MathF.Sin(u * 37) + 0.010f * MathF.Sin(u * 83),
        _ => Bend(u),
    };
    static float ShapeCleft(float u, float length, int mechanism) => mechanism switch
    {
        2 => 0.34f + 0.12f * MathF.Exp(-MathF.Pow(u / (length * 0.45f), 2)),
        3 => Math.Clamp(0.40f + 0.14f * MathF.Sin(u * 43) + 0.10f * MathF.Exp(-MathF.Pow(u / (length * 0.6f), 2)), 0.25f, 0.65f),
        _ => Cleft(u),
    };

    // Mechanism 0/1 deliberately keeps the approved slash's arithmetic and dimensions unchanged.
    static string ShapeShader => $$"""
        uint headMechanism(mat4 placement) { return uint(placement[3].y + 0.5); }
        void headShapePreset(uint profile, mat4 placement, inout float halfLength, inout float halfWidth)
        {
            uint mechanism = headMechanism(placement);
            if (mechanism == 2u) { halfLength = profile >= 3u ? {{F(ThrustHeavyLength)}} : {{F(ThrustLength)}}; halfWidth = profile >= 3u ? {{F(ThrustHeavyWidth)}} : {{F(ThrustWidth)}}; }
            else if (mechanism == 3u) { halfLength = profile >= 3u ? {{F(CrushHeavyLength)}} : {{F(CrushLength)}}; halfWidth = profile >= 3u ? {{F(CrushHeavyWidth)}} : {{F(CrushWidth)}}; }
        }
        float headShapeWidth(float u, float halfLength, float halfWidth, mat4 placement)
        {
            uint mechanism = headMechanism(placement);
            if (mechanism == 2u) return halfWidth * pow(max(0.0, 1.0 - abs(u) / halfLength), 0.8) * (1.0 + 0.08 * sin(u * 69.0));
            if (mechanism == 3u) return halfWidth * pow(max(0.0, 1.0 - abs(u) / halfLength), 0.55) * (1.0 + 0.20 * sin(u * 51.0) + 0.12 * sin(u * 97.0) + 0.08 * cos(u * 39.0));
            return halfWidth * pow(max(0.0, 1.0 - abs(u) / halfLength), 0.65) * (1.0 + 0.10 * sin(u * 47.0) + 0.06 * sin(u * 83.0));
        }
        float headShapeBend(float u, mat4 placement)
        {
            uint mechanism = headMechanism(placement);
            if (mechanism == 2u) return 0.003 * sin(u * 53.0);
            if (mechanism == 3u) return 0.025 * sin(u * 37.0) + 0.010 * sin(u * 83.0);
            return 0.008 * sin(u * 15.0) + 0.003 * sin(u * 37.0);
        }
        float headShapeCleft(float u, float halfLength, mat4 placement)
        {
            uint mechanism = headMechanism(placement);
            if (mechanism == 2u) { float t = u / (halfLength * 0.45); return 0.34 + 0.12 * exp(-t * t); }
            if (mechanism == 3u) { float t = u / (halfLength * 0.6); return clamp(0.40 + 0.14 * sin(u * 43.0) + 0.10 * exp(-t * t), 0.25, 0.65); }
            float t = (u - 0.12) / 0.20;
            return 0.24 + 0.20 * exp(-t * t);
        }
        """;
}
