using System.Numerics;

namespace Meitou.Simulation.Wounds;

/// <summary>Estimates a fixed head projection frame from the medically selected region and incoming side. No weapon/skin contact is available.</summary>
public static class HeadWoundPlacementPolicy
{
    /// <summary>The point is an interior bind-space ray anchor, not a measured contact. The renderer projects onto the original head surface.</summary>
    public static HeadWoundPlacement? Estimate(Vector2 travel, float victimYaw, int cutDirection)
    {
        if (!float.IsFinite(travel.X) || !float.IsFinite(travel.Y) || !float.IsFinite(victimYaw)) return null;
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
        return new(new Vector3(0, 18.65f, 0), normal, tangent);
    }
}
