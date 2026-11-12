using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;

namespace Meitou.Brutality.Rendering;

#if !BRUTALITY_STOCK_MASTER
internal sealed record HeadBurstShard(int Key, RuntimeCharacterGeometry Geometry, Vector3 Centre, Vector3 HalfExtent, float Radius, bool Brain);

#endif

/// <summary>Immutable by ownership. Runtime wrappers may retire without changing the meshes.</summary>
internal sealed record PreparedHeadBurstShard(int Key, OgreMesh Body, IReadOnlyList<(OgreMesh Mesh, string Label)> Parts,
    Vector3 Centre, Vector3 HalfExtent, float Radius, bool Brain);

/// <summary>Actual skin, skull and fitted brain geometry, built on the cut worker.</summary>
internal static class HeadBurstGeometry
{
#if !BRUTALITY_STOCK_MASTER
    internal static IReadOnlyList<HeadBurstShard> Build(CharacterAppearance appearance, SkeletonRig rig, OgreMesh body,
        OgreMesh? anatomy, Vector4 neckPlane, Vector4 flesh, CancellationToken cancellation) =>
        Instantiate(appearance, Prepare(appearance, rig, body, anatomy, neckPlane, flesh, cancellation));

    internal static IReadOnlyList<PreparedHeadBurstShard> Prepare(CharacterAppearance appearance, SkeletonRig rig, OgreMesh body,
        OgreMesh? anatomy, Vector4 neckPlane, Vector4 flesh, CancellationToken cancellation)
    {
        // Separate anatomy layers before clipping.
        var skullVertices = new List<Vertex>();
        var brainVertices = new List<Vertex>();
        if (anatomy is not null)
        {
            int headBone = rig.Handle("Bip01 Head") ?? -1, jawBone = rig.Handle("Bip01 Jaw") ?? -1;
            foreach (var part in Model.Build(anatomy, rig.BoneCount, preserveRawVertexChannels: true).Parts)
                for (int i = 0; i + 2 < part.Indices.Length; i += 3)
                {
                    var vertex = part.Vertices[part.Indices[i]];
                    int bone = (int)(vertex.Bones & 255);
                    var destination = vertex.Tangent.W == 6 ? brainVertices
                        : vertex.Tangent.W == 0 && (bone == headBone || bone == jawBone) ? skullVertices : null;
                    if (destination is null) continue;
                    for (int k = 0; k < 3; k++)
                        destination.Add(part.Vertices[part.Indices[i + k]] with { Tangent = Vector4.Zero });
                }
        }
        var skull = skullVertices.Count == 0 ? null : MeshCut.FromTriangles(skullVertices, body.SkeletonName);
        var brain = brainVertices.Count == 0 ? null : MeshCut.FromTriangles(brainVertices, body.SkeletonName);
        return PrepareShards(appearance, rig, body, skull, brain, neckPlane, flesh, cancellation);
    }
#endif

