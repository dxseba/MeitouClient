using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Particles;
using Meitou.Modding;

namespace Meitou.FlowingHair;

/// <summary>
/// The wind on rain and dust (the original's <c>RainAndDust</c>, facts only): it gives every technique of a particle system whose template name
/// holds "rain", "dust" or "sandstorm" a LinearForce affector adding the wind (horizontal, times <c>ParticleWindScale</c>) to the particles'
/// velocity. Here a postfix on <c>ParticleSimulation.Affect</c> (each technique's affectors, every step) adds the same force after the
/// technique's own affectors. Display only: the weather particles are drawn, never simulated.
/// </summary>
static class ParticlePatches
{
    static readonly ConditionalWeakTable<PuSystemDef, StrongBox<bool>> Windy = new();

    static Func<object, int>? s_count;
    static Func<object, float[]>? s_vx, s_vz;
    static ModContext? s_context;
    static int s_faulted;

    public static void Install(ModContext context)
    {
        s_context = context;
        var simulation = typeof(ParticleSimulation);
        var technique = Access.Nested(simulation, "TechniqueState");
        s_count = Access.Getter<int>(technique, "Count");
        s_vx = Access.Getter<float[]>(technique, "Vx");
        s_vz = Access.Getter<float[]>(technique, "Vz");
        context.Harmony.Patch(Access.Method(simulation, "Affect", 3), postfix: new HarmonyMethod(typeof(ParticlePatches), nameof(AffectPostfix)));
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void AffectPostfix(ParticleSimulation __instance, object t, float dt)
    {
        try
        {
            Push(__instance, t, dt);
        }
        catch (Exception e)
        {
            if (Interlocked.Exchange(ref s_faulted, 1) == 0)
                s_context?.LogError($"pushing particles with the wind failed; they move as without the mod: {e}");
        }
    }
#pragma warning restore IDE1006

    static void Push(ParticleSimulation simulation, object technique, float dt)
    {
        var s = FlowingHairMod.Settings;
        if (!s.Wind || !s.RainAndDust || !(dt > 0 && dt < 1) || !IsWindy(simulation.Definition)) return;

        var wind = HairWind.Current * s.ParticleWindScale;
        if (wind.X == 0 && wind.Z == 0) return;

        int n = s_count!(technique);
        var vx = s_vx!(technique);
        var vz = s_vz!(technique);
        float fx = wind.X * dt, fz = wind.Z * dt;
        for (int i = 0; i < n && i < vx.Length && i < vz.Length; i++)
        {
            vx[i] += fx;
            vz[i] += fz;
        }
    }

    static bool IsWindy(PuSystemDef definition) => Windy.GetValue(definition, d =>
    {
        string name = d.Name.ToLowerInvariant();
        return new StrongBox<bool>(name.Contains("rain") || name.Contains("dust") || name.Contains("sandstorm"));
    }).Value;
}
