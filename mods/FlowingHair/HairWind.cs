using System.Numerics;
using System.Runtime.CompilerServices;
using Meitou.Data.World;

namespace Meitou.FlowingHair;

/// <summary>
/// The wind on hair, in world units per second squared (the original's <c>WeatherDynamics.h</c> / <c>WindField.cpp</c> rules, facts only): the
/// weather's wind at the camera, its speed times the multiplier and gusting by <c>1 + gust (0.7 sin p + 0.3 sin(2.17 p + 1.3))</c> with
/// <c>p = 2 pi f t</c>, capped; or, without following the weather, the procedural wind. Shared by every character, as the original's is.
/// </summary>
static class HairWind
{
    /// <summary>The procedural wind's vertical part (the original's <c>WindSettings</c> default).</summary>
    const float Vertical = 0.5f;

    /// <summary>The weather the camera saw last frame (<c>WorldWeather.Update</c>); null before a world.</summary>
    public static volatile WeatherState? Weather;

    /// <summary>The wind's clock (game seconds: stopped while paused), and this frame's blended sample without and with the stress wind.</summary>
    static double s_time;
    static Vector3 s_sample;
    static volatile StrongBox<Vector3> s_current = new(Vector3.Zero), s_hair = new(Vector3.Zero);

    /// <summary>How far the stress wind is in (0 to 1), and whether its key holds it on.</summary>
    static float s_stress;
    public static volatile bool StressOn;

    /// <summary>The wind on rain and dust particles this frame (no stress wind, as the original's particle force).</summary>
    public static Vector3 Current => s_current.Value;

    /// <summary>The wind on hair this frame (the stress wind blended in).</summary>
    public static Vector3 Hair => s_hair.Value;

    /// <summary>The acceleration cap, raised toward the stress wind while it blows (the original's <c>accelerationCap</c>).</summary>
    public static float Cap(FlowingHairSettings s) =>
        s.MaximumAcceleration + (MathF.Max(s.MaximumAcceleration, s.StressAcceleration) - s.MaximumAcceleration) * s_stress;

    /// <summary>
    /// Once a frame: the clock runs unless paused, the sample eases toward the wind at <c>2 dt</c> a frame (the original's weather blend), and the
    /// stress wind blends in at 0.75 a second or out at 3 a second (frame time at most 0.1 s), a steady wind of the stress acceleration along the
    /// wind direction setting.
    /// </summary>
    public static void Tick(FlowingHairSettings s, float dt, bool paused)
    {
        if (paused || !(dt > 0)) dt = 0;

        s_time += Math.Min(dt, 0.25f);
        s_sample += (At(s, s_time) - s_sample) * Math.Clamp(dt * 2, 0, 1);
        float step = Math.Min(dt, 0.1f) * (StressOn ? 0.75f : 3);
        s_stress = Math.Clamp(s_stress + (StressOn ? step : -step), 0, 1);

        double direction = s.Direction * Math.PI / 180;
        var stress = new Vector3((float)Math.Cos(direction), 0, (float)Math.Sin(direction)) * s.StressAcceleration;
        s_current = new(s_sample);
        s_hair = new(Vector3.Lerp(s_sample, stress, s_stress));
    }

    static Vector3 At(FlowingHairSettings s, double time)
    {
        if (!s.Wind) return Vector3.Zero;

        return s.FollowWeather ? FromWeather(s, time) : Procedural(s, time);
    }

    static Vector3 FromWeather(FlowingHairSettings s, double time)
    {
        if (Weather is not { } w || !(w.WindSpeed > 0) || !(s.WeatherMultiplier > 0) || !(s.WeatherMaximum > 0)) return Vector3.Zero;

        var direction = w.WindDirection;
        if (direction.LengthSquared() < 1e-12f || !float.IsFinite(direction.X) || !float.IsFinite(direction.Y)) return Vector3.Zero;

        direction = Vector2.Normalize(direction);
        double phase = time * Math.Clamp(s.GustFrequency, 0.001, 1) * 2 * Math.PI;
        double gust = 1 + Math.Clamp(s.WeatherGust, 0, 1) * (0.7 * Math.Sin(phase) + 0.3 * Math.Sin(phase * 2.17 + 1.3));
        float magnitude = (float)Math.Min(s.WeatherMaximum, w.WindSpeed * s.WeatherMultiplier * gust);
        return new Vector3(direction.X * magnitude, 0, direction.Y * magnitude);
    }

    /// <summary>The procedural wind: a gusting magnitude and a wandering direction, with the original's small vertical part (0.5).</summary>
    static Vector3 Procedural(FlowingHairSettings s, double time)
    {
        double phase = time * Math.Max(0.001, s.GustFrequency) * 2 * Math.PI;
        double direction = (s.Direction + Math.Sin(phase * 0.37) * s.DirectionVariation) * Math.PI / 180;
        float gust = Math.Clamp(0.5f + (float)Math.Sin(phase) * 0.32f + (float)Math.Sin(phase * 2.17 + 1.3) * 0.18f, 0, 1);
        float magnitude = Math.Max(0, s.BaseWind) + Math.Max(0, s.GustWind) * gust;
        float vertical = Vertical * ((float)Math.Sin(phase * 1.41) * 0.55f + (float)Math.Sin(phase * 0.63 + 2.1) * 0.45f);
        return new Vector3((float)Math.Cos(direction) * magnitude, vertical, (float)Math.Sin(direction) * magnitude);
    }

    /// <summary>The wet hair's heaviness, 1 dry to 1 + 0.75 soaked (the original's <c>WetHairExtraWeight</c> default).</summary>
    public static float WetWeight(float wet) => 1 + Math.Clamp(wet, 0, 1) * 0.75f;

    /// <summary>Soaks in 20 s of full rain, dries in 120 s (the original's <c>WetHairSoakSeconds</c> / <c>WetHairDrySeconds</c> defaults).</summary>
    public static float Soak(float wet, float rain, float dt)
    {
        dt = Math.Min(dt, 0.25f);
        rain = Math.Clamp(rain, 0, 1);
        return Math.Clamp(wet + (rain > 0 ? rain * dt / 20 : -dt / 120), 0, 1);
    }
}
