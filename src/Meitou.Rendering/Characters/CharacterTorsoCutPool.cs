using System.Numerics;
using System.Runtime.InteropServices;
using Meitou.Rendering.Gpu;

namespace Meitou.Rendering.Characters;

/// <summary>At most 64 live half-body cap buffers, reused without growing renderer batch identities.</summary>
sealed class CharacterTorsoCutPool(IGpuContext gpu) : IDisposable
{
    const int Capacity = 64, VertexCapacity = 32768;
    sealed class Slot
    {
        public long Key, Seen;
        public CharacterAsset? Asset;
        public CharacterTorsoCut Cut;
        public uint MorphBase;
        public int[]? Components;
        public AssetPart? Cap;
        public IReadOnlyList<AssetPart>? SourceParts, View;
        public bool Ready;
        public Vertex[] Rim = [];
    }
    readonly List<Slot> slots = [];
    readonly Dictionary<long, Slot> byKey = [];
    readonly PartMaterial material = new() { Shading = CharacterShaders.ShadeItem, VertexColoured = true, DoubleSided = true, Description = "torso cut flesh" };

    readonly PartMaterial mechanicalMaterial = new() { Shading = CharacterShaders.ShadeItem, VertexColoured = true,
        DoubleSided = true, Mechanical = true, Description = "robot cut metal" };

    public IReadOnlyList<AssetPart> Resolve(long key, CharacterAsset asset, IReadOnlyList<AssetPart> parts, in CharacterTorsoCut cut, uint[] groups, long stamp, int[]? components = null)
    {
        var source = cut.HeadOnly ? HeadCutSurface.Skin(asset) : asset.BodySource;
        if (asset.Blending || source is not { Length: > 0 }) return parts;
        if (!byKey.TryGetValue(key, out var slot))
        {
            if (slots.Count < Capacity) { slot = new Slot(); slots.Add(slot); }
            else
            {
                slot = slots.MinBy(s => s.Seen)!;
                if (stamp - slot.Seen <= 60) return parts;
                byKey.Remove(slot.Key); slot.Asset = null;
            }
            slot.Key = key; byKey[key] = slot;
        }
        slot.Seen = stamp;
        bool sameComponents = ReferenceEquals(slot.Components, components) || slot.Components is { } prior && components is { } currentComponents && prior.AsSpan().SequenceEqual(currentComponents);
        if (!ReferenceEquals(slot.Asset, asset) || slot.Cut != cut || slot.MorphBase != asset.MorphBase || !sameComponents)
        {
            (slot.Asset, slot.Cut, slot.MorphBase) = (asset, cut, asset.MorphBase);
            slot.SourceParts = null;
            slot.Components = components;
            var morphs = asset.Morph?.Poses.Deltas(asset.Morph.Weights, asset.Morph.CutHorns);
            uint bloodColour = unchecked((uint)asset.Appearance.Race.GetInt("blood colour", 0x800000));
            bool mechanical = asset.Appearance.Race.GetBool("is robot");
            var vertices = TorsoCutSurface.Build(source, groups, cut, morphs, components, bloodColour, mechanical: mechanical);
            vertices = WithBrainSection(vertices, asset, groups, cut, components, bloodColour);
            slot.Ready = vertices is { Length: > 0 and <= VertexCapacity };
            slot.Rim = slot.Ready ? CutContactGeometry.Sample(vertices!) : [];
            if (slot.Ready)
            {
                slot.Cap ??= Allocate();
                slot.Cap.Material = mechanical ? mechanicalMaterial : material;
                using var uploads = gpu.Uploads.Begin();
                uploads.Write(slot.Cap.Mesh.Parts[0].Vertices, 0, MemoryMarshal.AsBytes(vertices!.AsSpan()));
                slot.Cap.Mesh.Parts[0].Count[0] = vertices!.Length;
                var current = vertices!;
                var min = current.Aggregate(new Vector3(float.MaxValue), (p, v) => Vector3.Min(p, v.Position));
                var max = current.Aggregate(new Vector3(float.MinValue), (p, v) => Vector3.Max(p, v.Position));
                slot.Cap.BoundsCentre = slot.Cap.Mesh.Centre = (min + max) / 2;
                slot.Cap.BoundsRadius = slot.Cap.Mesh.Radius = Vector3.Distance(min, max) / 2;
            }
        }
        if (!slot.Ready) return parts;
        if (!ReferenceEquals(slot.SourceParts, parts))
        {
            slot.SourceParts = parts;
            slot.View = [.. parts, slot.Cap!];
        }
        return slot.View!;
    }

    static Vertex[]? WithBrainSection(Vertex[]? flesh, CharacterAsset asset, uint[] groups, in CharacterTorsoCut cut,
        int[]? components, uint bloodColour)
    {
        if (!cut.FullBody && !cut.HeadOnly || asset.BrainSource is not { Length: > 0 } brain) return flesh;
        var section = TorsoCutSurface.Build(brain, groups, cut, components: components, bloodColour: bloodColour, brainSection: true);
        if (section is not { Length: > 0 } || (flesh?.Length ?? 0) + section.Length > VertexCapacity) return flesh;
        return [.. flesh ?? [], .. section];
    }

    public ReadOnlySpan<Vertex> Rim(long key) => byKey.TryGetValue(key, out var slot) && slot.Ready ? slot.Rim : [];

    AssetPart Allocate()
    {
        gpu.EnsureFrame();
        var vb = DeviceBuffer.Create(gpu, (ulong)(VertexCapacity * Vertex.Size), BufferUse.Vertex, "torso cut slots");
        var ib = DeviceBuffer.Create(gpu, VertexCapacity * sizeof(uint), BufferUse.Index, "torso cut slots");
        var indices = new uint[VertexCapacity];
        for (uint i = 0; i < indices.Length; i++) indices[i] = i;
        using (var uploads = gpu.Uploads.Begin()) uploads.Write(ib, 0, MemoryMarshal.AsBytes(indices.AsSpan()));
        var mesh = new GpuObjectMesh { Distances = [0], Manual = new GpuObjectMesh?[1], Centre = default, Radius = 4 };
        mesh.Parts.Add(new GpuObjectPart { SubMeshIndex = 0, MaterialName = "torso cut flesh", HasTangents = false, HasColours = true, Vertices = vb, Indices = ib, Offset = [0], Count = [0], Attributes = GpuVertex.FloatAttributes(vb) });
        return new AssetPart { Label = "torso cut flesh", Mesh = mesh, Shared = true, Anatomy = true, IsTorsoCap = true, BoundsCentre = default, BoundsRadius = 4, Material = material };
    }

    public void Dispose()
    {
        foreach (var slot in slots)
            if (slot.Cap is { } cap)
                foreach (var part in cap.Mesh.Parts) { part.Vertices.Dispose(); part.Indices.Dispose(); }
        slots.Clear(); byKey.Clear();
    }
}
