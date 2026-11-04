using System.Numerics;
using System.Runtime.InteropServices;
using Meitou.Brutality.Rendering.Surface;
using Meitou.Rendering.Characters;

namespace Meitou.Tests.Modding;

public sealed partial class BrutalitySurfacePackingTests
{
    [Theory]
    [InlineData(0x800000u)]
    [InlineData(0xC48814u)]
    [InlineData(0x2580C8u)]
    [InlineData(0x002020u)]
    [InlineData(0u)]
    public void Race_rgb_occupies_its_own_palette_word_without_overwriting_native_material_channels_or_cut_state(uint rgb)
    {
        var palette = NativeSurfaceBridge.Pack(Rig(), State(null, new Meitou.Brutality.LimbCutState(0, .4f, new(.2f, -.1f))), 10, 0, null, null, default);
        var words = MemoryMarshal.Cast<Matrix4x4, uint>(palette.AsSpan());
        Assert.Equal(0x800000u, words[29]);
        var before = words.ToArray();
        NativeSurfaceBridge.SetBloodColour(palette, rgb | 0xFF000000u);
        for (int word = 0; word < words.Length; word++) Assert.Equal(word == 29 ? rgb : before[word], words[word]);
        var decoded = new Vector3((words[29] >> 16) & 255, (words[29] >> 8) & 255, words[29] & 255) / 255;
        Assert.Equal(new Vector3((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255) / 255, decoded);
        Assert.NotEqual(0u, words[28]); // The independent limb plane mask remains usable.
    }

    [Fact]
    public void Ported_hair_lookup_uses_the_current_hosts_material_channel_while_wound_colours_use_the_palette()
    {
        string host = CharacterShaders.Fragment();
        string port = NativeSurfaceShaders.Fragment();
        static string HairLookup(string source) => System.Text.RegularExpressions.Regex.Match(source, @"vec4 hairTex\(CharMaterial m, vec2 uv\)\s*\{[^}]*\}").Value;
        Assert.Equal(HairLookup(host), HairLookup(port));
        Assert.Contains("uint rgb = vRaceRgb;", port);
        Assert.DoesNotContain("uint(m.hairColour.w)", port);
        var vertex = NativeSurfaceShaders.Vertex();
        Assert.Contains("flat out uint vRaceRgb;", vertex);
        Assert.Contains("flat in uint vRaceRgb;", port);
        Assert.Contains("[3][1]", vertex); // word29 relative to the legacy header matrix
    }
}
