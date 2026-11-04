using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Meitou.Brutality.Effects;
using Meitou.Brutality.Rendering;
using Meitou.Content;
using Meitou.Data;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;
using Meitou.Rendering;
using Meitou.Rendering.Characters;
using Meitou.Rendering.Gpu;
using Meitou.Rendering.Gpu.Core;
using Meitou.Rendering.Gpu.Shaders;

namespace Meitou.Tests.Modding;

[Collection("StageClock")] // Native effect recording shares the host pass clocks and GPU test lifecycle.
public sealed class BrutalityResourceLifetimeTests
{
    static readonly MethodInfo Load = typeof(CharacterContent).GetMethod("Mesh", BindingFlags.Instance | BindingFlags.NonPublic)!;

    [Fact]
    public void A_retired_variant_keeps_a_shared_live_upload_and_waits_for_in_flight_frames_before_deleting_the_other_mesh()
    {
        using var fixture = new SyntheticContent();
        var gpu = fixture.Gpu; var content = fixture.Content;
        var shared = Triangle(0); var unique = Triangle(4);
        var first = new RuntimeCharacterGeometry(shared); var second = new RuntimeCharacterGeometry(shared); var old = new RuntimeCharacterGeometry(unique);
        var sharedUpload = Upload(content, shared); var uniqueUpload = Upload(content, unique);
        int[] sharedBuffers = Buffers(sharedUpload), uniqueBuffers = Buffers(uniqueUpload);
        Assert.Same(sharedUpload, Upload(content, shared));
        first.Retire(); old.Retire();
        content.SweepRuntime([second], new HashSet<uint>());
        Assert.Equal(1, content.MeshCount);
        Assert.All(sharedBuffers.Concat(uniqueBuffers), id => Assert.True(gpu.Memory.ContainsKey(id)));
        gpu.Advance(gpu.FramesInFlight - 1);
        Assert.All(uniqueBuffers, id => Assert.True(gpu.Memory.ContainsKey(id)));
        gpu.Advance(1);
        Assert.All(uniqueBuffers, id => Assert.False(gpu.Memory.ContainsKey(id)));
        Assert.All(sharedBuffers, id => Assert.True(gpu.Memory.ContainsKey(id)));
        Assert.Same(sharedUpload, Upload(content, shared));
        second.Retire(); content.SweepRuntime([], new HashSet<uint>()); gpu.Advance(gpu.FramesInFlight);
        Assert.Equal(0, content.MeshCount); Assert.All(sharedBuffers, id => Assert.False(gpu.Memory.ContainsKey(id)));
    }

    [Fact]
    public void A_hundred_generated_wound_variants_keep_gpu_uploads_bounded_when_the_producer_retires_old_geometry()
    {
        using var fixture = new SyntheticContent();
        var gpu = fixture.Gpu; var content = fixture.Content;
        long baseline = gpu.Bytes, one = 0, peak = 0;
        RuntimeCharacterGeometry? previous = null;
        for (int i = 0; i < 100; i++)
        {
            var mesh = Triangle(i);
            var current = new RuntimeCharacterGeometry(mesh);
            _ = Upload(content, mesh);
            if (i == 0) one = gpu.Bytes - baseline;
            previous?.Retire(); content.SweepRuntime([current], new HashSet<uint>());
            Assert.Equal(1, content.MeshCount);
            peak = Math.Max(peak, gpu.Bytes - baseline);
            gpu.Advance(1); previous = current;
        }
        Assert.True(peak <= one * (gpu.FramesInFlight + 1), $"{peak} bytes retained for {one}-byte variants");
        previous!.Retire(); content.SweepRuntime([], new HashSet<uint>()); gpu.Advance(gpu.FramesInFlight);
        Assert.Equal(0, content.MeshCount); Assert.Equal(baseline, gpu.Bytes);
    }

    [Fact]
    public void Supplied_runtime_index_levels_are_uploaded_and_projected_size_matches_the_pinned_thresholds()
    {
        using var fixture = new SyntheticContent(); var mesh = Triangle(0);
        mesh.Lod = new() { Strategy = "distance_sphere" };
        for (int i = 1; i <= 3; i++)
        {
            var level = new OgreLodLevel { UserValue = i };
            level.Faces.Add(new(3, 0, null, new OgreIndexBuffer(true, [0, 1, 2]))); mesh.Lod.Levels.Add(level);
        }
        var uploaded = Upload(fixture.Content, mesh); Assert.Equal(4, uploaded.Distances.Length);
        var part = new AssetPart { Label = "generated", Mesh = uploaded, Shared = true, Material = new(),
            BoundsCentre = Vector3.Zero, BoundsRadius = 1, ScreenLodThresholds = AnatomyDetail.ScreenThresholds };
        foreach (var (pixels, expected) in new[] { (651f, 0), (650f, 0), (649f, 1), (400f, 1), (399f, 2), (260f, 2), (259f, 3), (0f, 3) })
            Assert.Equal(expected, CharacterRenderer.ScreenLevel(part, pixels));
    }

