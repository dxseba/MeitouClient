// Adapted from the integrated GPL-3.0-or-later renderer at 3a15eeb9f5523b9d106b40fd0ec99ccdb2658efa.
namespace Meitou.Brutality.Rendering.Surface;
internal static class NativeTorsoSurface
{
    public const string ShaderField = """
        const float torsoExposureDepth = 2.8;
        float torsoCutRagged(vec3 p) { return 0.018 * sin(p.x * 13.0 + p.y * 7.0 + p.z * 11.0) + 0.012 * sin(p.x * 29.0 + p.z * 17.0); }
        """;
}
