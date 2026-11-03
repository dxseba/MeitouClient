using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Rendering.Gpu;

namespace Meitou.Rendering.Characters;

/// <summary>A body vertex, for finding where on the skin a wound lands.</summary>
readonly record struct WoundPoint(Vector3 Position, Vector3 Normal, Vector2 Uv, int Bone = 0, uint Bones = 0, Vector4 Weights = default);

/// <summary>
/// A small texture per wounded character in its body's UVs: red the cut's core, green its open area, blue the bruised ring. Painted on the CPU when the character's
/// wounds change, so a cut stays on the skin it landed on (docs/character-renderer.md "Wounds"). A cut crossing a UV island's edge is clipped there.
/// </summary>
sealed class WoundMasks(IGpuContext gpu) : IDisposable
{
    const int Size = 512, Capacity = 64, RepaintsPerFrame = 4;
    /// <summary>The cut chart's unit in UV: a body's UVs give about 0.05 per world unit (<b>Observed</b> tuning value).</summary>
    const float ChartUv = 0.045f;

    sealed class Mask { public required GpuTexture Texture; public uint Index; public int Hash; public long Used; public Vector4[] Spheres = new Vector4[Spheres16], Cuts = new Vector4[Spheres16]; }

    /// <summary>World units round a wound in which the anatomy shows: a tuning value.</summary>
    /// <summary>The lower jaw's own group, for cutting it off.</summary>
    public const int JawGroup = 6;
    public const int Spheres16 = 64;
    const float SphereRadius = 1.4f;

    readonly Dictionary<long, Mask> masks = [];
    readonly byte[] pixels = new byte[Size * Size * 4];
    int repaints;
    long frame;

    /// <summary>The cut profile at a chart point (core, area, ring), from the host (<c>WoundPolicy.Sample</c>).</summary>
    public static Func<float, float, CharacterWoundMark, Vector3>? Sample { get; set; }

    public void BeginFrame() { repaints = 0; frame++; }

    /// <summary>The bindless index of the character's mask, 0 without one.</summary>
    public uint Update(long key, SkeletonRig rig, WoundPoint[]? points, IReadOnlyList<CharacterWoundMark> wounds)
    {
        if (wounds.Count == 0 || points is not { Length: > 0 } || Sample is null) return 0;
        var hash = new HashCode();
        foreach (var w in wounds) hash.Add(w);
        int code = hash.ToHashCode();
        if (masks.TryGetValue(key, out var mask) && mask.Hash == code) { mask.Used = frame; return mask.Index; }
        // Full: the mask of a character gone longest makes room.
        if (mask is null && masks.Count >= Capacity && masks.MinBy(m => m.Value.Used) is { Value.Used: var used, Key: var old, Value: var stale } && used < frame - 600)
        {
            gpu.Bindless.Free(BindlessKind.Texture2D, stale.Index);
            gpu.ReleaseTexture(stale.Texture);
            masks.Remove(old);
        }
        if (repaints >= RepaintsPerFrame || (mask is null && masks.Count >= Capacity)) return mask?.Index ?? 0;
        repaints++;
        Array.Clear(pixels);
        int count = 0;
        var spheres = new Vector4[Spheres16];
        var cuts = new Vector4[Spheres16];
        foreach (var w in wounds)
            if (Paint(w, rig, points, out var cutUv) is { } at && count < spheres.Length)
            {
                float angle = MathF.Atan2(w.Sine, w.Cosine), head = cutUv.Y < 0 ? 0.2f : 1;
                cuts[count] = new Vector4(cutUv, angle + Style(w) * 16f, MathF.Floor((0.3f + 0.4f * w.Depth) * w.Size * head * 0.07f * 10000) + Math.Clamp(w.Depth, 0, 0.99f) * 0.99f);
                spheres[count++] = new Vector4(at, (w.Kind == 3 ? 0 : Math.Min(SphereRadius * MathF.Max(1, w.Size * w.Depth), 9.5f)) + 10 * BodyGroup(w.BoneA));
            }
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
        mask.Used = frame;
        mask.Spheres = spheres;
        mask.Cuts = cuts;
        return mask.Index;
    }

    /// <summary>0 torso, 1 left arm, 2 right arm, 3 left leg, 4 right leg. In the bind pose the hands hang beside the hips, so a wound opens only its own group's anatomy.</summary>
    public static int BodyGroup(string bone)
    {
        bool left = bone.Contains(" L "), right = bone.Contains(" R ");
        if (!left && !right || bone.Contains("Clavicle")) return 0;
        bool leg = bone.Contains("Thigh") || bone.Contains("Calf") || bone.Contains("Foot") || bone.Contains("Toe");
        return (leg ? 3 : 1) + (right ? 1 : 0);
    }

    readonly System.Collections.Concurrent.ConcurrentDictionary<SkeletonRig, uint[]> boneGroups = new();

    /// <summary>Each bone's <see cref="BodyGroup"/>, four bits a bone in 8 words: the palette header's, for a worn vertex to know its group.</summary>
    public uint[] BoneGroups(SkeletonRig rig) => boneGroups.GetOrAdd(rig, static r =>
    {
        var words = new uint[8];
        foreach (var bone in r.Skeleton.Bones)
            if (bone.Handle < 64) words[bone.Handle / 8] |= (uint)(bone.Name == "Bip01 Jaw" ? JawGroup : BodyGroup(bone.Name)) << (bone.Handle % 8 * 4);
        return words;
    });

    /// <summary>A light cut as one thin unbroken line along its axis, where the ragged profile alone would leave dots.</summary>
    static float Scratch(float x, float y, in CharacterWoundMark w)
    {
        float along = (x * w.Cosine + y * w.Sine) / 1.2f, across = -x * w.Sine + y * w.Cosine;
        return MathF.Abs(along) >= 1 ? 0 : MathF.Max(0, 1 - MathF.Abs(across) / (0.09f * (1 - along * along) + 0.01f));
    }

