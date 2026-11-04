using System.Numerics;

using Meitou.Rendering.Gpu;
using Meitou.Data.Particles;

namespace Meitou.Rendering;

public readonly record struct LiquidBloodStream(Vector3 Head, Vector3 Tail, float Width, Vector4 Colour, float TailWidth = 0);

public readonly record struct LiquidBloodDrop(Vector3 Head, Vector3 Tail, float Width, Vector4 Colour);

public sealed unsafe partial class ParticleRenderer
{
    public const int LiquidBloodLimit = 768;
    public List<LiquidBloodStream> LiquidStreams { get; } = [];
    public List<LiquidBloodDrop> LiquidBlood { get; } = [];
    public int DrawnLiquidBlood { get; private set; }

    void AddLiquidBlood(IGpuFrame frame, Vector3 eye, Vector3 forward, float nearPlane)
    {
        int streamCount = Math.Min(LiquidStreams.Count, LiquidStreamLimit);
        AddLiquidStreamBatch(frame, eye, forward, nearPlane, streamCount);
        int count = Math.Min(LiquidBlood.Count, LiquidBloodLimit - DrawnLiquidBlood);
        if (scratch.Length < count) scratch = new ParticleInstance[Math.Max(count, scratch.Length * 2)];
        int written = 0;
        for (int i = 0; i < count; i++)
        {
            var drop = LiquidBlood[i];
            if (Vector3.DistanceSquared(drop.Head, eye) > DrawRange * DrawRange) continue;
            var along = drop.Head - drop.Tail;
            float length = along.Length();
            scratch[written++] = new ParticleInstance
            {
                Position = (drop.Head + drop.Tail) * 0.5f - eye,
                Direction = length > 1e-5f ? along / length : Vector3.UnitY,
                Width = drop.Width,
                Height = MathF.Max(length, drop.Width * 1.25f),
                Colour = drop.Colour
            };
        }
        if (written == 0) return;
        int close = gpu is not GpuContext ? 0 : PartitionByDepth(scratch.AsSpan(0, written), forward, nearPlane);
        var instances = frame.AllocateConstants((ulong)(written * InstanceBytes), 16);
        scratch.AsSpan(0, written).CopyTo(new Span<ParticleInstance>(instances.Pointer, written));
        draws.Add(new DrawItem
        {
            Instances = instances,
            Count = written,
            Close = close,
            Technique = TracerTechnique,
            Material = TracerMaterial,
            Texture = tracerTexture,
            Divisor = 1,
            Tracer = true,
            Discharge = 5
        });
        DrawnLiquidBlood += written;
        drawnParticles += written;
    }
    public const int LiquidStreamLimit = 512;

    void AddLiquidStreamBatch(IGpuFrame frame, Vector3 eye, Vector3 forward, float nearPlane, int count)
    {
        if (scratch.Length < count) scratch = new ParticleInstance[Math.Max(count, scratch.Length * 2)];
        int written = 0;
        for (int i = 0; i < count; i++)
        {
            var stream = LiquidStreams[i];
            if (!LiquidBloodStripGeometry.Valid(stream)
                || MathF.Min(Vector3.DistanceSquared(stream.Head, eye), Vector3.DistanceSquared(stream.Tail, eye)) > DrawRange * DrawRange) continue;
            scratch[written++] = LiquidBloodStripGeometry.Pack(LiquidStreams, i, eye);
        }
        if (written == 0) return;
        int close = gpu is not GpuContext ? 0 : PartitionLiquidByDepth(scratch.AsSpan(0, written), forward, nearPlane);
        var instances = frame.AllocateConstants((ulong)(written * InstanceBytes), 16);
        scratch.AsSpan(0, written).CopyTo(new Span<ParticleInstance>(instances.Pointer, written));
        draws.Add(new DrawItem
        {
            Instances = instances, Count = written, Close = close, Technique = TracerTechnique,
            Material = TracerMaterial, Texture = tracerTexture, Divisor = 1, Tracer = true, Discharge = 6,
        });
        DrawnLiquidBlood += written;
        drawnParticles += written;
    }

    static int PartitionLiquidByDepth(Span<ParticleInstance> instances, Vector3 forward, float nearPlane)
    {
        int close = 0;
        for (int i = 0; i < instances.Length; i++)
        {
            var centre = (instances[i].Position + instances[i].Direction) * 0.5f;
            if (Vector3.Dot(centre, forward) >= nearPlane) continue;
            (instances[close], instances[i]) = (instances[i], instances[close]);
            close++;
        }
        return close;
    }
}
