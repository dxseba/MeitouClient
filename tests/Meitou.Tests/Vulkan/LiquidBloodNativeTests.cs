using System.Numerics;
using Meitou.Data.Particles;
using Meitou.Data.Textures;
using Meitou.Rendering;
using Meitou.Rendering.Gpu;
using Meitou.Rendering.Gpu.Core;

namespace Meitou.Tests.Vulkan;

[Collection("StageClock")]
public class LiquidBloodNativeTests
{
    const int Size = 256;
    static readonly Vector4 Blood = new(1, 0, 0, 1);

    [Fact]
    [Slow]
    public void Actual_particle_streams_render_visible_round_flow_with_unbroken_straight_and_curved_joins()
    {
        VulkanDevice? d;
        try { d = VulkanDevice.Create(new VulkanDeviceOptions { Validation = true, SyncValidation = true }); }
        catch (Exception e) when (e is VulkanException or DllNotFoundException or EntryPointNotFoundException
            or Silk.NET.Core.Loader.SymbolLoadingException) { d = null; }
        using var device = d;
        Assert.SkipWhen(device is null, "No Vulkan 1.3 device");
        using (var ctx = new GpuContext(device!))
        using (var particles = new ParticleRenderer(ctx, _ => null, new ParticleLibrary()))
        using (var target = ctx.CreateTexture(new TextureDesc(InternalFormat.Rgba8, Size, Size,
            Use: TextureUse.ColourTarget | TextureUse.TransferSrc, Name: "liquid stream test picture")))
        {
            ShadowsOffGlobals.Publish(ctx);
            LiquidBloodStream[] straight =
            [
                new(new(-2.5f, 1, 0), new(-1, 1, 0), 0.24f, Blood, 0.24f),
                new(new(-1, 1, 0), new(0.5f, 1, 0), 0.24f, Blood, 0.24f),
            ];
            var image = Draw(ctx, device!, particles, target, straight);
            AssertVisible(image);
            AssertContinuous(image, straight[0].Tail);
            Export("liquid-straight.png", image);

            LiquidBloodStream[] curved =
            [
                new(new(-2.5f, -1, 0), new(-1, -1.5f, 0), 0.28f, Blood, 0.24f),
                new(new(-1, -1.5f, 0), new(0, -0.5f, 0), 0.24f, Blood, 0.18f),
                new(new(0, -0.5f, 0), new(1.5f, -0.2f, 0), 0.18f, Blood, 0.10f),
            ];
            image = Draw(ctx, device!, particles, target, curved);
            AssertVisible(image);
            AssertContinuous(image, curved[0].Tail);
            AssertContinuous(image, curved[1].Tail);
            Export("liquid-curved.png", image);
        }
        Assert.True(device!.ValidationErrors == 0, string.Join("\n", device.ValidationLog));
    }

    static byte[] Draw(GpuContext ctx, VulkanDevice device, ParticleRenderer particles, GpuTexture target,
        LiquidBloodStream[] streams)
    {
        particles.LiquidStreams.Clear();
        particles.LiquidStreams.AddRange(streams);
        var eye = new Vector3(0, 0, 10);
        var view = Matrix4x4.CreateLookAt(eye, Vector3.Zero, Vector3.UnitY);
        var vp = view * Matrix4x4.CreateOrthographic(8, 8, 0.1f, 100);
        ctx.EnsureFrame();
        particles.Prepare(vp, vp, 0.1f, view, eye, Vector3.UnitY, Size / 8f, Size * Size);
        Assert.Equal(streams.Length, particles.DrawnLiquidBlood);
        Assert.Equal(1, particles.FullCount);
        var targets = PassTargets.Of(target, null);
        var cmd = ctx.BeginNative("liquid stream test scene");
        cmd.BeginRendering(targets.Rendering with
        {
            Colour = targets.Colour with { Load = LoadOp.Clear, Clear = new ClearColour(0.05f, 0.08f, 0.1f, 1) },
        });
        ctx.BeginHostPass((CommandList)cmd, targets, DrawState.For(targets.Formats, device.DepthClamp));
        particles.Record(1);
        ctx.EndHostPass(cmd);
        cmd.EndRendering();
        ctx.EndNative(cmd);
        ctx.Finish();
        return ctx.ReadBack(target, 4);
    }

    static void AssertVisible(byte[] image)
    {
        int red = 0;
        for (int i = 0; i < image.Length; i += 4)
            if (image[i] > 100 && image[i] > image[i + 1] * 3 && image[i] > image[i + 2] * 3) red++;
        Assert.InRange(red, 150, 3000);
    }

    static void AssertContinuous(byte[] image, Vector3 joint)
    {
        int x = (int)MathF.Round((joint.X / 8 + 0.5f) * Size);
        int y = (int)MathF.Round((joint.Y / 8 + 0.5f) * Size);
        // Both native viewport orientations are valid; only the projected location is searched, never another stream.
        foreach (int offset in new[] { -2, -1, 0, 1, 2 })
        {
            byte strength = 0;
            foreach (int row in new[] { y, Size - 1 - y })
                for (int dy = -2; dy <= 2; dy++)
                {
                    int i = ((row + dy) * Size + x + offset) * 4;
                    if (image[i + 1] < 40 && image[i + 2] < 40) strength = Math.Max(strength, image[i]);
                }
            Assert.True(strength > 100, $"Stream has a background gap at joint {joint}, column {offset}: red {strength}");
        }
    }

    static void Export(string name, byte[] image)
    {
        if (Environment.GetEnvironmentVariable("MEITOU_LIQUID_TEST_DIRECTORY") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        PngWriter.Write(Path.Combine(directory, name), Size, Size, image);
    }
}
