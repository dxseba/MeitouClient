using System.Numerics;
using Meitou.Data.Characters;

namespace Meitou.Rendering.Characters;

internal sealed partial class CharacterRenderer
{
    public sealed class CarryArmSupport
    {
        internal CharacterAsset Asset = null!;
        internal SurfaceContact? Material;
    }

    public readonly record struct TorsoGripGeometry(Vector3 Centre, Vector3 Contact);

    /// <summary>Real posed upper-arm skin, cached near the middle of the shoulder-to-elbow segment.</summary>
    public Vector3? UpperArmSupportPoint(long key, Vector3 shoulder, Vector3 elbow, Vector3 probe,
        ref CarryArmSupport? cached, Matrix4x4? placement = null)
    {
        if (!states.TryGetValue(key, out var state) || state.Asset is not { BodySource: not null } asset || state.Posed < 0
            || asset.Rig.Handle("Bip01 L UpperArm") is not int upper) return null;
        if (cached is null || !ReferenceEquals(cached.Asset, asset)) cached = new() { Asset = asset };
        if (cached.Material is null)
        {
            var eligible = new bool[asset.Rig.BoneCount]; eligible[upper] = true;
            var faces = new List<ContactFace>();
            _ = ClosestTorsoContact(state, new(new(0, 1, 0, 100000), 1, true), probe, placement: placement,
                limbCuts: ContactLimbCuts(key), collect: faces, eligibleBones: eligible, armWindow: new(shoulder, elbow));
            cached.Material = ClosestFaceContact(faces, probe, probe);
            if (cached.Material is null) return null;
        }
        var point = CarryMaterialPosition(asset, state, cached.Material.Value);
        if (placement is not { } world) return point;
        return Matrix4x4.Invert(state.World, out var inverse) ? Vector3.Transform(point, inverse * world) : null;
    }

    /// <summary>Area centre and nearest stable material on the actual retained root skin, for native diagnostics.</summary>
    public TorsoGripGeometry? TorsoGripCentre(long key, in CharacterTorsoCut cut, Vector3 support, int? component = null)
    {
        if (!states.TryGetValue(key, out var state) || state.Asset is not { } asset || state.Seen != updateStamp || !state.InWorld) return null;
        var material = CentralCarryContact(state, cut, support, ContactLimbCuts(key), component, out var centre);
        return material is { } contact ? new(centre, contact.Position) : null;
    }

    SurfaceContact? CentralCarryContact(CharState state, in CharacterTorsoCut cut, Vector3 support, uint limbCuts,
        int? component, out Vector3 centre)
    {
        centre = default;
        var asset = state.Asset!;
        var faces = new List<ContactFace>();
        _ = ClosestTorsoContact(state, cut, support, component, limbCuts: limbCuts, collect: faces, eligibleBones: CarryPinnedBones(asset.Rig));
        if (component is null) faces = RootCarryIsland(asset, faces);
        if (faces.Count == 0) return null;
        // Relative coordinates keep world-height offsets out of the weighted sum; no axis-aligned bounds are involved.
        var origin = faces[0].A.Position;
        var weighted = Vector3.Zero;
        double total = 0;
        foreach (var face in faces)
        {
            float area = Vector3.Cross(face.B.Position - face.A.Position, face.C.Position - face.A.Position).Length() * 0.5f;
            weighted += ((face.A.Position - origin) + (face.B.Position - origin) + (face.C.Position - origin)) * (area / 3);
            total += area;
        }
        if (total <= 1e-8 || !double.IsFinite(total)) return null;
        centre = origin + weighted / (float)total;
        return ClosestFaceContact(faces, centre, support);
    }

