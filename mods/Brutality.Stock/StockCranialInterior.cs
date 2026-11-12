using Meitou.Game.Rendering.Frame;
using System.Numerics;
using Meitou.Brutality.Rendering;
using Meitou.Brutality.Rendering.Surface;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;

namespace Meitou.Brutality;

internal static partial class StockHeadAnatomy
{
    internal static OgreMesh? SkinBacking(OgreMesh head, SkeletonRig rig, CharacterAppearance look,
        Vector4 plane, int side, Vector4 flesh)
    {
        var source = new List<Vertex>();
        bool shaved = look.Character?.GetBool("shaved") == true;
        foreach (var part in MeshCut.SourceModel(head, rig.BoneCount).Parts)
        {
            var morph = MeshCut.MorphDeltas(head, part.SubMeshIndex, part.Vertices.Length, look.PoseWeights, shaved);
            foreach (uint index in part.Indices)
                if (index < part.Vertices.Length)
                {
                    var vertex = part.Vertices[index];
                    source.Add(vertex with { Position = vertex.Position + morph[index] });
                }
        }
        var skin = source.ToArray();
        var palette = StockTissuePalette.For(look);
        bool mechanical = look.Race.GetBool("is robot");
        var lip = mechanical ? new Vector4(.35f, .38f, .39f, 1) : palette.Wet;
        var inside = mechanical ? new Vector4(.19f, .22f, .23f, 1) : palette.Muscle;
        if (rig.Handle("Bip01 Head") is int bone && bone <= byte.MaxValue
            && CranialBacking(skin, plane, side, bone, lip, flesh, inside, mechanical ? .22f : .38f) is { Length: > 0 } lining)
        {
            var section = MeshCut.FromTriangles(lining, head.SkeletonName);
            OgreMeshWeld.Apply(section);
            return section;
        }
        var cut = new NativeSectionCut(plane, side, FullBody: true);
        var cap = NativeSectionGeometry.Build(skin, [], cut, weldDistance: .002f)
            ?? NativeSectionGeometry.Build(skin, [], cut);
        if (cap is null) return null;
        for (int i = 0; i < cap.Length; i++)
        {
            var vertex = cap[i];
            var shade = .36f + .64f * Math.Clamp(vertex.Colour.X / .43f, 0, 1);
            cap[i] = vertex with { Colour = (flesh * shade) with { W = 1 }, Tangent = Vector4.UnitW };
        }
        var backing = MeshCut.FromTriangles(cap, head.SkeletonName);
        OgreMeshWeld.Apply(backing);
        return backing;
    }

    internal static IEnumerable<(OgreMesh Mesh, string Label)> BodySection(string folder, OgreMesh body,
        SkeletonRig rig, CharacterAppearance look, CutHalfspace section, CancellationToken cancellation)
    {
        var (min, max) = AnatomyGeometry.Bounds(HeadReliefGeometry.HeadPoints(body, rig, look));
        var span = max - min;
        if (look.Headless || body.SkeletonName is not { } skeleton || !UsableSize(span)) yield break;
        var normal = new Vector3(section.Plane.X, section.Plane.Y, section.Plane.Z);
        float distance = Vector3.Dot(normal, (min + max) * .5f) + section.Plane.W;
        if (MathF.Abs(distance) >= Vector3.Dot(Vector3.Abs(normal), span * .5f)) yield break;
        var palette = StockTissuePalette.For(look);
        var (skull, brain) = Load(folder, rig, body, look, skeleton, min, max, cancellation);
        var head = MeshCut.Clip(body, rig, [new(new(0, 1, 0, 1_000_000), 1, 7, true)],
            look.PoseWeights, look.Character?.GetBool("shaved") == true, buildCaps: false);
        var lining = head.HasTriangles ? SkinBacking(head.Skin, rig, look, section.Plane, section.KeepSide, palette.Flesh) : null;
        var anatomy = CutInterior(skull, brain, rig, section, palette, skeleton, cancellation);
        if (lining is null && anatomy is null) yield break;
        var joined = lining is null ? anatomy! : anatomy is null ? lining
            : MeshCut.FromTriangles(Triangles(lining, rig).Concat(Triangles(anatomy, rig)).ToArray(), skeleton);
        if (lining is not null && anatomy is not null) OgreMeshWeld.Apply(joined);
        yield return (joined, "skull and brain");
    }

    internal static OgreMesh? CutInterior(OgreMesh? skull, OgreMesh? brain, SkeletonRig rig, CutHalfspace cut,
        StockTissuePalette palette, string skeleton, CancellationToken cancellation)
    {
        var tissue = new List<Vertex>();
        var normal = new Vector3(cut.Plane.X, cut.Plane.Y, cut.Plane.Z);
        float length = normal.Length();
        if (!float.IsFinite(length) || length < 1e-5f) return null;
        var depthLimit = new CutHalfspace(-cut.Plane / length * cut.KeepSide + new Vector4(0, 0, 0, 1.2f));
        CutHalfspace[] band = [cut, depthLimit];
        var section = new NativeSectionCut(cut.Plane, cut.KeepSide, FullBody: true);
        Add(skull, isBrain: false);
        Add(brain, isBrain: true);
        if (tissue.Count == 0) return null;
        var geometry = MeshCut.FromTriangles(tissue, skeleton);
        OgreMeshWeld.Apply(geometry);
        return geometry;

        void Add(OgreMesh? source, bool isBrain)
        {
            if (source is null) return;
            cancellation.ThrowIfCancellationRequested();
            var skin = MeshCut.Clip(source, rig, band, buildCaps: false);
            if (!skin.HasTriangles) return;
            foreach (var vertex in Triangles(skin.Skin, rig)) tissue.Add(vertex);
            var full = Triangles(source, rig).ToArray();
            var cap = NativeSectionGeometry.Build(full, [], section, brainSection: isBrain,
                weldDistance: .002f, corticalColour: isBrain ? null : palette.Bone);
            if (isBrain && (cap is null || cap.Length < 150)
                && rig.Handle("Bip01 Head") is int bone && bone <= byte.MaxValue)
            {
                var light = Vector4.Lerp(palette.Brain, palette.Fat, .22f);
                foreach (var vertex in CranialBacking(full, cut.Plane, cut.KeepSide, bone,
                    Shade(palette.Brain, .68f), palette.Brain, light, .11f)) tissue.Add(vertex);
                return;
            }
            if (cap is not null)
                foreach (var vertex in cap)
                    tissue.Add(vertex with { Colour = isBrain ? palette.Tint(vertex.Colour with { W = 1 }, .43f) : vertex.Colour,
                        Tangent = Vector4.UnitW });
        }
    }

    static IEnumerable<Vertex> Triangles(OgreMesh mesh, SkeletonRig rig)
    {
        foreach (var part in MeshCut.SourceModel(mesh, rig.BoneCount).Parts)
            foreach (uint index in part.Indices)
                if (index < part.Vertices.Length) yield return part.Vertices[index];
    }
}