    [Fact]
    public void Retired_morph_slots_are_not_reused_before_the_fence_and_completed_adjacent_ranges_are_coalesced_without_corrupting_live_data()
    {
        var gpu = new BufferGpu();
        using var arena = new MorphArena(gpu);
        Vector4[] Values(float value, int count) => Enumerable.Repeat(new Vector4(value), count).ToArray();
        uint old = arena.Add("old", Values(1, 16));
        uint live = arena.Add("live", Values(2, 16));
        arena.Release(old);
        uint before = arena.Add("before fence", Values(3, 16));
        Assert.NotEqual(old, before);
        gpu.Advance(gpu.FramesInFlight - 1);
        Assert.Equal(Values(2, 16), MorphValues(gpu, live, 16));
        gpu.Advance(1);
        uint reused = arena.Add("after fence", Values(4, 8));
        Assert.InRange(reused, old, old + 8);
        int highWater = arena.Entries;
        arena.Release(live); arena.Release(before); gpu.Advance(gpu.FramesInFlight);
        uint merged = arena.Add("merged", Values(5, 32));
        Assert.True(reused + 8 <= merged || merged + 32 <= reused);
        Assert.Equal(highWater, arena.Entries);
        Assert.Equal(Values(4, 8), MorphValues(gpu, reused, 8));
        Assert.Equal(Values(5, 32), MorphValues(gpu, merged, 32));
    }

    [Fact]
    [Slow]
    public void Opaque_bowel_geometry_is_drawn_by_the_mod_pass_when_translucent_particle_effects_are_disabled()
    {
        using var device = TryCreate(); Assert.SkipWhen(device is null, "No Vulkan 1.3 device");
        string? mod = ModFolder(); Assert.SkipWhen(mod is null, "Brutality effect sprites are not in this checkout");
        using (var gpu = new GpuContext(device!))
        using (var renderer = new GoreEffectRenderer(gpu, mod!))
        using (var target = gpu.CreateTexture(new TextureDesc(InternalFormat.Rgba8, 32, 32, Use: TextureUse.ColourTarget | TextureUse.TransferSrc, Name: "opaque bowel test")))
        {
            Vertex Make(Vector3 p) => new() { Position = p, Normal = Vector3.UnitZ, Colour = new(.5f, .1f, .08f, 1) };
            Vertex a = Make(new(-.8f, -.8f, .5f)), b = Make(new(.8f, -.8f, .5f)), c = Make(new(0, .8f, .5f));
            var scene = new GoreEffectsSimulation(new Random(1)) { Bowels = [new(1, Vector3.Zero, [a, b, c, c, b, a], 0x800000, 1)] };
            renderer.Prepare(scene, Matrix4x4.Identity, Matrix4x4.Identity, new(0, 0, 2), enabled: false, Vector3.UnitZ);
            Assert.True(renderer.HasDraws); Assert.Equal(1, renderer.BatchCount);
            var cmd = gpu.BeginNative("opaque organ host");
            cmd.BeginRendering(new(new RenderTarget(target.Attachment(), LoadOp.Clear, new ClearColour(0, 0, 0, 1)), default, 32, 32));
            gpu.BeginHostPass(cmd, PassTargets.Of(target, null), DrawState.For(new(InternalFormat.Rgba8, InternalFormat.None), device!.DepthClamp));
            renderer.Record();
            cmd.EndRendering(); gpu.EndHostPass(cmd); gpu.EndNative(cmd); gpu.Finish();
            var pixels = gpu.ReadBack(target, 4);
            int centre = (16 * 32 + 16) * 4;
            Assert.True(pixels[centre] > pixels[centre + 1] && pixels[centre] > 40, "the lit anatomical triangle was not visible");
            scene.Bowels = []; renderer.Prepare(scene, Matrix4x4.Identity, Matrix4x4.Identity, Vector3.Zero, enabled: false, Vector3.UnitZ);
            Assert.False(renderer.HasDraws);
        }
        Assert.True(device!.ValidationErrors == 0, string.Join('\n', device.ValidationLog));
    }