    static SurfaceContact? ClosestFaceContact(List<ContactFace> faces, Vector3 centre, Vector3 support)
    {
        SurfaceContact? selected = null;
        float best = float.PositiveInfinity, arm = float.PositiveInfinity;
        foreach (var face in faces)
        {
            var point = ClosestContactTriangle(centre, face.A.Position, face.B.Position, face.C.Position);
            float gap = Vector3.DistanceSquared(point, centre), toArm = Vector3.DistanceSquared(point, support);
            if (!float.IsFinite(gap) || gap > best + 1e-5f || MathF.Abs(gap - best) <= 1e-5f && toArm >= arm) continue;
            var bary = ContactBarycentric(point, face.A.Position, face.B.Position, face.C.Position);
            selected = new(face.Triangle, face.A.Barycentric * bary.X + face.B.Barycentric * bary.Y + face.C.Barycentric * bary.Z, point);
            best = gap; arm = toArm;
        }
        return selected;
    }

    static bool[] CarryPinnedBones(SkeletonRig rig)
    {
        var result = new bool[rig.BoneCount];
        for (int bone = 0; bone < result.Length; bone++)
        {
            result[bone] = true;
            for (int at = bone; at >= 0; at = rig.Parent[at])
                if (rig.ByHandle[at]?.Name is "Bip01 Head" or "Bip01 Jaw" or "Bip01 L Forearm" or "Bip01 R Forearm")
                { result[bone] = false; break; }
        }
        return result;
    }

    // Without physical component metadata, retained bind-space skin connectivity identifies the torso island.
    // Welding uses the original clipped positions, so a posed wrist touching the trunk cannot join two islands.
    static List<ContactFace> RootCarryIsland(CharacterAsset asset, List<ContactFace> faces)
    {
        if (faces.Count == 0) return faces;
        var parents = Enumerable.Range(0, faces.Count).ToArray();
        var vertices = new Dictionary<(long, long, long), int>();
        int Root(int i) { while (parents[i] != i) { parents[i] = parents[parents[i]]; i = parents[i]; } return i; }
        for (int i = 0; i < faces.Count; i++)
        {
            var face = faces[i];
            foreach (var bary in new[] { face.A.Barycentric, face.B.Barycentric, face.C.Barycentric })
            {
                var source = asset.BodySource!;
                var p = source[face.Triangle].Position * bary.X + source[face.Triangle + 1].Position * bary.Y + source[face.Triangle + 2].Position * bary.Z;
                var key = ((long)MathF.Round(p.X * 10000), (long)MathF.Round(p.Y * 10000), (long)MathF.Round(p.Z * 10000));
                if (vertices.TryGetValue(key, out int other)) parents[Root(i)] = Root(other);
                else vertices[key] = i;
            }
        }
        var scores = new Dictionary<int, (double Axial, double Area)>();
        for (int i = 0; i < faces.Count; i++)
        {
            var face = faces[i];
            float area = Vector3.Cross(face.B.Position - face.A.Position, face.C.Position - face.A.Position).Length() * 0.5f;
            var weights = Vector3.Zero;
            for (int v = 0; v < 3; v++)
            {
                var source = asset.BodySource![face.Triangle + v];
                for (int k = 0; k < 4; k++)
                {
                    int bone = (int)(source.Bones >> (k * 8) & 255);
                    if (bone < asset.Rig.BoneCount && WoundMasks.BodyGroup(asset.Rig.ByHandle[bone]?.Name ?? "") == 0)
                        weights[v] += source.Weights[k];
                }
            }
            float axial = (Vector3.Dot(weights, face.A.Barycentric) + Vector3.Dot(weights, face.B.Barycentric) + Vector3.Dot(weights, face.C.Barycentric)) / 3;
            int root = Root(i); var score = scores.GetValueOrDefault(root);
            scores[root] = (score.Axial + area * axial, score.Area + area);
        }
        int chosen = scores.OrderByDescending(p => p.Value.Axial).ThenByDescending(p => p.Value.Area).ThenBy(p => p.Key).First().Key;
        return faces.Where((_, i) => Root(i) == chosen).ToList();
    }
}
