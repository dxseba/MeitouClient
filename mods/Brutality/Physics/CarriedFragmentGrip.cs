namespace Meitou.Brutality.Physics;
internal static class CarriedFragmentGrip
{
    public static System.Numerics.Vector3 UpperArmProbe(System.Numerics.Vector3 shoulder, System.Numerics.Vector3 elbow)
    {
        var axis = elbow - shoulder;
        float length = axis.Length();
        if (length < 1e-6f) return shoulder + System.Numerics.Vector3.UnitY;
        axis /= length;
        var up = System.Numerics.Vector3.UnitY - axis * System.Numerics.Vector3.Dot(System.Numerics.Vector3.UnitY, axis);
        if (up.LengthSquared() < 1e-6f) up = System.Numerics.Vector3.UnitX - axis * axis.X;
        return (shoulder + elbow) * .5f + System.Numerics.Vector3.Normalize(up) * MathF.Max(.6f, length * .5f);
    }

    /// <summary>A retained fragment is supported near its top so its weight hangs below the upper arm.</summary>
    public static float UpperGripBand(float minimumY, float maximumY) => maximumY - MathF.Max(0.6f, (maximumY - minimumY) * 0.12f);

}
