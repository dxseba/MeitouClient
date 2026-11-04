using System.Numerics;
using System.Runtime.InteropServices;
using Meitou.Rendering.Gpu;

namespace Meitou.Rendering.Characters;

/// <summary>One consolidated cap buffer per draw key, rebuilt only when permanent limb planes, morphs or components change.</summary>
sealed class CharacterLimbCutPool(IGpuContext gpu) : IDisposable
{
    const int Capacity = 64;
    sealed class Slot
    {
        public long Key, Seen;
        public CharacterAsset? Asset;
        public LimbCutPlanes Planes;
        public uint PartOnly, MorphBase;
        public int[]? Components;
        public AssetPart? Cap;
        public IReadOnlyList<AssetPart>? SourceParts, View;
        public bool Ready;
        public Vertex[][] Rims = [[], [], [], []];
    }
    readonly List<Slot> slots = [];
    readonly Dictionary<long, Slot> byKey = [];
    readonly PartMaterial flesh = new() { Shading = CharacterShaders.ShadeItem, VertexColoured = true,
        DoubleSided = true, Description = "limb cut flesh" };
    readonly PartMaterial metal = new() { Shading = CharacterShaders.ShadeItem, VertexColoured = true,
        DoubleSided = true, Mechanical = true, Description = "limb cut metal" };

    public IReadOnlyList<AssetPart> Resolve(long key, CharacterAsset asset, IReadOnlyList<AssetPart> parts,
        in LimbCutPlanes planes, uint partOnly, uint[] groups, long stamp, int[]? components)
    {
        if (planes.Mask == 0 || asset.Blending || asset.BodySource is not { Length: > 0 } source) return parts;
        if (!byKey.TryGetValue(key, out var slot))
        {
            if (slots.Count < Capacity) { slot = new(); slots.Add(slot); }
            else
            {
                slot = slots.MinBy(s => s.Seen)!;
                if (stamp - slot.Seen <= 60) return parts;
                byKey.Remove(slot.Key); slot.Asset = null;
            }
            slot.Key = key; byKey[key] = slot;
        }
        slot.Seen = stamp;
        bool sameComponents = ReferenceEquals(slot.Components, components)
            || slot.Components is { } old && components is { } current && old.AsSpan().SequenceEqual(current);
        if (!ReferenceEquals(slot.Asset, asset) || slot.Planes != planes || slot.PartOnly != partOnly
            || slot.MorphBase != asset.MorphBase || !sameComponents)
        {
            (slot.Asset, slot.Planes, slot.PartOnly, slot.MorphBase, slot.Components) = (asset, planes, partOnly, asset.MorphBase, components);
            slot.SourceParts = null;
            uint colour = unchecked((uint)asset.Appearance.Race.GetInt("blood colour", 0x800000));
            bool mechanical = asset.Appearance.Race.GetBool("is robot");
            var morphs = asset.Morph?.Poses.Deltas(asset.Morph.Weights, asset.Morph.CutHorns);
            var vertices = LimbCutSurface.Build(source, groups, planes, partOnly, morphs, components, colour, mechanical, asset.Rig);
            slot.Ready = vertices.Length > 0;
            for (int limb = 0; limb < 4; limb++)
                slot.Rims[limb] = CutContactGeometry.Sample(vertices.Where(v => TorsoCutSurface.Group(v, groups) == limb + 1)
                    .Select(v => v with { Tangent = v.Tangent with { W = TorsoCutSurface.Layer } }).ToArray());
            if (slot.Ready) Upload(slot, vertices, mechanical);
        }
        if (!slot.Ready) return parts;
        if (!ReferenceEquals(slot.SourceParts, parts)) { slot.SourceParts = parts; slot.View = [.. parts, slot.Cap!]; }
        return slot.View!;
    }

    public ReadOnlySpan<Vertex> Rim(long key, int slot) => slot is >= 0 and < 4 && byKey.TryGetValue(key, out var cached)
        && cached.Ready ? cached.Rims[slot] : [];

    void Upload(Slot slot, Vertex[] vertices, bool mechanical)
    {
        slot.Cap ??= Allocate();
        slot.Cap.Material = mechanical ? metal : flesh;
        using var uploads = gpu.Uploads.Begin();
        uploads.Write(slot.Cap.Mesh.Parts[0].Vertices, 0, MemoryMarshal.AsBytes(vertices.AsSpan()));
        slot.Cap.Mesh.Parts[0].Count[0] = vertices.Length;
        var min = vertices.Aggregate(new Vector3(float.MaxValue), (p, v) => Vector3.Min(p, v.Position));
        var max = vertices.Aggregate(new Vector3(float.MinValue), (p, v) => Vector3.Max(p, v.Position));
        slot.Cap.BoundsCentre = slot.Cap.Mesh.Centre = (min + max) * .5f;
        slot.Cap.BoundsRadius = slot.Cap.Mesh.Radius = Vector3.Distance(min, max) * .5f;
    }

    AssetPart Allocate()
    {
        gpu.EnsureFrame();
        var vb = DeviceBuffer.Create(gpu, (ulong)(LimbCutSurface.VertexLimit * Vertex.Size), BufferUse.Vertex, "limb cut slots");
        var ib = DeviceBuffer.Create(gpu, LimbCutSurface.VertexLimit * sizeof(uint), BufferUse.Index, "limb cut slots");
        var indices = new uint[LimbCutSurface.VertexLimit];
        for (uint i = 0; i < indices.Length; i++) indices[i] = i;
        using (var uploads = gpu.Uploads.Begin()) uploads.Write(ib, 0, MemoryMarshal.AsBytes(indices.AsSpan()));
        var mesh = new GpuObjectMesh { Distances = [0], Manual = new GpuObjectMesh?[1], Centre = default, Radius = 4 };
        mesh.Parts.Add(new GpuObjectPart { SubMeshIndex = 0, MaterialName = "limb cut", HasTangents = false, HasColours = true,
            Vertices = vb, Indices = ib, Offset = [0], Count = [0], Attributes = GpuVertex.FloatAttributes(vb) });
        return new AssetPart { Label = "limb cut", Mesh = mesh, Shared = true, Anatomy = true, IsLimbCap = true,
            BoundsCentre = default, BoundsRadius = 4, Material = flesh };
    }

    public void Dispose()
    {
        foreach (var slot in slots)
            if (slot.Cap is { } cap)
                foreach (var part in cap.Mesh.Parts) { part.Vertices.Dispose(); part.Indices.Dispose(); }
        slots.Clear(); byKey.Clear();
    }
}