    static VulkanDevice? TryCreate()
    {
        try { return VulkanDevice.Create(new VulkanDeviceOptions { Validation = true, SyncValidation = true }); }
        catch (Exception error) when (error is VulkanException or DllNotFoundException or EntryPointNotFoundException or Silk.NET.Core.Loader.SymbolLoadingException) { return null; }
    }
    static string? ModFolder()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            string mod = Path.Combine(folder.FullName, "mods", "Brutality");
            if (File.Exists(Path.Combine(mod, "assets", "effects", "robot-spark.png"))) return mod;
        }
        return null;
    }
    static Vector4[] MorphValues(BufferGpu gpu, uint start, int count) => MemoryMarshal.Cast<byte, Vector4>(gpu.Memory.Values.Single(p => p.Name == "character morphs").Bytes.AsSpan())
        .Slice((int)start + 1, count).ToArray();
    static GpuObjectMesh Upload(CharacterContent content, OgreMesh mesh)
    {
        var loaded = Load.Invoke(content, ["synthetic.mesh", 1, 1f, false, null, false, false, null, mesh, false, false])!;
        return (GpuObjectMesh)loaded.GetType().GetProperty("Gpu")!.GetValue(loaded)!;
    }
    static int[] Buffers(GpuObjectMesh mesh) => mesh.Parts.SelectMany(p => new[] { p.Vertices.Handle.Index, p.Indices.Handle.Index }).ToArray();
    static OgreMesh Triangle(float x) => MeshCut.FromTriangles([new() { Position = new(x, 1, 0), Normal = Vector3.UnitZ, Colour = Vector4.One, Weights = Vector4.UnitX },
        new() { Position = new(x + 1, 1, 0), Normal = Vector3.UnitZ, Colour = Vector4.One, Weights = Vector4.UnitX },
        new() { Position = new(x, 2, 0), Normal = Vector3.UnitZ, Colour = Vector4.One, Weights = Vector4.UnitX }]);

    sealed class SyntheticContent : IDisposable
    {
        readonly string root = Directory.CreateTempSubdirectory("meitou-brutality-lifetime-").FullName;
        public readonly BufferGpu Gpu = new();
        public readonly CharacterContent Content;
        public SyntheticContent()
        {
            Directory.CreateDirectory(Path.Combine(root, "data")); File.WriteAllBytes(Path.Combine(root, "data", "gamedata.base"), []);
            File.WriteAllText(Path.Combine(root, "resources.cfg"), "[General]\nFileSystem=data\n");
            var install = GameInstall.Open(root); Content = new(Gpu, install, new GameDatabase(), new AssetLocator(install));
        }
        public void Dispose() { Content.Dispose(); Directory.Delete(root, recursive: true); }
    }

    // An actual deferred buffer/upload protocol, with no rasterizer: tests can advance completed fences explicitly.
    sealed class BufferGpu : IGpuContext, IGpuUploads
    {
        public sealed record Allocation(string Name, byte[] Bytes);
        public readonly Dictionary<int, Allocation> Memory = [];
        readonly Queue<(long Frame, Action Release)> deferred = [];
        readonly TestFrame frame = new();
        int serial;
        public long Bytes => Memory.Values.Sum(value => (long)value.Bytes.Length);
        public int FramesInFlight => 3;
        public void Advance(int frames)
        {
            frame.Number += frames;
            while (deferred.TryPeek(out var next) && next.Frame <= frame.Number - FramesInFlight) deferred.Dequeue().Release();
        }
        public GpuBuffer CreateBuffer(ulong size, BufferUse use, string name) { int id = ++serial; Memory.Add(id, new(name, new byte[checked((int)size)])); return new(id, size); }
        public void ReleaseBuffer(GpuBuffer buffer) { if (!Memory.Remove(buffer.Index)) throw new InvalidOperationException("Buffer released twice"); }
        public void DeferDelete(Action release) => deferred.Enqueue((frame.Number, release));
        public bool FrameCompleted(long number) => number <= frame.Number - FramesInFlight;
        public IGpuUploads Uploads => this;
        public IGpuUploadBatch Begin() => new Upload(this);
        sealed class Upload(BufferGpu owner) : IGpuUploadBatch
        {
            public void Write(DeviceBuffer target, ulong offset, ReadOnlySpan<byte> bytes) => bytes.CopyTo(owner.Memory[target.Handle.Index].Bytes.AsSpan(checked((int)offset)));
            public GpuTexture Create(TextureDesc desc) => throw new NotSupportedException();
            public void Write(GpuTexture target, int level, int layer, in PixelRect region, ReadOnlySpan<byte> data) => throw new NotSupportedException();
            public IGpuCommandList Commands => throw new NotSupportedException();
            public void Dispose() { }
        }
        public IGpuFrame Frame => frame;
        public void EnsureFrame() { }
        public FrameGlobals Globals { get; } = new();
        public IGpuPipelines Pipelines => throw new NotSupportedException();
        public IGpuShaders Shaders => throw new NotSupportedException();
        public IGpuSamplers Samplers => throw new NotSupportedException();
        public IGpuBindlessTable Bindless => throw new NotSupportedException();
        public SampledTexture Dummy(SamplerInfo sampler) => throw new NotSupportedException();
        public uint StandIn2D => 0;
        public GpuBuffer DummyVertex => default;
        public bool ConstantVertexAttributes => true;
        public bool RenderTargetsBottomUp => true;
        public GpuBuffer DummyVertexArray(ScalarKind kind) => throw new NotSupportedException();
        public ulong UniformBufferAlign => 16;
        public bool HasRayQuery => false;
        public bool HasTessellation => false;
        public int MaxSampleCount => 1;
        public ulong VideoMemoryBudget => 1024 * 1024 * 1024;
        public void Finish() => throw new NotSupportedException();
        public IGpuReadback CreateReadback(ulong size, string name) => throw new NotSupportedException();
        public GpuTexture CreateTexture(TextureDesc desc) => throw new NotSupportedException();
        public GpuView View(GpuTexture texture, int baseLevel, int levels, int baseLayer, int layers, GpuViewType type, GpuSwizzle swizzle) => throw new NotSupportedException();
        public void ReleaseTexture(GpuTexture texture) => throw new NotSupportedException();
        public GpuSet CreateSet(GpuSetLayout layout, ReadOnlySpan<GpuBinding> bindings) => throw new NotSupportedException();
        public IGpuCommandList BeginGuest(string label) => throw new NotSupportedException();
        public void BeginHostPass(IGpuCommandList cmd, PassTargets targets, DrawState state) => throw new NotSupportedException();
        public void BeginHostPassParallel(IGpuCommandList cmd, PassTargets targets, DrawState state, int stage) => throw new NotSupportedException();
        public void SetPassViewport(PixelViewport viewport, PixelRect? scissor = null) => throw new NotSupportedException();
        public void SetPassStage(int stage) => throw new NotSupportedException();
        public void ClearDepth(IGpuCommandList host, float value, PixelRect rect) => throw new NotSupportedException();
        public void EndHostPassParallel(IGpuCommandList cmd) => throw new NotSupportedException();
        public void EndHostPass(IGpuCommandList cmd) => throw new NotSupportedException();
        public bool DepthClamp => false;
        public bool RecordsSecondaries => false;
        public void EndGuest(IGpuCommandList cmd) => throw new NotSupportedException();
        public IGpuCommandList BeginNative(string label) => throw new NotSupportedException();
        public void EndNative(IGpuCommandList cmd) => throw new NotSupportedException();
        public bool PassOpen => false;
        public PassTargets CurrentTargets() => throw new NotSupportedException();
        public DrawState CurrentState() => throw new NotSupportedException();
        public void Interleave(Action<IGpuCommandList> record) => throw new NotSupportedException();
        public void Record(string label, RecordJob job) => throw new NotSupportedException();
        public GpuSet AllocateConstantSet(GpuSetLayout layout, GpuBuffer buffer, ulong size) => throw new NotSupportedException();
        public bool IsIntegrated => false;
        public float LodBias { get; set; }
        sealed class TestFrame : IGpuFrame
        {
            public long Number { get; set; } = 1;
            public int Slot => (int)(Number % 3);
            public bool Open => true;
            public IGpuCommandList Commands => throw new NotSupportedException();
            public IGpuCommandList PreFrame => throw new NotSupportedException();
            public ResourceStates States => throw new NotSupportedException();
            public GpuStats Stats => throw new NotSupportedException();
            public Transient WriteConstants(ReadOnlySpan<byte> data, ulong alignment = 16) => throw new NotSupportedException();
            public Transient AllocateConstants(ulong size, ulong alignment = 16) => throw new NotSupportedException();
            public IGpuTimestamps Timestamps => throw new NotSupportedException();
        }
    }
}
