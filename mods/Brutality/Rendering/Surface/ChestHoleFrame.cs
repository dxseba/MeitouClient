using System.Numerics;
using Meitou.Data.Characters;

namespace Meitou.Brutality.Rendering.Surface;

/// <summary>
/// A punch-through hole resolved against one rig's bind pose: the frame the shader's prefix and the lining share. Distances across the
/// axis are measured on the hole's ellipse: a point at elliptic radius r lies r x <see cref="Aspect"/> along <c>Major</c> and r / Aspect
/// along <c>Minor</c>, the measure <see cref="ChestHoleShader"/> uses.
/// </summary>
internal readonly record struct ChestHoleFrame(Vector3 Centre, Vector3 Axis, float Radius, float Seed, float Aspect = 1, float Tilt = 0,
    float Tear = 1, float Flare = 1, int Region = ChestHoleState.Chest)
{
    /// <summary>
    /// The ragged edge's largest outward reach at Tear 1, as a fraction of the radius: the sine amplitudes of <see cref="ChestHoleShader"/>.
    /// </summary>
    public const float Ragged = .22f;
    /// <summary>The lining's clearance outside the most ragged edge (bind units).</summary>
    const float Margin = .06f;

    public static ChestHoleFrame? Resolve(SkeletonRig rig, in ChestHoleState hole)
    {
        if (!hole.IsValid || rig.Handle("Bip01 Pelvis") is not { } pelvis || rig.Handle("Bip01 Neck") is not { } neck
            || pelvis >= rig.BindPosition.Length || neck >= rig.BindPosition.Length) return null;

        var low = rig.BindPosition[pelvis];
        var high = rig.BindPosition[neck];
        float length = Vector3.Distance(low, high);
        if (!float.IsFinite(length) || length < 1e-3f) return null;

        var centre = Vector3.Lerp(low, high, hole.Height) + new Vector3(hole.Across * length, 0, 0);
        var axis = Vector3.Normalize(new Vector3(MathF.Sin(hole.Yaw), 0, -hole.Entry * MathF.Cos(hole.Yaw)));
        return new(centre, axis, hole.Radius * length, hole.Seed, hole.Aspect, hole.Tilt, hole.Tear, hole.Flare, hole.Region);
    }

    /// <summary>
    /// The shader's hole matrix: column 0 the centre and radius, 1 the axis and ragged seed, 2 the aspect, tilt, tear and flare, 3 the region.
    /// </summary>
    public Matrix4x4 Packed => new(
        Centre.X, Centre.Y, Centre.Z, Radius,
        Axis.X, Axis.Y, Axis.Z, Seed,
        Aspect, Tilt, Tear, Flare,
        Region, 0, 0, 1);

    /// <summary>How much wider the opening is at <paramref name="along"/> (bind units from the centre, towards the exit): 1 entering.</summary>
    public float Flared(float along)
    {
        float span = 1.3f * Radius, x = Math.Clamp((along + span) / (2 * span), 0, 1);
        return 1 + (Flare - 1) * x * x * (3 - 2 * x);
    }

    /// <summary>The lining's elliptic radius at a depth: just outside the most ragged edge there.</summary>
    public float LiningRadius(float along) => Radius * (1 + Ragged * Tear) * Flared(along) + Margin;

    /// <summary>The offset from the axis at a turn round it and an elliptic radius.</summary>
    public Vector3 Across(float turn, float radius)
    {
        var (major, minor) = Basis();
        return major * (MathF.Cos(turn) * radius * Aspect) + minor * (MathF.Sin(turn) * radius / Aspect);
    }

    /// <summary>The ellipse's axes: the shader's side and up directions turned by <see cref="Tilt"/>.</summary>
    public (Vector3 Major, Vector3 Minor) Basis()
    {
        var side = Vector3.Normalize(Vector3.Cross(Axis, Vector3.UnitY));
        var up = Vector3.Cross(side, Axis);
        float c = MathF.Cos(Tilt), s = MathF.Sin(Tilt);
        return (side * c + up * s, up * c - side * s);
    }
}
