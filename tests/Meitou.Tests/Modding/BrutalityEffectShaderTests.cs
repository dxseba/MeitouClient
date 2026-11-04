using System.Numerics;
using Meitou.Brutality.Effects;
using Meitou.Rendering.Gpu.Shaders;

namespace Meitou.Tests.Modding;

public sealed class BrutalityEffectShaderTests
{
    [Fact]
    public void Electrical_strokes_and_the_shader_share_the_reference_negative_colour_recolour_contract()
    {
        var strokes = new RobotDischargeStroke[RobotDischargeGeometry.QuadLimit];
        int count = RobotDischargeGeometry.Build([new(Vector3.Zero, .1f, 25, 7, 1.6f)], strokes);
        var active = strokes.AsSpan(0, count).ToArray();
        Assert.Contains(active, stroke => stroke.Arc && stroke.Colour.X < 0 && stroke.Colour.Y < 0 && stroke.Colour.Z < 0);
        // The reference's negative channels replace texture hue with brightness before applying an absolute tint.
        // Keeping this ABI is essential: ordinary multiplication sends negative electrical light to additive blending.
        string shader = GoreEffectShaders.Fragment;
        int recolour = shader.IndexOf("if (min(vColour.r, min(vColour.g, vColour.b)) < 0.0) t.rgb = vec3(max(t.r, max(t.g, t.b)));", StringComparison.Ordinal);
        int tint = shader.IndexOf("vec4 c = t * abs(vColour);", StringComparison.Ordinal);
        int premultiply = shader.IndexOf("if (additive) c.rgb *= c.a;", StringComparison.Ordinal);
        Assert.True(recolour >= 0 && tint > recolour && premultiply > tint);
    }

    [Fact, Slow]
    public void Liquid_and_electrical_stages_compile_on_the_shared_legacy_shader_transport()
    {
        var compiler = new GlslProgramCompiler(new ShaderCompileOptions { UseDiskCache = false });
        var program = compiler.Compile(GoreEffectShaders.Vertex, GoreEffectShaders.Fragment);
        Assert.NotNull(program);
    }
}