    internal static IReadOnlyList<PreparedHeadBurstShard> PrepareShards(CharacterAppearance appearance, SkeletonRig rig, OgreMesh body,
        OgreMesh? skull, OgreMesh? brain, Vector4 neckPlane, Vector4 flesh, CancellationToken cancellation)
    {
        var result = new List<PreparedHeadBurstShard>(9);
        bool shaved = appearance.Character?.GetBool("shaved") == true;
        var head = MeshCut.Clip(body, rig, [new(neckPlane, 1, 7, true, MorphedDistance: true)],
            appearance.PoseWeights, shaved, buildCaps: false);
        if (!head.HasTriangles) return [];
        var centre = (head.Min + head.Max) * .5f;
        var half = Vector3.Max((head.Max - head.Min) * .5f, new Vector3(.01f));
        var rise = Through(new(.12f, 1, -.23f), centre + new Vector3(0, half.Y * .03f, 0));
        for (int tier = 0; tier < 2; tier++)
        {
            var across = new Vector3(1, tier == 0 ? -.2f : .2f, tier == 0 ? .43f : -.52f);
            for (int column = 0; column < 3; column++)
            {
                cancellation.ThrowIfCancellationRequested();
                var rules = new List<CutHalfspace>(3) { new(rise, tier == 0 ? -1 : 1, MorphedDistance: true) };
                if (column != 0) rules.Add(new(Through(across, centre + Vector3.UnitX * half.X * (column == 1 ? -.27f : .24f)), MorphedDistance: true));
                if (column != 2) rules.Add(new(Through(across, centre + Vector3.UnitX * half.X * (column == 0 ? -.27f : .24f)), -1, MorphedDistance: true));
                var skin = MeshCut.Clip(head.Skin, rig, rules, appearance.PoseWeights, shaved, capColour: flesh);
                if (!skin.HasTriangles) continue;
                var parts = new List<(OgreMesh Mesh, string Label)>(2);
                if (skin.Wound is { } wound) parts.Add((wound, "fracture surface"));
                if (skull is not null)
                {
                    var piece = MeshCut.Clip(skull, rig, rules, buildCaps: false);
                    if (piece.HasTriangles) parts.Add((piece.Skin, "skull"));
                }
                Add(skin.Skin, parts, skin, 20 + tier * 3 + column, brain: false);
            }
        }
        if (brain is not null)
        {
            var bounds = brain.Bounds!.Value;
            var mid = (bounds.Min + bounds.Max) * .5f;
            float third = (bounds.Max.X - bounds.Min.X) * .17f;
            var axis = new Vector3(1, .22f, -.27f);
            var left = Through(axis, mid - Vector3.UnitX * third);
            var right = Through(axis, mid + Vector3.UnitX * third);
            var noSkin = new OgreMesh { SkeletonName = body.SkeletonName, Bounds = brain.Bounds };
            CutHalfspace[][] lobes = [[new(left, -1)], [new(left), new(right, -1)], [new(right)]];
            for (int i = 0; i < lobes.Length; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                var lobe = MeshCut.Clip(brain, rig, lobes[i], capColour: flesh);
                if (!lobe.HasTriangles) continue;
                var parts = new List<(OgreMesh Mesh, string Label)>(2) { (lobe.Skin, "brain") };
                if (lobe.Wound is { } wound) parts.Add((wound, "brain section"));
                Add(noSkin, parts, lobe, 26 + i, brain: true);
            }
        }
        return result.AsReadOnly();

        void Add(OgreMesh mesh, List<(OgreMesh Mesh, string Label)> parts, CutMeshResult piece, int key, bool brain) =>
            result.Add(new(key, mesh, parts.AsReadOnly(), (piece.Min + piece.Max) * .5f, (piece.Max - piece.Min) * .5f,
                MathF.Max(.08f, Vector3.Distance(piece.Min, piece.Max) * .5f), brain));
    }

#if !BRUTALITY_STOCK_MASTER
    internal static IReadOnlyList<HeadBurstShard> Instantiate(CharacterAppearance appearance, IReadOnlyList<PreparedHeadBurstShard> prepared) =>
        Array.AsReadOnly(prepared.Select(shard => new HeadBurstShard(shard.Key,
            new(shard.Body, shard.Parts.Select(part => Tissue(appearance, part.Mesh, part.Label)),
                bodyDoubleSided: shard.Brain ? null : true),
            shard.Centre, shard.HalfExtent, shard.Radius, shard.Brain)).ToArray());

#endif

    static Vector4 Through(Vector3 normal, Vector3 point)
    {
        normal = Vector3.Normalize(normal);
        return new(normal, -Vector3.Dot(normal, point));
    }

#if !BRUTALITY_STOCK_MASTER
    static RuntimeCharacterPart Tissue(CharacterAppearance appearance, OgreMesh mesh, string label) =>
        new(new CharacterPart
        {
            Record = appearance.Race, Mesh = "brutality-head-burst-" + label, Field = label, Mode = AttachMode.SharedSkeleton,
        }, mesh, VertexColours: true)
        { DoubleSided = true, CastsShadows = false, ShowInPortrait = false };
#endif
}
