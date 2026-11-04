using Meitou.Brutality.Rendering.Surface;
using Meitou.Rendering.Characters;
using Meitou.Rendering.Gpu.Shaders;

namespace Meitou.Tests.Modding;

/// <summary>The actual ported programs, including the host's GI colour wrapper, must compile on the shared native character ABI.</summary>
public class NativeSurfaceShaderTests
{
    [Theory, Slow]
    [InlineData("colour", true)]
    [InlineData("colour", false)]
    [InlineData("depth", true)]
    [InlineData("motion", true)]
    public void Ported_surface_stages_compile_with_host_helpers_and_colour_wrapping(string pass, bool lateDepth)
    {
        var set = NativeSurfaceShaders.CreateSet();
        Assert.Same(set, NativeSurfaceShaders.CreateSet());
        var (vertex, fragment) = pass switch
        {
            "colour" => (set.ColourVertex, CharacterRenderer.RuntimeColourFragment(lateDepth ? set.ColourFragment : NativeSurfaceShaders.Fragment(false))),
            "depth" => (set.DepthVertex, set.DepthFragment),
            _ => (set.MotionVertex, set.MotionFragment),
        };
        var compiler = new GlslProgramCompiler(new ShaderCompileOptions { UseDiskCache = false });
        Assert.NotEmpty(compiler.CompileNative(vertex, fragment: false));
        Assert.NotEmpty(compiler.CompileNative(fragment, fragment: true));
    }
}
