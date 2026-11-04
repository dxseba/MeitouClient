using System.Numerics;
using Meitou.Data.Characters;

namespace Meitou.Rendering.Characters;

/// <summary>Closed, locally owned limb sections. Skin, marrow and mechanical backing reuse the torso section machinery.</summary>
internal static class LimbCutSurface
{
    public const int Layer = 35, VertexLimit = 16384;

    public static Vertex[] Build(Vertex[] source, uint[] groups, in LimbCutPlanes planes, uint partOnly = 0,
        Vector4[]? morphs = null, int[]? components = null, uint bloodColour = GorePalette.DefaultBlood, bool mechanical = false,
        SkeletonRig? rig = null)
    {
        var result = new List<Vertex>();
        for (int slot = 0; slot < 4; slot++)
        {
            int group = slot + 1;
            if (!planes.Has(group) || partOnly != 0 && partOnly >> 8 != group) continue;
            var local = new List<Vertex>();
            for (int triangle = 0; triangle + 2 < source.Length; triangle += 3)
            {
                if (TorsoCutSurface.Group(source[triangle], groups) != group
                    || TorsoCutSurface.Group(source[triangle + 1], groups) != group
                    || TorsoCutSurface.Group(source[triangle + 2], groups) != group) continue;
                local.Add(source[triangle]); local.Add(source[triangle + 1]); local.Add(source[triangle + 2]);
            }
            var cut = new CharacterTorsoCut(planes.Plane(slot), partOnly == 0 ? 1 : -1, FullBody: true);
            var cap = TorsoCutSurface.Build(local.ToArray(), groups, cut, morphs, components, bloodColour, mechanical: mechanical);
            if (cap is null || result.Count + cap.Length > VertexLimit) continue;
            int anchor = rig is null ? -1 : SectionBone(rig, slot, planes.Plane(slot));
            foreach (var vertex in cap)
            {
                // A section belongs to one cut actor, even when several moving joints share a connected-component id.
                var fixedVertex = anchor >= 0 ? AnatomyTriangles.FollowBone(vertex, anchor) : vertex;
                result.Add(fixedVertex with { Tangent = new(vertex.Tangent.X, vertex.Tangent.Y, vertex.Tangent.Z, Layer) });
            }
        }
        return result.ToArray();
    }

    static int SectionBone(SkeletonRig rig, int slot, Vector4 plane)
    {
        string side = slot % 2 == 0 ? "L" : "R";
        string proximal = slot < 2 ? "UpperArm" : "Thigh", middle = slot < 2 ? "Forearm" : "Calf";
        if (rig.Handle($"Bip01 {side} {proximal}") is not int upper) return -1;
        if (rig.Handle($"Bip01 {side} {middle}") is not int lower) return upper;
        return Vector4.Dot(plane, new(rig.BindPosition[lower], 1)) >= 0 ? lower : upper;
    }
}
