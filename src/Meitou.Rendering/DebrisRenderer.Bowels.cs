using System.Numerics;
using Meitou.Data.Physics;
using Meitou.Rendering.Gpu;

namespace Meitou.Rendering;

public sealed unsafe partial class DebrisRenderer
{
    const int BowelCapacity = 32;
    sealed class BowelSlot
    {
        public long Key, Seen;
        public required Mesh Mesh;
        public required Vertex[] Vertices;
        public required byte[] Packed;
        public Matrix4x4[] LastTransforms = [];
        public bool Ready;
        public Vector3 Min, Max;
    }

    BowelSkinning? bowelSkinning;
    readonly List<BowelSlot> bowelSlots = [];
    readonly Dictionary<long, BowelSlot> bowelsByKey = [];
    long bowelStamp;

    public BowelRigGeometry? BowelRig()
    {
        _ = Load(Bowel, null);
        return bowelSkinning?.Geometry;
    }

    /// <summary>Uploads tissue poses before any world pass reads the reused vertex buffers.</summary>
    public void PrepareBowels(ReadOnlySpan<DebrisPiece> pieces)
    {
        if (bowelSkinning is null || !meshes.TryGetValue(Bowel, out var source) || source is null) return;
        long stamp = ++bowelStamp;
        var active = new HashSet<long>();
        foreach (var piece in pieces) if (piece.Bowel is { } pose) active.Add(pose.Key);
        foreach (var piece in pieces)
        {
            if (piece.Bowel is not { } pose || !Finite(pose.Centre)) continue;
            if (!bowelsByKey.TryGetValue(pose.Key, out var slot))
            {
                if (bowelSlots.Count < BowelCapacity)
                {
                    var vb = DeviceBuffer.Create(gpu, source.Vertices.Size, BufferUse.Vertex, "deformed bowel slots");
                    slot = new BowelSlot { Mesh = new Mesh(vb, source.Indices, source.Parts, source.Extent),
                        Vertices = new Vertex[(int)(source.Vertices.Size / GpuVertex.ObjectSize)], Packed = new byte[(int)source.Vertices.Size] };
                    bowelSlots.Add(slot);
                }
                else
                {
                    slot = bowelSlots.Where(s => !active.Contains(s.Key)).MinBy(s => s.Seen);
                    if (slot is null) continue;
                    bowelsByKey.Remove(slot.Key);
                }
                slot.Key = pose.Key;
                slot.Ready = false;
                slot.LastTransforms = [];
                bowelsByKey[slot.Key] = slot;
            }
            slot.Seen = stamp;
            if (slot.Ready && slot.LastTransforms.AsSpan().SequenceEqual(pose.Transforms)) continue;
            if (!bowelSkinning.Deform(pose.Transforms, slot.Vertices)) continue;
            GpuVertex.PackObject(slot.Vertices, slot.Packed);
            using (var upload = gpu.Uploads.Begin()) upload.Write(slot.Mesh.Vertices, 0, slot.Packed);
            slot.Min = slot.Vertices.Aggregate(new Vector3(float.MaxValue), (p, v) => Vector3.Min(p, v.Position));
            slot.Max = slot.Vertices.Aggregate(new Vector3(float.MinValue), (p, v) => Vector3.Max(p, v.Position));
            slot.Mesh.Extent = Vector3.Max(Vector3.Abs(slot.Min), Vector3.Abs(slot.Max));
            slot.LastTransforms = (Matrix4x4[])pose.Transforms.Clone();
            slot.Ready = true;
        }
    }

    Mesh? BowelMesh(BowelPose pose) => bowelsByKey.TryGetValue(pose.Key, out var slot) && slot.Ready ? slot.Mesh : null;

    internal (Vector3 Min, Vector3 Max)? BowelBounds(BowelPose pose) =>
        bowelsByKey.TryGetValue(pose.Key, out var slot) && slot.Ready ? (slot.Min + pose.Centre, slot.Max + pose.Centre) : null;

    void DisposeBowels()
    {
        foreach (var slot in bowelSlots) slot.Mesh.Vertices.Dispose();
        bowelSlots.Clear();
        bowelsByKey.Clear();
        bowelSkinning = null;
    }

    static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
}
