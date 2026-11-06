using Meitou.Data.Characters;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Bodies;
using Meitou.Simulation;
using System.Numerics;

namespace Meitou.Brutality;

/// <summary>Estimates a fixed head projection frame from the medically selected region and incoming side. No weapon/skin contact is available.</summary>
public static class HeadWoundPlacementPolicy
{
    /// <summary>The point is an interior bind-space ray anchor, not a measured contact. The renderer projects onto the original head surface.</summary>
    public static HeadWoundPlacement? Estimate(Vector2 travel, float victimYaw, int cutDirection, float force = 30, ulong variation = 0)
    {
        if (!float.IsFinite(force) || !float.IsFinite(travel.X) || !float.IsFinite(travel.Y) || !float.IsFinite(victimYaw)) return null;
        float length = travel.LengthSquared();
        if (!float.IsFinite(length) || length < 1e-8f) return null;
        travel /= MathF.Sqrt(length);
        float sine = MathF.Sin(victimYaw), cosine = MathF.Cos(victimYaw);
        var local = new Vector2(cosine * travel.X - sine * travel.Y, sine * travel.X + cosine * travel.Y);
        var normal = Vector3.Normalize(new Vector3(-local.X, 0.15f, -local.Y));
        var across = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, normal));
        var up = Vector3.Normalize(Vector3.Cross(normal, across));
        var tangent = cutDirection switch
        {
            1 or 4 or 5 or 7 => up,
            2 => Vector3.Normalize(-across * 0.83f + up * 0.55f),
            _ => Vector3.Normalize(across * 0.83f + up * 0.55f),
        };
        float height = 0;
        if (variation != 0)
        {
            float angle = (((variation >> 16) & 0xFFFF) / 65535f * 2 - 1) * (MathF.PI / 18); // at most ten degrees in the surface plane
            tangent = Vector3.Normalize(tangent * MathF.Cos(angle) + Vector3.Cross(normal, tangent) * MathF.Sin(angle));
            height = ((variation & 0xFFFF) / 65535f * 2 - 1) * 0.06f;
        }
        var size = Size(force);
        return new(new Vector3(0, 18.65f + height, 0), normal, tangent, size.Length, size.Width);
    }
    static (float Length, float Width) Size(float force)
    {
        float t = Math.Clamp((force - 15) / 45, 0, 1);
        return (0.9f + 0.3f * t, 0.85f + 0.45f * t);
    }

    /// <summary>A stronger later hit can enlarge the saved footprint; its anchor and blade axis remain fixed.</summary>
    public static HeadWoundPlacement Enlarge(in HeadWoundPlacement placement, float force)
    {
        if (!float.IsFinite(force)) return placement;
        var size = Size(force);
        return placement with { LengthScale = MathF.Max(placement.LengthScale, size.Length), WidthScale = MathF.Max(placement.WidthScale, size.Width) };
    }
}
