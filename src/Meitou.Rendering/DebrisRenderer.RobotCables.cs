using System.Numerics;
using System.Runtime.InteropServices;

using Meitou.Rendering.Gpu;

namespace Meitou.Rendering;

public sealed record RobotCablePose(long Key, Vector3 Centre, Vector3[] Nodes);

public sealed unsafe partial class DebrisRenderer
{
    public const string RobotCable = "@robot-cable";
    public const int RobotCableLimit = 32, RobotCableNodes = 7;
    const int CableSides = 5, CableVertices = (RobotCableNodes - 1) * CableSides * 6;
    sealed class CableSlot
    {
        public long Key, Seen;
        public bool Uploaded;
        public readonly Vector3[] Nodes = new Vector3[RobotCableNodes];
        public required Mesh Mesh;
        public readonly Vertex[] Vertices = new Vertex[CableVertices];
        public readonly byte[] Packed = new byte[CableVertices * GpuVertex.ObjectSize];
    }
    readonly List<CableSlot> cableSlots = [], dirtyCables = [];
    readonly Dictionary<long, CableSlot> cables = [];
    long cableStamp;

    Mesh BuildRobotCable()
    {
        var vb = DeviceBuffer.Create(gpu, CableVertices * GpuVertex.ObjectSize, BufferUse.Vertex, "robot cable prototype");
        var ib = DeviceBuffer.Create(gpu, CableVertices * sizeof(uint), BufferUse.Index, "robot cable indices");
        uint[] indices = [.. Enumerable.Range(0, CableVertices).Select(i => (uint)i)];
        using var upload = gpu.Uploads.Begin();
        upload.Write(ib, 0, MemoryMarshal.AsBytes(indices.AsSpan()));
        return new Mesh(vb, ib, [(0, CableVertices, 0, null, -4)], new Vector3(5));
    }

    public void PrepareRobotCables(ReadOnlySpan<DebrisPiece> pieces)
    {
        bool any = false;
        foreach (var piece in pieces) if (piece.Cable is not null) { any = true; break; }
        if (!any) return;
        var source = Load(RobotCable, null);
        if (source is null) return;
        long stamp = ++cableStamp;
        dirtyCables.Clear();
        foreach (var piece in pieces)
        {
            if (piece.Cable is not { Nodes.Length: RobotCableNodes } pose) continue;
            var slot = CableSlotFor(pose.Key, source, stamp);
            if (slot is null) continue;
            slot.Seen = stamp;
            bool changed = !slot.Uploaded;
            for (int i = 0; i < RobotCableNodes; i++)
                changed |= Vector3.DistanceSquared(slot.Nodes[i], pose.Nodes[i] - pose.Centre) > 0.000001f;
            if (!changed) continue;
            for (int i = 0; i < RobotCableNodes; i++) slot.Nodes[i] = pose.Nodes[i] - pose.Centre;
            FillCable(slot, pose);
            dirtyCables.Add(slot);
        }
        if (dirtyCables.Count == 0) return;
        using var upload = gpu.Uploads.Begin();
        foreach (var slot in dirtyCables)
        {
            upload.Write(slot.Mesh.Vertices, 0, slot.Packed);
            slot.Uploaded = true;
        }
    }

    CableSlot? CableSlotFor(long key, Mesh source, long stamp)
    {
        if (cables.TryGetValue(key, out var slot)) return slot;
        if (cableSlots.Count < RobotCableLimit)
        {
            var vb = DeviceBuffer.Create(gpu, source.Vertices.Size, BufferUse.Vertex, "robot cable slot");
            slot = new CableSlot { Mesh = new Mesh(vb, source.Indices, source.Parts, source.Extent) };
            cableSlots.Add(slot);
        }
        else
        {
            slot = cableSlots.MinBy(s => s.Seen);
            if (slot is null || slot.Seen == stamp) return null;
            cables.Remove(slot.Key);
        }
        slot.Uploaded = false;
        slot.Key = key;
        cables[key] = slot;
        return slot;
    }

    static void FillCable(CableSlot slot, RobotCablePose pose)
    {
        int written = 0;
        for (int segment = 0; segment < RobotCableNodes - 1; segment++)
        {
            var a = pose.Nodes[segment] - pose.Centre;
            var b = pose.Nodes[segment + 1] - pose.Centre;
            var axis = b - a;
            if (axis.LengthSquared() < 1e-10f) axis = Vector3.UnitY;
            axis = Vector3.Normalize(axis);
            var right = Vector3.Normalize(Vector3.Cross(axis, MathF.Abs(axis.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
            var up = Vector3.Cross(axis, right);
            for (int side = 0; side < CableSides; side++)
            {
                var n0 = right * MathF.Cos(side * MathF.Tau / CableSides) + up * MathF.Sin(side * MathF.Tau / CableSides);
                var n1 = right * MathF.Cos((side + 1) * MathF.Tau / CableSides) + up * MathF.Sin((side + 1) * MathF.Tau / CableSides);
                Add(a + n0 * 0.055f, n0);
                Add(b + n1 * 0.055f, n1);
                Add(b + n0 * 0.055f, n0);
                Add(a + n0 * 0.055f, n0);
                Add(a + n1 * 0.055f, n1);
                Add(b + n1 * 0.055f, n1);
            }
        }
        GpuVertex.PackObject(slot.Vertices, slot.Packed);
        void Add(Vector3 at, Vector3 normal) => slot.Vertices[written++] = new Vertex { Position = at, Normal = normal, Colour = Vector4.One };
    }

    Mesh? RobotCableMesh(RobotCablePose pose) => cables.TryGetValue(pose.Key, out var slot) ? slot.Mesh : null;
    void DisposeRobotCables()
    {
        foreach (var slot in cableSlots) slot.Mesh.Vertices.Dispose();
        cableSlots.Clear(); cables.Clear();
    }
}