using System.Numerics;
using System.Runtime.InteropServices;
using Meitou.Data.Particles;
using Meitou.Rendering.Gpu;

namespace Meitou.Rendering;

public sealed unsafe partial class ParticleRenderer
{
    public List<RobotBreakExplosion> RobotBreakExplosions { get; } = [];
    public int DrawnRobotExplosionQuads { get; private set; }
    readonly RobotExplosionQuad[] explosionQuads = new RobotExplosionQuad[RobotBreakExplosionGeometry.QuadLimit];

    void AddRobotExplosions(IGpuFrame frame, Vector3 eye, Vector3 forward, float nearPlane)
    {
        int count = RobotBreakExplosionGeometry.Build(CollectionsMarshal.AsSpan(RobotBreakExplosions), explosionQuads);
        if (count == 0) return;
        if (scratch.Length < count) scratch = new ParticleInstance[Math.Max(count, scratch.Length * 2)];
        AddRobotExplosionBatch(frame, eye, forward, nearPlane, count, RobotExplosionQuadKind.Smoke, white, 3);
        AddRobotExplosionBatch(frame, eye, forward, nearPlane, count, RobotExplosionQuadKind.Flash, white, 7);
        var sparks = RobotSparkImage();
        AddRobotExplosionBatch(frame, eye, forward, nearPlane, count, RobotExplosionQuadKind.Spark,
            sparks ?? tracerTexture, sparks is null ? 2 : 4);
    }

    void AddRobotExplosionBatch(IGpuFrame frame, Vector3 eye, Vector3 forward, float nearPlane, int count,
        RobotExplosionQuadKind kind, SampledImage texture, int textureMode)
    {
        int written = 0;
        for (int i = 0; i < count; i++)
        {
            var quad = explosionQuads[i];
            if (quad.Kind != kind || Vector3.Distance(quad.Position, eye) - MathF.Max(quad.Width, quad.Height) > DrawRange) continue;
            scratch[written++] = new ParticleInstance { Position = quad.Position - eye, Direction = quad.Direction,
                Width = quad.Width, Height = quad.Height, Rotation = quad.Rotation, Colour = quad.Colour };
        }
        if (written == 0) return;
        if (kind == RobotExplosionQuadKind.Smoke)
            scratch.AsSpan(0, written).Sort(static (a, b) => b.Position.LengthSquared().CompareTo(a.Position.LengthSquared()));
        int close = gpu is not GpuContext ? 0 : PartitionByDepth(scratch.AsSpan(0, written), forward, nearPlane);
        var instances = frame.AllocateConstants((ulong)(written * InstanceBytes), 16);
        scratch.AsSpan(0, written).CopyTo(new Span<ParticleInstance>(instances.Pointer, written));
        draws.Add(new DrawItem { Instances = instances, Count = written, Close = close,
            Technique = kind == RobotExplosionQuadKind.Spark ? TracerTechnique : RobotSmokeTechnique,
            Material = kind == RobotExplosionQuadKind.Smoke ? RobotSmokeMaterial : DischargeMaterial,
            Texture = texture, Divisor = 1, Tracer = kind == RobotExplosionQuadKind.Spark, Discharge = textureMode });
        DrawnRobotExplosionQuads += written;
        drawnParticles += written;
    }
}
