using System.Numerics;

namespace Meitou.Data.Characters;

/// <summary>A permanent limb cut: distance along its two bind segments and an oblique tilt, independent of race size and transient physics.</summary>
public readonly record struct LimbCutState(int LimbSlot, float Fraction, Vector2 Slant)
{
    public bool IsValid => LimbSlot is >= 0 and <= 3 && float.IsFinite(Fraction) && Fraction is >= .08f and <= .92f
        && float.IsFinite(Slant.X) && float.IsFinite(Slant.Y) && Slant.LengthSquared() <= 1.2f * 1.2f;
}

/// <summary>The shared bind-space plane used by limb skin, its wound surface, and both collision fragments.</summary>
public static class LimbCutGeometry
{
    /// <summary>Positive signed distance retains the proximal stump. Scale applies uniformly to bind positions, as in a physics definition.</summary>
    public static bool TryPlane(SkeletonRig rig, in LimbCutState cut, out Vector4 plane, float scale = 1)
    {
        plane = default;
        if (!cut.IsValid || !float.IsFinite(scale) || scale <= 0) return false;
        string side = cut.LimbSlot % 2 == 0 ? "L" : "R";
        var (proximal, middle, distal) = cut.LimbSlot < 2 ? ("UpperArm", "Forearm", "Hand") : ("Thigh", "Calf", "Foot");
        if (rig.Handle($"Bip01 {side} {proximal}") is not int first
            || rig.Handle($"Bip01 {side} {middle}") is not int second
            || rig.Handle($"Bip01 {side} {distal}") is not int third) return false;
        var a = rig.BindPosition[first];
        var b = rig.BindPosition[second];
        var c = rig.BindPosition[third];
        float upper = Vector3.Distance(a, b), lower = Vector3.Distance(b, c);
        if (!Finite(a) || !Finite(b) || !Finite(c) || !float.IsFinite(upper + lower) || upper < 1e-5f || lower < 1e-5f) return false;

        float distance = (upper + lower) * cut.Fraction;
        var axis = distance <= upper ? (a - b) / upper : (b - c) / lower;
        var centre = distance <= upper ? Vector3.Lerp(a, b, distance / upper) : Vector3.Lerp(b, c, (distance - upper) / lower);
        // A fixed reference keeps the same slant stable across meshes and scales. Z-aligned chains use Y instead.
        var reference = MathF.Abs(Vector3.Dot(axis, Vector3.UnitZ)) < .9f ? Vector3.UnitZ : Vector3.UnitY;
        var across = Vector3.Normalize(Vector3.Cross(reference, axis));
        var forward = Vector3.Cross(axis, across);
        var normal = Vector3.Normalize(axis + across * cut.Slant.X + forward * cut.Slant.Y);
        float offset = -Vector3.Dot(normal, centre) * scale;
        if (!Finite(normal) || !float.IsFinite(offset)) return false;
        plane = new(normal, offset);
        return true;
    }

    static bool Finite(Vector3 point) => float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);
}
