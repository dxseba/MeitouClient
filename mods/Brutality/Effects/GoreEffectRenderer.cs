using Meitou.Game.Rendering.Sky;
using Meitou.Game.Rendering.Gpu;
using System.Buffers;
using System.Numerics;
using System.Runtime.InteropServices;
using Meitou.Data.Textures;

namespace Meitou.Brutality.Effects;

/// <summary>Mod-owned GPU resources and draw calls, through the same public interfaces on Vulkan and Metal.</summary>
internal sealed unsafe class GoreEffectRenderer : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    readonly record struct FxVertex(Vector3 Position, Vector2 Uv, Vector4 Colour, Vector3 Ends, Vector3 Normal = default);
    sealed class TissueSlot(DeviceBuffer buffer, int count)
    {
        public readonly DeviceBuffer Buffer = buffer;
        public readonly int Count = count;
        public long Version = -1;
        public uint BloodColour;
        public bool Mechanical;
        public Vector3 Centre;
        public Matrix4x4[]? Pose;
    }
    sealed class Batch(int kind, Texture image)
    {
        public readonly int Kind = kind;
        public readonly Texture Image = image;
        public readonly List<FxVertex> Vertices = [];
        public Transient Buffer;
    }
    sealed class Texture : IDisposable
    {
        readonly GpuTexture texture;
        readonly IGpuContext gpu;
        public Texture(IGpuContext gpu, RgbaImage image, string name)
        {
            this.gpu = gpu;
            using var batch = gpu.Uploads.Begin();
            int levels = 1 + (int)Math.Floor(Math.Log2(Math.Max(image.Width, image.Height)));
            texture = batch.Create(new TextureDesc(InternalFormat.Rgba8, image.Width, image.Height, levels,
                Use: TextureUse.Sampled | TextureUse.TransferDst | TextureUse.TransferSrc, Name: name));
            batch.Write(texture, 0, 0, new(0, 0, (uint)image.Width, (uint)image.Height), image.Pixels);
            if (levels > 1) batch.Commands.GenerateMips(texture);
        }
        public SampledTexture Sampled() => new(gpu.Samplers.Get(SamplerDesc.FromGl(TextureMinFilter.LinearMipmapLinear, TextureMagFilter.Linear,
            TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge, false, DepthFunction.Lequal, false, 1, false, gpu.LodBias)), texture.View());
        public void Dispose() => texture.Dispose();
    }
    readonly IGpuContext gpu;
    readonly LegacyProgram program;
    readonly UniformHandle viewProjection, eyeUniform, kindUniform, coverageUniform, centreUniform, sunUniform;
    readonly UniformHandle bowelSun, bowelSky, bowelGround, atmosphereParams, tissueSkinned, tissuePose;
    readonly SamplerSlot imageUniform;
    readonly VertexArrayBindings attributes;
    readonly Dictionary<(AttachmentFormats, BlendState, ColorMask), GpuPipeline> pipelines = [];
    readonly Texture white, arc, spark;
    readonly Batch smoke, stream, drops, arcs, sparks, flashes;
    readonly Batch[] batches;
    readonly Dictionary<long, TissueSlot> tissues = [];
    readonly HashSet<long> activeTissues = [];
    readonly RobotDischargeStroke[] strokes = new RobotDischargeStroke[RobotDischargeGeometry.QuadLimit];
    readonly RobotSmokePuff[] puffs = new RobotSmokePuff[RobotDischargeSmokeGeometry.PuffLimit];
    readonly RobotExplosionQuad[] explosionQuads = new RobotExplosionQuad[RobotBreakExplosionGeometry.QuadLimit];
    Matrix4x4 vp;
    Vector3 eye, right, up;
    Vector3 sun;
    long frame = -1;
    bool disposed;
    public int BatchCount { get; private set; }
    public bool HasDraws => !disposed && frame == gpu.Frame.Number && BatchCount > 0;

    public GoreEffectRenderer(IGpuContext gpu, string modFolder)
    {
        this.gpu = gpu;
        gpu.EnsureFrame();
        program = LegacyProgram.Create(gpu, GoreEffectShaders.Vertex, GoreEffectShaders.Fragment, "Brutality liquid and electrical effects");
        viewProjection = program.Uniform("uViewProjection"); eyeUniform = program.Uniform("uEye");
        kindUniform = program.Uniform("uDischarge"); coverageUniform = program.Uniform("uCoverage"); imageUniform = program.Sampler("uTexture");
        centreUniform = program.Uniform("uCentre"); sunUniform = program.Uniform("uSunDirection");
        bowelSun = program.Uniform("uBowelSun"); bowelSky = program.Uniform("uBowelSky"); bowelGround = program.Uniform("uBowelGround");
        atmosphereParams = program.Uniform("uAtmoParams");
        tissueSkinned = program.Uniform("uTissueSkinned");
        tissuePose = program.Uniform("uTissuePose[0]");
        white = new(gpu, new(1, 1, [255, 255, 255, 255]), "Brutality white");
        arc = Load("robot-electric-arc.png"); spark = Load("robot-spark.png");
        smoke = new(3, white); stream = new(6, white); drops = new(5, white); arcs = new(1, arc); sparks = new(4, spark); flashes = new(7, white);
        batches = [smoke, stream, drops, arcs, sparks, flashes];
        const uint stride = 60;
        attributes = new([new LegacyProgram.Attribute(default, InternalFormat.Rgb32F, stride, false), new LegacyProgram.Attribute(default, InternalFormat.Rg32F, stride, false),
            new LegacyProgram.Attribute(default, InternalFormat.Rgba32F, stride, false), new LegacyProgram.Attribute(default, InternalFormat.Rgb32F, stride, false),
            new LegacyProgram.Attribute(default, InternalFormat.Rgb32F, stride, false)], default);
        return;

        Texture Load(string name)
        {
            string path = Meitou.Content.GameFiles.Resolve(Path.Combine(modFolder, "assets", "effects", name));
            if (!File.Exists(path)) throw new FileNotFoundException($"Missing Brutality effect sprite {name}", path);
            return new(gpu, TextureLoader.LoadFile(path, allMips: false).Levels[0], "Brutality " + name);
        }
    }

    public void Prepare(GoreEffectsSimulation scene, Matrix4x4 projection, Matrix4x4 view, Vector3 cameraEye, bool enabled, Vector3 sunDirection)
    {
        frame = gpu.Frame.Number; BatchCount = 0;
        foreach (var batch in batches) batch.Vertices.Clear();
        if (!enabled && scene.Bowels.Count == 0 && scene.Cables.Count == 0)
        {
            foreach (var slot in tissues.Values) slot.Buffer.Dispose(); tissues.Clear();
            return;
        }
        eye = cameraEye; vp = Matrix4x4.CreateTranslation(eye) * projection;
        sun = sunDirection.LengthSquared() > 1e-10f ? Vector3.Normalize(sunDirection) : Vector3.UnitY;
        right = new(view.M11, view.M21, view.M31); up = new(view.M12, view.M22, view.M32);
        if (enabled)
        {
        var anchors = scene.Anchors.ToArray();
        int count = RobotDischargeSmokeGeometry.Build(anchors, puffs);
        puffs.AsSpan(0, count).Sort((a, b) => Vector3.DistanceSquared(b.Position, cameraEye).CompareTo(Vector3.DistanceSquared(a.Position, cameraEye)));
        for (int i = 0; i < count; i++)
        {
            var p = puffs[i]; Billboard(smoke, p.Position, p.Size, p.Size, p.Rotation, new(.025f, .027f, .03f, p.Alpha));
        }
        var liquid = scene.Blood.Streams;
        for (int i = 0; i < Math.Min(liquid.Count, LiquidBloodSimulation.StreamSegmentLimit); i++)
        {
            if (!LiquidBloodStripGeometry.Valid(liquid[i])) continue;
            var p = LiquidBloodStripGeometry.Pack(liquid, i, eye);
            var headSide = Side(Decode(new(p.Height, p.Depth)), p.Position);
            var tailSide = Side(Decode(new(p.Pad0, p.Pad1)), p.Direction);
            float hw = MathF.Abs(p.Rotation), tw = MathF.Abs(p.Width);
            var ends = new Vector3(p.Rotation < 0 ? 1 : 0, p.Width < 0 ? 1 : 0, Vector3.Distance(p.Position, p.Direction) / Math.Max(hw, Math.Max(tw, .00001f)));
            Quad(stream, p.Position - headSide * (hw * .5f), p.Position + headSide * (hw * .5f),
                p.Direction + tailSide * (tw * .5f), p.Direction - tailSide * (tw * .5f), p.Colour, ends, relative: true);
        }
        foreach (var p in scene.Blood.Drops)
        {
            var tail = p.At - p.Velocity * MathF.Min(.045f, 1.5f / MathF.Max(p.Velocity.Length(), .001f));
            var lighting = Vector3.Zero;
#if BRUTALITY_STOCK_MASTER
            if (p.Colour == LiquidBloodSimulation.DefaultOrganicPigment) lighting = Vector3.UnitX;
#endif
            Stroke(drops, p.At, tail, p.Width, new(p.Colour, .9f * Math.Clamp((2.5f - p.Age) * 3, 0, 1)), lighting);
        }
        count = RobotDischargeGeometry.Build(anchors, strokes);
        for (int i = 0; i < count; i++)
        {
            var p = strokes[i]; Stroke(p.Arc ? arcs : sparks, p.Head, p.Tail, p.Width, p.Colour);
        }
        count = RobotBreakExplosionGeometry.Build(scene.Explosions.ToArray(), explosionQuads);
        for (int i = 0; i < count; i++)
        {
            var p = explosionQuads[i];
            if (p.Kind == RobotExplosionQuadKind.Spark)
                Stroke(sparks, p.Position + p.Direction * (p.Height * .5f), p.Position - p.Direction * (p.Height * .5f), p.Width, p.Colour);
            else Billboard(p.Kind == RobotExplosionQuadKind.Smoke ? smoke : flashes, p.Position, p.Width, p.Height, p.Rotation, p.Colour);
        }
        foreach (var batch in batches)
        {
            if (batch.Vertices.Count == 0) continue;
            batch.Buffer = gpu.Frame.WriteConstants(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(batch.Vertices)), 16);
            BatchCount++;
        }
        }
        activeTissues.Clear();
        foreach (var piece in scene.Bowels.Concat(scene.Cables)) activeTissues.Add(piece.Key);
        foreach (long key in tissues.Keys.Where(key => !activeTissues.Contains(key)).ToArray()) { tissues[key].Buffer.Dispose(); tissues.Remove(key); }
        foreach (var piece in scene.Bowels.Concat(scene.Cables))
        {
            if (piece.Vertices.Length == 0) continue;
            if (!tissues.TryGetValue(piece.Key, out var slot))
                tissues.Add(piece.Key, slot = new(DeviceBuffer.Create(gpu, (ulong)(piece.Vertices.Length * 60 + 84), BufferUse.Vertex, "Brutality bowel tissue"), piece.Vertices.Length));
            slot.Centre = piece.Centre;
            slot.Pose = piece.Pose;
            if (slot.Version != piece.Version || slot.BloodColour != piece.BloodColour || slot.Mechanical != piece.Mechanical)
            {
                var tissueLighting = Vector3.Zero;
#if BRUTALITY_STOCK_MASTER
                // The stock organ palette expects the forward pass to supply diffuse bounce.
                if (piece.Albedo.HasValue) tissueLighting = Vector3.UnitZ;
#endif
                // Skinning keeps the rest vertices resident. Reuse scratch memory for initial and CPU-only uploads.
                var packed = ArrayPool<FxVertex>.Shared.Rent(slot.Count);
                try
                {
                    for (int i = 0; i < slot.Count; i++)
                    {
                        var v = piece.Vertices[i];
                        var uv = v.Uv;
                        var aux = tissueLighting;
                        if (piece.Skinning is { } skinning)
                        {
                            var (weights, weightAndNodes) = skinning.PackedInfluences(i);
                            uv = weights;
                            aux.X = weightAndNodes.X;
                            aux.Y = weightAndNodes.Y;
                        }
                        packed[i] = new(v.Position, uv, piece.ColourOf(v.Colour), aux, v.Normal);
                    }
                    using var upload = gpu.Uploads.Begin();
                    upload.Write(slot.Buffer, 0, MemoryMarshal.AsBytes(packed.AsSpan(0, slot.Count)));
                }
                finally
                {
                    ArrayPool<FxVertex>.Shared.Return(packed);
                }
                slot.Version = piece.Version; slot.BloodColour = piece.BloodColour; slot.Mechanical = piece.Mechanical;
            }
            BatchCount++;
        }
    }

    void Billboard(Batch batch, Vector3 centre, float width, float height, float rotation, Vector4 colour, Vector3 ends = default)
    {
        if (!Finite(centre) || Vector3.DistanceSquared(centre, eye) > 20400f * 20400f) return;
        float sine = MathF.Sin(rotation), cosine = MathF.Cos(rotation);
        var x = (right * cosine + up * sine) * (width * .5f);
        var y = (-right * sine + up * cosine) * (height * .5f);
        Quad(batch, centre - x + y, centre + x + y, centre + x - y, centre - x - y, colour, ends);
    }
    void Stroke(Batch batch, Vector3 head, Vector3 tail, float width, Vector4 colour, Vector3 ends = default)
    {
        if (!Finite(head) || !Finite(tail) || Vector3.DistanceSquared(head, eye) > 20400f * 20400f) return;
        var axis = head - tail;
        if (axis.LengthSquared() < 1e-10f) { Billboard(batch, head, width, width * 1.6f, 0, colour, ends); return; }
        var side = Side(Vector3.Normalize(axis), (head + tail) * .5f - eye) * (width * .5f);
        Quad(batch, head - side, head + side, tail + side, tail - side, colour, ends);
    }
    void Quad(Batch batch, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector4 colour, Vector3 ends = default, bool relative = false)
    {
        if (!relative) { a -= eye; b -= eye; c -= eye; d -= eye; }
        batch.Vertices.Add(new(a, new(0, 0), colour, ends)); batch.Vertices.Add(new(b, new(1, 0), colour, ends)); batch.Vertices.Add(new(c, new(1, 1), colour, ends));
        batch.Vertices.Add(new(a, new(0, 0), colour, ends)); batch.Vertices.Add(new(c, new(1, 1), colour, ends)); batch.Vertices.Add(new(d, new(0, 1), colour, ends));
    }
    Vector3 Side(Vector3 tangent, Vector3 point)
    {
        var side = Vector3.Cross(tangent, point);
        return side.LengthSquared() > 1e-10f ? Vector3.Normalize(side) : right;
    }
    static Vector3 Decode(Vector2 oct)
    {
        var n = new Vector3(oct, 1 - MathF.Abs(oct.X) - MathF.Abs(oct.Y));
        float t = Math.Clamp(-n.Z, 0, 1);
        n.X += n.X >= 0 ? -t : t; n.Y += n.Y >= 0 ? -t : t;
        return Vector3.Normalize(n);
    }

    public void Record(ColorMask? coverageMask = null, bool depthTest = true)
    {
        if (!HasDraws) return;
        var targets = gpu.CurrentTargets();
        if (targets.Formats.Colour == InternalFormat.None) return;
        var cmd = gpu.BeginGuest(coverageMask is null ? "Brutality effects" : "Brutality effect reactive mask");
        var baseState = gpu.CurrentState();
        cmd.SetViewport(targets.Viewport); cmd.SetScissor(targets.Scissor);
        program.Set(viewProjection, vp); program.Set(eyeUniform, eye);
        program.Set(sunUniform, sun);
        program.Set(coverageUniform, coverageMask is null ? 0 : coverageMask == ColorMask.A ? 2 : 1);
        if (coverageMask is null && tissues.Values.Any(slot => !slot.Mechanical))
        {
            program.ApplyGlobals();
            // ApplyGlobals publishes the world's physical sky flag. This pass uses its own bounded sky light.
            program.Set(atmosphereParams, Vector4.Zero);
            var simple = SkyColours.For(sun).Lighting(0);
            program.Set(bowelSun, simple.SunColour); program.Set(bowelSky, simple.AmbientSky); program.Set(bowelGround, simple.AmbientGround);
        }
        if (tissues.Count > 0)
        {
            var state = baseState with { Cull = CullMode.Back, Front = FrontFaceDirection.Ccw, DepthTest = depthTest && targets.Formats.Depth != InternalFormat.None,
                DepthWrite = coverageMask is null && targets.Formats.Depth != InternalFormat.None, ColourMask = coverageMask ?? (ColorMask.R | ColorMask.G | ColorMask.B),
                Blend = coverageMask is null ? BlendState.Off : coverageMask == ColorMask.R
                    ? new(true, BlendingFactor.One, BlendingFactor.One, BlendOperation.Max, BlendOperation.Max) : new(true, BlendingFactor.One, BlendingFactor.One) };
            var key = (targets.Formats, state.Blend, state.ColourMask);
            if (!pipelines.TryGetValue(key, out var pipeline)) pipelines[key] = pipeline = gpu.Pipelines.Get(state.Pipeline(program.Program,
                program.VertexLayout(attributes.Attributes), PrimitiveType.Triangles, targets.Formats, "Brutality bowel tissue"));
            program.Bind(imageUniform, white.Sampled());
            cmd.SetRaster(state.Cull, state.Front); cmd.SetDepth(state.DepthTest, state.DepthWrite, state.Compare); cmd.SetDepthBias(false, 0, 0); cmd.BindPipeline(pipeline);
            foreach (var slot in tissues.Values)
            {
                program.Set(kindUniform, slot.Mechanical ? 9 : 8);
                program.Set(centreUniform, slot.Centre - eye);
                program.Set(tissueSkinned, slot.Pose is null ? 0 : 1);
                if (slot.Pose is { } pose) program.Set(tissuePose, MemoryMarshal.Cast<Matrix4x4, float>(pose.AsSpan()), 4, 4);
                GpuBufferBinding[] bindings = [new(slot.Buffer.Handle, 0), new(slot.Buffer.Handle, 12), new(slot.Buffer.Handle, 20), new(slot.Buffer.Handle, 36), new(slot.Buffer.Handle, 48)];
                cmd.BindVertexBuffers(0, bindings); program.Flush(cmd); cmd.Draw((uint)slot.Count);
            }
        }
        program.Set(centreUniform, Vector3.Zero);
        program.Set(tissueSkinned, 0);
        foreach (var batch in batches)
        {
            if (batch.Vertices.Count == 0) continue;
            bool additive = batch.Kind is 1 or 4 or 7;
            var state = baseState with
            {
                Cull = CullMode.None, DepthTest = depthTest && targets.Formats.Depth != InternalFormat.None, DepthWrite = false,
                ColourMask = coverageMask ?? (ColorMask.R | ColorMask.G | ColorMask.B),
                Blend = coverageMask is not null ? coverageMask == ColorMask.R
                    ? new(true, BlendingFactor.One, BlendingFactor.One, BlendOperation.Max, BlendOperation.Max)
                    : new(true, BlendingFactor.One, BlendingFactor.One)
                    : additive ? new(true, BlendingFactor.One, BlendingFactor.One) : new(true, BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha),
            };
            var key = (targets.Formats, state.Blend, state.ColourMask);
            if (!pipelines.TryGetValue(key, out var pipeline))
                pipelines[key] = pipeline = gpu.Pipelines.Get(state.Pipeline(program.Program, program.VertexLayout(attributes.Attributes), PrimitiveType.Triangles, targets.Formats, "Brutality effects"));
            program.Set(kindUniform, batch.Kind); program.Bind(imageUniform, batch.Image.Sampled());
            cmd.SetRaster(state.Cull, state.Front); cmd.SetDepth(state.DepthTest, state.DepthWrite, state.Compare); cmd.SetDepthBias(state.BiasEnable, state.BiasConstant, state.BiasSlope);
            cmd.BindPipeline(pipeline);
            GpuBufferBinding[] bindings = [new(batch.Buffer.Handle, batch.Buffer.Offset), new(batch.Buffer.Handle, batch.Buffer.Offset + 12),
                new(batch.Buffer.Handle, batch.Buffer.Offset + 20), new(batch.Buffer.Handle, batch.Buffer.Offset + 36), new(batch.Buffer.Handle, batch.Buffer.Offset + 48)];
            cmd.BindVertexBuffers(0, bindings); program.Flush(cmd); cmd.Draw((uint)batch.Vertices.Count);
        }
        gpu.EndGuest(cmd);
    }
    static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        program.Dispose(); white.Dispose(); arc.Dispose(); spark.Dispose(); pipelines.Clear();
        foreach (var slot in tissues.Values) slot.Buffer.Dispose(); tissues.Clear();
    }
}
