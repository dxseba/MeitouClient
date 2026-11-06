using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;
using Meitou.Rendering;

namespace Meitou.Brutality.Rendering;

/// <summary>The stock lit material has no wound-discard shader: retain anatomical triangles only behind an actual opened surface.</summary>
internal static class AnatomyExposure
{
    public static readonly bool Hidden = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_NO_ANATOMY") is { } value
        && (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase));

    public static OgreMesh Filter(OgreMesh anatomy, SkeletonRig rig, IReadOnlyList<CutHalfspace> cuts,
        IReadOnlyList<CutVolume> pockets, float scale = 1)
    {
        float depth = 1.4f * Math.Clamp(scale, .5f, 2);
        var groups = MeshCut.BoneGroups(rig);
        var kept = new List<Vertex>();
        foreach (var part in Model.Build(anatomy, rig.BoneCount, gameBinormals: true).Parts)
            for (int i = 0; i + 2 < part.Indices.Length; i += 3)
            {
                var a = part.Vertices[part.Indices[i]]; var b = part.Vertices[part.Indices[i + 1]]; var c = part.Vertices[part.Indices[i + 2]];
                int ga = MeshCut.Group(a, groups), gb = MeshCut.Group(b, groups), gc = MeshCut.Group(c, groups);
                int group = ga == gb || ga == gc ? ga : gb;
                bool exposed = false;
                foreach (var cut in cuts)
                {
                    float da = cut.Band(a.Position, group), db = cut.Band(b.Position, group), dc = cut.Band(c.Position, group);
                    if (MathF.Min(da, MathF.Min(db, dc)) <= depth && MathF.Max(da, MathF.Max(db, dc)) >= -depth)
                    { exposed = true; break; }
                }
                if (!exposed)
                    foreach (var pocket in pockets)
                        if (Intersects(a.Position, b.Position, c.Position, group, pocket, depth)) { exposed = true; break; }
                if (!exposed) continue;
                kept.Add(a); kept.Add(b); kept.Add(c);
            }
        return MeshCut.FromTriangles(kept, anatomy.SkeletonName);
    }

    static bool Intersects(Vector3 a, Vector3 b, Vector3 c, int group, CutVolume pocket, float depth)
    {
        if (pocket.Group >= 0 && !(pocket.Group == 7 ? group is 6 or 7 : group == pocket.Group)) return false;
        foreach (var plane in pocket.Planes)
        {
            // Keep the wound's lateral footprint, extending only toward the cavity so brain and bone can sit behind its relief.
            var normal = new Vector3(plane.Plane.X, plane.Plane.Y, plane.Plane.Z);
            float back = Vector3.Dot(normal * plane.KeepSide, pocket.FacingNormal) > .9f ? depth : 0;
            if (plane.Band(a, group) < -back && plane.Band(b, group) < -back && plane.Band(c, group) < -back) return false;
        }
        return true;
    }
}
