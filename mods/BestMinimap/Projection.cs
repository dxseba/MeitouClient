using System.Numerics;
using Meitou.Data.Ui;

namespace Meitou.BestMinimap;

/// <summary>
/// The minimap's view: its rectangle on screen, the world point at its centre (X, Z), pixels per world unit and its turn (radians clockwise on
/// screen; 0 north up, else minus the camera's heading, so the camera looks up). World +X is right and +Z down, as on the big map.
/// </summary>
readonly record struct Projection(UiRect Rect, Vector2 Centre, float Scale, float Angle, bool Round)
{
    public Vector2 ScreenCentre => new(Rect.X + Rect.Width / 2, Rect.Y + Rect.Height / 2);

    /// <summary>The radius of a round minimap, the half of the shorter side of a square or rectangle.</summary>
    public float Radius => MathF.Min(Rect.Width, Rect.Height) / 2;

    public Vector2 ToScreen(Vector2 world) => ScreenCentre + Turn((world - Centre) * Scale, Angle);

    public Vector2 ToWorld(Vector2 screen) => Centre + Turn(screen - ScreenCentre, -Angle) / Scale;

    /// <summary>A world direction (X, Z) as a screen angle, clockwise from up.</summary>
    public float ScreenAngle(Vector2 direction)
    {
        var d = Turn(direction, Angle);
        return MathF.Atan2(d.X, -d.Y);
    }

    /// <summary>Whether a screen point lies inside the minimap (within <paramref name="margin"/> of its edge).</summary>
    public bool Inside(Vector2 screen, float margin = 0)
    {
        if (Round) return Vector2.Distance(screen, ScreenCentre) <= Radius - margin;

        return screen.X >= Rect.X + margin && screen.X <= Rect.X + Rect.Width - margin
            && screen.Y >= Rect.Y + margin && screen.Y <= Rect.Y + Rect.Height - margin;
    }

    /// <summary>A screen point outside the minimap pulled onto its edge (inset by <paramref name="margin"/>), toward the centre.</summary>
    public Vector2 Pin(Vector2 screen, float margin)
    {
        var c = ScreenCentre;
        var d = screen - c;
        if (d.LengthSquared() < 1e-6f) return c;

        if (Round) return c + Vector2.Normalize(d) * (Radius - margin);

        float hx = Rect.Width / 2 - margin, hy = Rect.Height / 2 - margin;
        float t = MathF.Min(hx / MathF.Max(MathF.Abs(d.X), 1e-6f), hy / MathF.Max(MathF.Abs(d.Y), 1e-6f));
        return c + d * MathF.Min(1, t);
    }

    /// <summary><paramref name="v"/> turned by <paramref name="angle"/> radians clockwise on screen (y down).</summary>
    public static Vector2 Turn(Vector2 v, float angle)
    {
        float c = MathF.Cos(angle), s = MathF.Sin(angle);
        return new(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }
}
