// Adapted from the integrated GPL-3.0-or-later renderer at 3a15eeb9f5523b9d106b40fd0ec99ccdb2658efa.
using Meitou.Rendering;
using System.Numerics;
using System.Runtime.InteropServices;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;
using Meitou.Rendering.Gpu;

namespace Meitou.Brutality.Rendering.Surface;

/// <summary>
/// The face and body poses (Ogre morph targets) of a mesh as sparse per-vertex slots (docs/character-renderer.md, "Face poses"): the vertices any pose moves
/// get a slot number 1..n (stored in the vertex's colour X, see <see cref="CharacterContent"/>), and each pose is a list of (slot, offset). An appearance's
/// morph is one offset per slot, the poses summed at their weights, kept in the <see cref="MorphArena"/>; the vertex program adds it before skinning.
/// </summary>
internal sealed class NativeMeshMorphs
{
    public required int SlotCount;
    /// <summary>The submesh the poses move (all of them target one).</summary>
    public required int SubMeshIndex;
    public required Dictionary<string, (int[] Slots, Vector3[] Offsets)> Poses;
    public bool HasCuttableHorns => Poses.ContainsKey("bone_horns_top_short");

    /// <summary>The poses of <paramref name="mesh"/> on <paramref name="model"/>'s parts; null when it has none, or they do not line up with the part's vertices.</summary>
    public static NativeMeshMorphs? From(OgreMesh mesh, Model model, List<string> messages, string name)
    {
        if (mesh.Poses.Count == 0) return null;
        int target = mesh.Poses.Where(p => p.Target > 0).Select(p => p.Target - 1).DefaultIfEmpty(-1).First();
        if (target < 0 || target >= mesh.SubMeshes.Count) return null;
        var part = model.Parts.FirstOrDefault(p => p.SubMeshIndex == target);
        if (part is null || mesh.SubMeshes[target].VertexData is not { } data || part.Vertices.Length != data.VertexCount)
        {
            messages.Add($"{name}: poses do not line up with the part's vertices, not morphed");
            return null;
        }
        var slotOf = new Dictionary<uint, int>();
        foreach (var pose in mesh.Poses)
            if (pose.Target - 1 == target)
                foreach (var v in pose.Vertices)
                    if (v.Index < part.Vertices.Length && !slotOf.ContainsKey(v.Index)) slotOf[v.Index] = slotOf.Count + 1;
        var poses = new Dictionary<string, (int[], Vector3[])>(StringComparer.Ordinal);
        foreach (var pose in mesh.Poses)
        {
            if (pose.Target - 1 != target) continue;
            var valid = pose.Vertices.Where(v => v.Index < part.Vertices.Length).ToArray();
            poses[pose.Name] = ([.. valid.Select(v => slotOf[v.Index])], [.. valid.Select(v => v.Offset)]);
        }
        // The vertex's slot rides in the colour's X as raw bits (the characters use no vertex colours): 0 for a vertex no pose moves.
        foreach (var p in model.Parts)
            for (int i = 0; i < p.Vertices.Length; i++)
            {
                uint slot = p.SubMeshIndex == target && slotOf.TryGetValue((uint)i, out int s) ? (uint)s : 0;
                p.Vertices[i].Colour = new Vector4(BitConverter.UInt32BitsToSingle(slot), 0, 0, 1);
            }
        return new NativeMeshMorphs { SlotCount = slotOf.Count, SubMeshIndex = target, Poses = poses! };
    }

    /// <summary>One offset per slot for the appearance's pose weights (the file's values baked as Kenshi does, <see cref="CharacterShape.BakeWeight"/>); null when none applies.</summary>
    public Vector4[]? Deltas(IReadOnlyDictionary<string, float> weights, bool cutHorns)
    {
        Vector4[]? result = null;
        foreach (var (name, stored) in weights)
        {
            if (!Poses.TryGetValue(name, out var pose) || CharacterShape.BakeWeight(name, stored, cutHorns) is not { } w) continue;
            result ??= new Vector4[SlotCount];
            for (int i = 0; i < pose.Slots.Length; i++) result[pose.Slots[i] - 1] += new Vector4(pose.Offsets[i] * w, 0);
        }
        return result;
    }
}
