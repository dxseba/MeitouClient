using System.Numerics;
using Meitou.Brutality.Rendering;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;

namespace Meitou.Brutality;

/// <summary>Prepares fragments of the actual body mesh without touching the display thread.</summary>
internal static class StockHeadBuild
{
    internal sealed record Cut(int HeadBone, IReadOnlyList<PreparedHeadBurstShard> Pieces,
        IReadOnlyDictionary<int, IReadOnlyList<(CharacterPart Part, OgreMesh? Mesh)>>? Equipment = null);

    internal static Cut? Build(string folder, CharacterRigs rigs, CharacterAppearance look, HeadCutState cut, CancellationToken cancellation,
        CharacterAppearance? accessorySource = null)
    {
        if (look.BodyMesh is not { } mesh || rigs.FindMesh(mesh) is not { } path) return null;
        var body = OgreMeshReader.ReadFile(path);
        if (body.SkeletonName is not { } skeleton || rigs.Rig(skeleton) is not { } rig
            || rig.Handle("Bip01 Head") is not { } headBone) return null;
        var groups = MeshCut.BoneGroups(rig);
        bool shaved = look.Character?.GetBool("shaved") == true;
        var (min, max, chartMin, chartMax) = (new Vector3(float.MaxValue), new Vector3(float.MinValue),
            new Vector3(float.MaxValue), new Vector3(float.MinValue));
        foreach (var part in MeshCut.SourceModel(body, rig.BoneCount).Parts)
        {
            cancellation.ThrowIfCancellationRequested();
            bool shared = body.SubMeshes[part.SubMeshIndex].UseSharedVertices;
            var horns = body.Poses.Where(p => p.Target == (shared ? 0 : part.SubMeshIndex + 1)
                    && p.Name.Contains("horn", StringComparison.OrdinalIgnoreCase))
                .SelectMany(p => p.Vertices).Where(v => v.Offset.LengthSquared() > 0).Select(v => v.Index).ToHashSet();
            var morph = MeshCut.MorphDeltas(body, part.SubMeshIndex, part.Vertices.Length, look.PoseWeights, shaved);
            foreach (uint index in part.Indices.Distinct())
            {
                var vertex = part.Vertices[index];
                if (horns.Contains(index) || MeshCut.Group(vertex, groups) is not (6 or 7)) continue;
                var point = vertex.Position + morph[index];
                min = Vector3.Min(min, point);
                max = Vector3.Max(max, point);
                if (vertex.Uv.Y >= 0) continue;
                chartMin = Vector3.Min(chartMin, point);
                chartMax = Vector3.Max(chartMax, point);
            }
        }
        if (Vector3.Min(chartMax - chartMin, Vector3.One) == Vector3.One) (min, max) = (chartMin, chartMax);
        var span = max - min;
        if (!float.IsFinite(span.LengthSquared()) || span.X <= 0 || span.Y <= 0 || span.Z <= 0) return null;
        bool robot = look.Race.GetBool("is robot");
        var (skull, brain) = robot ? (null, null) : StockHeadAnatomy.Load(folder, rig, body, look, skeleton, min, max, cancellation);
        var mechanical = robot ? MechanicalInterior(body, rig) : null;
        var plane = new Vector4(0, 1, 0, -(min.Y + .05f * span.Y));
        var palette = StockTissuePalette.For(look);
        var colour = robot ? new Vector4(.14f, .15f, .16f, 1) : palette.Flesh;
        if (cut.Mode == HeadCutMode.Burst)
        {
            var shards = HeadBurstGeometry.PrepareShards(look, rig, body, skull, brain, plane, colour, cancellation);
            return shards.Count >= 2 ? new(headBone, shards) : null;
        }
        var split = ToBindPlane(cut.Plane, min, max);
        var halves = PrepareHalves(look, rig, body, skull, brain, mechanical, plane, split, colour, palette, cancellation);
        return halves.Count == 2 ? new(headBone, halves, CutEquipment(rigs, rig, accessorySource ?? look,
            split, halves[0].Key, halves[1].Key, cancellation)) : null;
    }

