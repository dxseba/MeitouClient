using System.Numerics;

namespace Meitou.FlowingHair;

/// <summary>
/// The force on one character's hair or tail, from its movement between frames, with its wetness (the original's <c>Plugin.cpp</c> rule, facts
/// only): <c>limit(inertia x scale + gravity x wet weight + wind / wet weight, cap) + airflow x scale / wet weight</c>, inertia
/// <c>(v_last - v) inertia / dt</c>, airflow <c>-v min(drag, maxMove / |v|)</c>; a tail takes inertia and airflow at 0.1. In model space.
/// </summary>
sealed class HairForce
{
    /// <summary>A move farther than this in one frame (a teleport, a load) starts the motion over.</summary>
    public const float Teleport = 10;

    Vector3 _lastPosition, _lastVelocity;

    /// <summary>How wet the hair is, 0 to 1.</summary>
    public float Wet { get; private set; }

    public bool Jumped(in Matrix4x4 world) => Vector3.Distance(world.Translation, _lastPosition) > Teleport;

    public void Start(in Matrix4x4 world) => (_lastPosition, _lastVelocity) = (world.Translation, Vector3.Zero);

    public Vector3 Model(FlowingHairSettings s, in Matrix4x4 world, float dt, Vector3 wind, float rain, float scale)
    {
        Wet = s.WetHair ? HairWind.Soak(Wet, rain, dt) : 0;
        var velocity = (world.Translation - _lastPosition) / dt;
        var inertia = (_lastVelocity - velocity) * (s.Inertia / dt);
        (_lastPosition, _lastVelocity) = (world.Translation, velocity);

        float wet = HairWind.WetWeight(Wet);
        var forces = Limited(inertia * scale + new Vector3(0, -s.Gravity * wet, 0) + wind / wet, HairWind.Cap(s));
        float speed = velocity.Length();
        var airflow = speed > 1e-6f && s.MovementDrag > 0 && s.MaximumMovementAcceleration > 0
            ? -velocity * (MathF.Min(s.MovementDrag, s.MaximumMovementAcceleration / speed) * scale / wet)
            : Vector3.Zero;

        return Matrix4x4.Invert(world, out var toModel) ? Vector3.TransformNormal(forces + airflow, toModel) : Vector3.Zero;
    }

    static Vector3 Limited(Vector3 v, float cap)
    {
        float length = v.Length();
        return length > cap && length > 0 ? v * (cap / length) : v;
    }
}
