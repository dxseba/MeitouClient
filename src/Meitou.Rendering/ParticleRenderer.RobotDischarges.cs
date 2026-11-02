using System.Numerics;
using Meitou.Data.Particles;
using Meitou.Rendering.Gpu;

namespace Meitou.Rendering;

public sealed unsafe partial class ParticleRenderer
{
    public List<RobotDischargeAnchor> RobotDischarges { get; } = [];
    public string RobotDischargeTexture { get; set; } = Path.Combine(AppContext.BaseDirectory, "effects", "robot-electric-arc.png");
    public int DrawnRobotDischargeQuads { get; private set; }

    readonly RobotDischargeStroke[] dischargeStrokes = new RobotDischargeStroke[RobotDischargeGeometry.QuadLimit];
    readonly UniformHandle dischargeMode;
    ParticleMaterial? dischargeTextureMaterial;
    static readonly ParticleMaterial DischargeMaterial = new("robot electrical discharge", null, ParticleBlend.Add,
        DepthCheck: true, DepthWrite: false, Ambient: false, Vector4.One, Clamp: true);

    void AddRobotDischarges(IGpuFrame frame, Vector3 eye, Vector3 forward, float nearPlane)
    {
        int count = RobotDischargeGeometry.Build(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(RobotDischarges), dischargeStrokes);
        if (count == 0) return;
        if (scratch.Length < count) scratch = new ParticleInstance[Math.Max(count, scratch.Length * 2)];
        var arcTexture = RobotArcTexture();
        DrawnRobotDischargeQuads += AddRobotDischargeBatch(frame, eye, forward, nearPlane, count, true, arcTexture ?? tracerTexture,
            arcTexture is null ? 2 : 1);
        DrawnRobotDischargeQuads += AddRobotDischargeBatch(frame, eye, forward, nearPlane, count, false, tracerTexture, 2);
    }

    int AddRobotDischargeBatch(IGpuFrame frame, Vector3 eye, Vector3 forward, float nearPlane, int count,
        bool arc, SampledImage texture, int textureMode)
    {
        int written = 0;
        for (int i = 0; i < count; i++)
        {
            var stroke = dischargeStrokes[i];
            if (stroke.Arc != arc) continue;
            var along = stroke.Head - stroke.Tail;
            float length = along.Length();
            if (length < 1e-4f || Vector3.Distance(stroke.Head, eye) - length > DrawRange) continue;
            scratch[written++] = new ParticleInstance { Position = (stroke.Head + stroke.Tail) * 0.5f - eye,
                Direction = along / length, Width = stroke.Width, Height = length, Colour = stroke.Colour };
        }
        if (written == 0) return 0;
        int close = gpu.ClipDepthZeroToOne ? 0 : PartitionByDepth(scratch.AsSpan(0, written), forward, nearPlane);
        var instances = frame.AllocateConstants((ulong)(written * InstanceBytes), 16);
        scratch.AsSpan(0, written).CopyTo(new Span<ParticleInstance>(instances.Pointer, written));
        draws.Add(new DrawItem { Instances = instances, Count = written, Close = close, Technique = TracerTechnique,
            Material = DischargeMaterial, Texture = texture, Divisor = 1, Tracer = true, Discharge = textureMode });
        drawnParticles += written;
        return written;
    }

    SampledImage? RobotArcTexture()
    {
        if (dischargeTextureMaterial?.Texture != RobotDischargeTexture)
            dischargeTextureMaterial = DischargeMaterial with { Texture = RobotDischargeTexture };
        var texture = TextureFor(dischargeTextureMaterial);
        return ReferenceEquals(texture, white) ? null : texture;
    }
}