    static IReadOnlyDictionary<int, IReadOnlyList<(CharacterPart Part, OgreMesh? Mesh)>> CutEquipment(CharacterRigs rigs,
        SkeletonRig rig, CharacterAppearance look, Vector4 plane, int negativeKey, int positiveKey, CancellationToken cancellation)
    {
        var negative = new List<(CharacterPart Part, OgreMesh? Mesh)>();
        var positive = new List<(CharacterPart Part, OgreMesh? Mesh)>();
        foreach (var part in look.Parts)
        {
            cancellation.ThrowIfCancellationRequested();
            if (part.Mode != AttachMode.SharedSkeleton || part.Slot is not (AttachSlot.Hair or AttachSlot.Beard or AttachSlot.Hat or AttachSlot.Eyes)
                && part.Record != look.Hair && part.Record != look.Beard || rigs.FindMesh(part.Mesh) is not { } path) continue;
            OgreMesh source;
            try { source = OgreMeshReader.ReadFile(path); }
            catch (Exception error) when (error is IOException or InvalidDataException or OgreFormatException or UnauthorizedAccessException) { continue; }
            var lower = MeshCut.Clip(source, rig, [new(plane, -1)], buildCaps: false);
            var upper = MeshCut.Clip(source, rig, [new(plane, 1)], buildCaps: false);
            if (lower.HasTriangles) negative.Add((part, upper.HasTriangles ? lower.Skin : null));
            if (upper.HasTriangles) positive.Add((part, lower.HasTriangles ? upper.Skin : null));
        }
        return new Dictionary<int, IReadOnlyList<(CharacterPart Part, OgreMesh? Mesh)>>
            { [negativeKey] = negative, [positiveKey] = positive };
    }

    internal static OgreMesh? MechanicalInterior(OgreMesh body, SkeletonRig rig)
    {
        var source = MeshCut.SourceModel(body, rig.BoneCount).Parts.SelectMany(part => part.Vertices).ToArray();
        if (MechanicalAnatomy.Build(rig, source, [], headOnly: true) is not { } parts) return null;
        for (int i = 0; i < parts.Count; i++)
            parts[i] = parts[i] with { Colour = parts[i].Colour with { W = 1 } };
        return MeshCut.FromTriangles(parts, body.SkeletonName);
    }

    static Vector4 ToBindPlane(Vector4 plane, Vector3 min, Vector3 max)
    {
        var normal = new Vector3(plane.X, plane.Y, plane.Z) / Vector3.Max((max - min) * .5f, new(.01f));
        return new Vector4(normal, plane.W - Vector3.Dot(normal, (min + max) * .5f)) / normal.Length();
    }

    static IReadOnlyList<PreparedHeadBurstShard> PrepareHalves(CharacterAppearance look, SkeletonRig rig, OgreMesh body,
        OgreMesh? skull, OgreMesh? brain, OgreMesh? mechanical, Vector4 neck, Vector4 cut, Vector4 colour,
        StockTissuePalette palette, CancellationToken cancellation)
    {
        bool shaved = look.Character?.GetBool("shaved") == true;
        var head = MeshCut.Clip(body, rig, [new(neck, 1, 7, true, MorphedDistance: true)], look.PoseWeights, shaved, buildCaps: false);
        if (!head.HasTriangles) return [];
        var halves = new List<PreparedHeadBurstShard>(2);
        foreach (int side in new[] { -1, 1 })
        {
            cancellation.ThrowIfCancellationRequested();
            CutHalfspace[] rule = [new(cut, side, MorphedDistance: true)];
            bool hasInterior = skull is not null || brain is not null || mechanical is not null;
            var piece = MeshCut.Clip(head.Skin, rig, rule, look.PoseWeights, shaved, colour, buildCaps: !hasInterior);
            if (!piece.HasTriangles) return [];
            var tissue = new List<(OgreMesh Mesh, string Label)>();
            var lining = cut with { W = cut.W - side * .17f };
            var cap = StockHeadAnatomy.SkinBacking(head.Skin, rig, look, cut, side, colour);
            cap ??= hasInterior ? MeshCut.Clip(head.Skin, rig, [new(lining, side, MorphedDistance: true)],
                look.PoseWeights, shaved, colour).Wound : piece.Wound;
            if (cap is not null) tissue.Add((cap, "fracture surface"));
            if (body.SkeletonName is { } skeleton
                && StockHeadAnatomy.CutInterior(skull, brain, rig, rule[0], palette, skeleton, cancellation) is { } organic)
                tissue.Add((organic, "skull and brain"));
            if (mechanical is not null)
            {
                var interior = MeshCut.Clip(mechanical, rig, rule, buildCaps: false);
                if (interior.HasTriangles) tissue.Add((interior.Skin, "fracture circuitry"));
            }
            var span = piece.Max - piece.Min;
            halves.Add(new(side < 0 ? 20 : 21, piece.Skin, tissue, (piece.Max + piece.Min) * .5f, span * .5f,
                MathF.Max(.08f, span.Length() * .5f), Brain: false));
            // Each half now carries one welded organic interior, independent of its skin material.
        }
        return halves;
    }
}
