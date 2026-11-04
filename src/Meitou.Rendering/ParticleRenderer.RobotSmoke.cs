using System.Numerics;
using System.Runtime.InteropServices;
using Meitou.Data.Particles;
using Meitou.Rendering.Gpu;

namespace Meitou.Rendering;

public sealed unsafe partial class ParticleRenderer
{
    public int DrawnRobotSmokePuffs { get; private set; }
    readonly RobotSmokePuff[] smokePuffs = new RobotSmokePuff[RobotDischargeSmokeGeometry.PuffLimit];
    static readonly PuTechniqueDef RobotSmokeTechnique = new()
    {
        Name = "robot soot", Renderer = new PuRendererDef { TypeName = "Billboard", BillboardType = "point" },
        Source = new PuNode("technique", [], 0),
    };
    static readonly ParticleMaterial RobotSmokeMaterial = new("robot soot", null, ParticleBlend.Alpha,
        DepthCheck: true, DepthWrite: false, Ambient: false, Vector4.One, Clamp: true);

    void AddRobotSmoke(IGpuFrame frame, Vector3 eye, Vector3 forward, float nearPlane)
    {
        int count = RobotDischargeSmokeGeometry.Build(CollectionsMarshal.AsSpan(RobotDischarges), smokePuffs);
        if (count == 0) return;
        if (scratch.Length < count) scratch = new ParticleInstance[Math.Max(count, scratch.Length * 2)];
        int written = 0;
        for (int i = 0; i < count; i++)
        {
            var puff = smokePuffs[i];
            if (Vector3.Distance(puff.Position, eye) - puff.Size > DrawRange) continue;
            scratch[written++] = new ParticleInstance { Position = puff.Position - eye, Rotation = puff.Rotation,
                Width = puff.Size, Height = puff.Size, Colour = new Vector4(0.025f, 0.027f, 0.03f, puff.Alpha) };
        }
        if (written == 0) return;
        // Alpha smoke is drawn back to front, before the brighter additive contacts.
        scratch.AsSpan(0, written).Sort(static (a, b) => b.Position.LengthSquared().CompareTo(a.Position.LengthSquared()));
        int close = gpu is not GpuContext ? 0 : PartitionByDepth(scratch.AsSpan(0, written), forward, nearPlane);
        var instances = frame.AllocateConstants((ulong)(written * InstanceBytes), 16);
        scratch.AsSpan(0, written).CopyTo(new Span<ParticleInstance>(instances.Pointer, written));
        draws.Add(new DrawItem { Instances = instances, Count = written, Close = close, Technique = RobotSmokeTechnique,
            Material = RobotSmokeMaterial, Texture = white, Divisor = 1, Discharge = 3 });
        DrawnRobotSmokePuffs = written;
        drawnParticles += written;
    }
}
