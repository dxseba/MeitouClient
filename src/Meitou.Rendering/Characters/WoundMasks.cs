using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Rendering.Gpu;

namespace Meitou.Rendering.Characters;

/// <summary>A body vertex, for finding where on the skin a wound lands.</summary>
readonly record struct WoundPoint(Vector3 Position, Vector3 Normal, Vector2 Uv);

/// <summary>
/// A small texture per wounded character in its body's UVs: red the cut's core, green its open area, blue the bruised ring. Painted on the CPU when the character's
/// wounds change, so a cut stays on the skin it landed on (docs/character-renderer.md "Wounds"). A cut crossing a UV island's edge is clipped there.
/// </summary>
sealed class WoundMasks(IGpuContext gpu) : IDisposable
{
    const int Size = 256, Capacity = 64, RepaintsPerFrame = 4;
    /// <summary>The cut chart's unit in UV: a body's UVs give about 0.05 per world unit (<b>Observed</b> tuning value).</summary>
    const float ChartUv = 0.045f;

    sealed class Mask { public required GpuTexture Texture; public uint Index; public int Hash; }

    readonly Dictionary<long, Mask> masks = [];
    readonly byte[] pixels = new byte[Size * Size * 4];
    int repaints;

    /// <summary>The cut profile at a chart point (core, area, ring), from the host (<c>WoundPolicy.Sample</c>).</summary>
    public static Func<float, float, CharacterWoundMark, Vector3>? Sample { get; set; }

    public void BeginFrame() => repaints = 0;

    /// <summary>The bindless index of the character's mask, 0 without one.</summary>
    public uint Update(long key, SkeletonRig rig, WoundPoint[]? points, IReadOnlyList<CharacterWoundMark> wounds)
    {
        if (wounds.Count == 0 || points is not { Length: > 0 } || Sample is null) return 0;
        var hash = new HashCode();
        foreach (var w in wounds) hash.Add(w);
        int code = hash.ToHashCode();
        if (masks.TryGetValue(key, out var mask) && mask.Hash == code) return mask.Index;
        if (repaints >= RepaintsPerFrame || (mask is null && masks.Count >= Capacity)) return mask?.Index ?? 0;
        repaints++;
        Array.Clear(pixels);
        foreach (var w in wounds) Paint(w, rig, points);
        using (var batch = gpu.Uploads.Begin())
        {
            bool created = mask is null;
            mask ??= new Mask { Texture = batch.Create(new TextureDesc(InternalFormat.Rgba8, Size, Size, Name: "wound mask")) };
            batch.Write(mask.Texture, 0, 0, new PixelRect(0, 0, Size, Size), pixels);
            if (created)
            {
                var sampler = gpu.Samplers.Get(SamplerDesc.FromGl(TextureMinFilter.Linear, TextureMagFilter.Linear, TextureWrapMode.Repeat, TextureWrapMode.Repeat, TextureWrapMode.Repeat, false, DepthFunction.Lequal, false, 1, false, 0));
                mask.Index = gpu.Bindless.Register(BindlessKind.Texture2D, new SampledTexture(sampler, mask.Texture.View()));
                masks[key] = mask;
            }
        }
        mask.Hash = code;
        return mask.Index;
    }

    void Paint(in CharacterWoundMark w, SkeletonRig rig, WoundPoint[] points)
    {
        if (rig.Handle(w.BoneA) is not { } a || rig.Handle(w.BoneB) is not { } b || a >= rig.BindPosition.Length || b >= rig.BindPosition.Length) return;
        var target = Vector3.Lerp(rig.BindPosition[a], rig.BindPosition[b], w.T);
        float best = float.MaxValue;
        var uv = Vector2.Zero;
        foreach (ref readonly var p in points.AsSpan())
        {
            if (Vector3.Dot(p.Normal, w.Side) < 0.2f) continue;
            float d = Vector3.DistanceSquared(p.Position, target);
            if (d < best) (best, uv) = (d, p.Uv);
        }
        if (best == float.MaxValue) return;
        int cx = (int)(uv.X * Size), cy = (int)(uv.Y * Size), reach = (int)(3 * ChartUv * Size);
        for (int y = -reach; y <= reach; y++)
            for (int x = -reach; x <= reach; x++)
            {
                var s = Sample!(x / (ChartUv * Size), y / (ChartUv * Size), w);
                if (s.Z <= 0) continue;
                int i = ((((cy + y) % Size + Size) % Size) * Size + ((cx + x) % Size + Size) % Size) * 4;
                pixels[i] = Math.Max(pixels[i], (byte)(s.X * 255));
                pixels[i + 1] = Math.Max(pixels[i + 1], (byte)(s.Y * 255));
                pixels[i + 2] = Math.Max(pixels[i + 2], (byte)(s.Z * 255));
                pixels[i + 3] = 255;
            }
    }

    public void Dispose()
    {
        foreach (var mask in masks.Values)
        {
            gpu.Bindless.Free(BindlessKind.Texture2D, mask.Index);
            gpu.ReleaseTexture(mask.Texture);
        }
        masks.Clear();
    }
}