    readonly System.Collections.Concurrent.ConcurrentDictionary<SkeletonRig, int[]> chains = new();

    /// <summary>The body group a bone hangs from, walking up to a named limb bone (a hand's prop bones count as its arm).</summary>
    public int GroupOfBone(SkeletonRig rig, int handle)
    {
        var groups = chains.GetOrAdd(rig, static r =>
        {
            var names = new string[r.BoneCount];
            foreach (var bone in r.Skeleton.Bones) if (bone.Handle < names.Length) names[bone.Handle] = bone.Name;
            var result = new int[r.BoneCount];
            for (int h = 0; h < result.Length; h++)
                for (int x = h; x >= 0 && x < names.Length; x = r.Parent[x])
                    if (names[x] is { } name && BodyGroup(name) != 0) { result[h] = BodyGroup(name); break; }
            return result;
        });
        return handle >= 0 && handle < groups.Length ? groups[handle] : 0;
    }

    /// <summary>The cuts of the character's mask, as the shader draws them: UV, angle, length x 10000 + depth.</summary>
    public Vector4[]? Cuts(long key) => masks.TryGetValue(key, out var mask) ? mask.Cuts : null;

    /// <summary>The wound spheres of the character's mask.</summary>
    public Vector4[]? Spheres(long key) => masks.TryGetValue(key, out var mask) ? mask.Spheres : null;

    static int Style(in CharacterWoundMark w) => w.Kind == 1 && w.WeaponCategory is 3 or 4 ? 4
        : w.Kind == 1 && w.WeaponCategory == 0 ? 5 : Math.Clamp(w.Kind, 0, 3);

    Vector3? Paint(in CharacterWoundMark w, SkeletonRig rig, WoundPoint[] points, out Vector2 uv)
    {
        uv = default;
        // A face takes far more UV per world unit than a body.
        float chart = w.BoneB.Contains("Head") ? ChartUv * 0.5f : ChartUv;
        chart *= 0.3f + 0.4f * w.Depth;   // a light cut stays small, a deep one opens wide
        if (rig.Handle(w.BoneA) is not { } a || rig.Handle(w.BoneB) is not { } b || a >= rig.BindPosition.Length || b >= rig.BindPosition.Length) return null;
        var target = Vector3.Lerp(rig.BindPosition[a], rig.BindPosition[b], w.T) + w.Side * 1.2f;   // out to the skin, so a slanted side lands off the bone
        float best = float.MaxValue;
        var hit = Vector3.Zero;
        foreach (ref readonly var p in points.AsSpan())
        {
            if (Vector3.Dot(p.Normal, w.Side) < 0.2f) continue;
            float d = Vector3.DistanceSquared(p.Position, target);
            if (d < best) (best, uv, hit) = (d, p.Uv, p.Position);
        }
        if (best == float.MaxValue) return null;
        // The head sits in v -1..0, the body in 0..1: each its half of the mask.
        int cx = (int)(uv.X * Size), cy = (int)((uv.Y + 1) * 0.5f * Size),  reach = (int)(3 * chart * Size);
        for (int y = -reach; y <= reach; y++)
            for (int x = -reach; x <= reach; x++)
            {
                var s = Sample!(x / (chart * Size), y * 2 / (chart * Size), w);
                if (w.Depth < 0.3f && w.Kind != 3) s = Vector3.Max(s, new Vector3(0, Scratch(x / (chart * Size), y * 2 / (chart * Size), w), 0));
                if (s.Z <= 0) continue;
                int i = ((((cy + y) % Size + Size) % Size) * Size + ((cx + x) % Size + Size) % Size) * 4;
                pixels[i + 2] = Math.Max(pixels[i + 2], (byte)(s.Z * 255));
                pixels[i + 3] = Math.Max(pixels[i + 3], (byte)(w.Depth * MathF.Min(1, s.Y * 1.3f) * 255));   // alpha: how deep the cut goes here
            }
        if (w.Depth > 0.25f && w.Kind != 3) Runs(cx, cy, w);
        return hit;
    }

    /// <summary>Blood run down the skin from a cut (mask channel: green), meandering, each its own length and thinning to its end: the same every repaint of a wound.</summary>
    void Runs(int cx, int cy, in CharacterWoundMark w)
    {
        var random = new Random(cx * 7919 + cy);
        int count = 1 + (int)(w.Depth * 1.6f);
        for (int run = 0; run < count; run++)
        {
            float x = cx + (random.NextSingle() - 0.5f) * 14, drift = 0, width = 1.6f + random.NextSingle() * 1.6f;
            int length = 24 + (int)(random.NextSingle() * random.NextSingle() * 80);
            for (int step = 0; step < length; step++)
            {
                float head = length * w.Flow - step;   // the run's front, swelling as a drop
                if (head < 0) { for (; step < length; step++) { random.NextSingle(); } break; }
                drift = Math.Clamp(drift + (random.NextSingle() - 0.5f) * 0.5f, -0.7f, 0.7f);
                x += drift;
                float half = width * (1 - (float)step / length * 0.65f) * (w.Flow < 1 && head < 4 ? 1.5f : 1);
                for (int dx = (int)-half; dx <= half; dx++)
                {
                    int i = (((cy + step) % Size + Size) % Size * Size + (((int)x + dx) % Size + Size) % Size) * 4;
                    pixels[i + 1] = Math.Max(pixels[i + 1], (byte)(255 * (1 - MathF.Abs(dx) / (half + 1)) * (1 - 0.5f * step / length)));
                }
            }
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
