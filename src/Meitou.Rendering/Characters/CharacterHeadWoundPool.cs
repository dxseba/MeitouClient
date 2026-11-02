using System.Numerics;
using System.Runtime.InteropServices;
using Meitou.Rendering.Gpu;

namespace Meitou.Rendering.Characters;

readonly record struct HeadWoundView(IReadOnlyList<AssetPart> Parts, Matrix4x4 Frame, bool ReliefReady);

/// <summary>Bounded per-character cut geometry. Stable slot/part/buffer identities also bound renderer batch IDs.</summary>
sealed class CharacterHeadWoundPool(IGpuContext gpu) : IDisposable
{
    const int Capacity = 64, VertexCapacity = 4096;
    sealed class Slot
    {
        public long Key, Seen;
        public CharacterAsset? Asset;
        public CharacterHeadWoundPlacement Placement;
        public uint HeadState, MorphBase;
        public AssetPart? Relief;
        public HeadWoundView View;
    }
    readonly List<Slot> slots = [];
    readonly Dictionary<long, Slot> byKey = [];
    readonly PartMaterial material = new() { Shading = CharacterShaders.ShadeItem, VertexColoured = true, DoubleSided = true, Description = "placed head wound" };

    public static HeadWoundView Fallback(CharacterAsset asset, uint headState)
    {
        int mechanism = (int)(headState >> 12 & 3);
        var frame = default(Matrix4x4);
        frame.M42 = mechanism;
        return new(mechanism >= 2 ? asset.HeadPaintedParts : asset.Parts, frame, false);
    }

    public HeadWoundView Resolve(long key, CharacterAsset asset, in CharacterHeadWoundPlacement placement, uint headState, long stamp)
    {
        if (asset.Appearance.Headless || asset.Animal || asset.Appearance.Race.GetBool("is robot") || asset.Blending || asset.HeadSource is not { Length: > 0 } source || asset.HeadBone < 0 || (headState >> 7 & 3) == 0)
            return Fallback(asset, headState);
        if (!byKey.TryGetValue(key, out var slot))
        {
            if (slots.Count < Capacity) { slot = new Slot(); slots.Add(slot); }
            else
            {
                slot = slots.MinBy(s => s.Seen)!;
                // More than the in-flight frame ring; never steal a slot still used by a recent view.
                if (stamp - slot.Seen <= 60) return Fallback(asset, headState);
                byKey.Remove(slot.Key);
                slot.Asset = null;
            }
            slot.Key = key;
            byKey[key] = slot;
        }
        slot.Seen = stamp;
        uint state = headState & 0x3F80;
        if (ReferenceEquals(slot.Asset, asset) && slot.Placement == placement && slot.HeadState == state && slot.MorphBase == asset.MorphBase)
            return slot.View;
        (slot.Asset, slot.Placement, slot.HeadState, slot.MorphBase) = (asset, placement, state, asset.MorphBase);
        int profile = Math.Clamp((int)(headState >> 9 & 7), 0, 5);
        int mechanism = (int)(headState >> 12 & 3);
        if (!HeadWoundSurface.TryProjectPlacement(source, placement, profile, out var frame, mechanism))
            return slot.View = Fallback(asset, headState);
        var parts = asset.HeadPaintedParts.ToList();
        var morphs = asset.Morph?.Poses.Deltas(asset.Morph.Weights, asset.Morph.CutHorns);
        var vertices = HeadWoundSurface.FitPlacementVertices(source, frame, asset.HeadBone, morphs, profile, (headState >> 7 & 3) >= 2, out var fitted,
            unchecked((uint)asset.Appearance.Race.GetInt("blood colour", 0x800000)));
        frame = fitted;
        bool ready = vertices is { Length: > 0 and <= VertexCapacity };
        if (ready)
        {
            slot.Relief ??= Allocate();
            using var uploads = gpu.Uploads.Begin();
            uploads.Write(slot.Relief.Mesh.Parts[0].Vertices, 0, MemoryMarshal.AsBytes(vertices!.AsSpan()));
            slot.Relief.Mesh.Parts[0].Count[0] = vertices!.Length;
            slot.Relief.BoundsCentre = frame.Centre;
            slot.Relief.Mesh.Centre = frame.Centre;
            parts.Add(slot.Relief);
        }
        return slot.View = new(parts.ToArray(), frame.Packed, ready);
    }

    AssetPart Allocate()
    {
        gpu.EnsureFrame();
        var vb = DeviceBuffer.Create(gpu, (ulong)(VertexCapacity * Vertex.Size), BufferUse.Vertex, "head wound slots");
        var ib = DeviceBuffer.Create(gpu, VertexCapacity * sizeof(uint), BufferUse.Index, "head wound slots");
        var indices = new uint[VertexCapacity];
        for (uint i = 0; i < indices.Length; i++) indices[i] = i;
        using (var uploads = gpu.Uploads.Begin()) uploads.Write(ib, 0, MemoryMarshal.AsBytes(indices.AsSpan()));
        var mesh = new GpuObjectMesh { Distances = [0], Manual = new GpuObjectMesh?[1], Centre = default, Radius = 2 };
        mesh.Parts.Add(new GpuObjectPart { SubMeshIndex = 0, MaterialName = "placed head wound", HasTangents = false, HasColours = true, Vertices = vb, Indices = ib, Offset = [0], Count = [0], Attributes = GpuVertex.FloatAttributes(vb) });
        return new AssetPart { Label = "placed head wound", Mesh = mesh, Shared = true, Anatomy = true, IsHeadRelief = true, BoundsCentre = default, BoundsRadius = 2, Material = material };
    }

    public void Dispose()
    {
        foreach (var slot in slots)
            if (slot.Relief is { } part)
                foreach (var p in part.Mesh.Parts) { p.Vertices.Dispose(); p.Indices.Dispose(); }
        slots.Clear();
        byKey.Clear();
    }
}
