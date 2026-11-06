using Meitou.Data.Characters;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Bodies;
using Meitou.Simulation;
using System.Numerics;

namespace Meitou.Brutality;

/// <summary>A fixed plane in the original body bind space: dot(Plane.xyz, position) + Plane.W = 0; positive and negative halves follow the plane. FullBody permits cuts through limbs.</summary>
public readonly record struct TorsoBisectionState(Vector4 Plane, bool FullBody = false)
{
    public bool IsValid
    {
        get
        {
            if (!float.IsFinite(Plane.X) || !float.IsFinite(Plane.Y) || !float.IsFinite(Plane.Z) || !float.IsFinite(Plane.W)) return false;
            float length = new Vector3(Plane.X, Plane.Y, Plane.Z).LengthSquared();
            // Transverse cuts retain the legacy upward limit; full-body planes may be vertical or steep.
            return float.IsFinite(length) && MathF.Abs(length - 1) <= 0.001f && (FullBody || Plane.Y / MathF.Sqrt(length) >= 0.5f);
        }
    }
}
