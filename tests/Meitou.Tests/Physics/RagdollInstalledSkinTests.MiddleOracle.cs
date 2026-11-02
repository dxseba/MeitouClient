using System.Numerics;
using Meitou.Rendering;
using Meitou.Rendering.Characters;
using Meitou.Simulation.Ragdolls;

namespace Meitou.Tests.Physics;

public partial class RagdollInstalledSkinTests
{
    readonly record struct MiddleFace(Vector3 A, Vector3 B, Vector3 C, int Triangle, Vector3 BA, Vector3 BB, Vector3 BC);
    readonly record struct MiddleMaterial(int Triangle, Vector3 Bary);

    // This oracle integrates expanded Model triangles independently of the simulation's skin cache and selector.
    static List<MiddleFace> MiddleFaces(Vertex[] source, Matrix4x4[] palette, Func<Vertex, float>[] fields,
        Func<Vertex[], bool>? allowed = null)
    {
        var result = new List<MiddleFace>();
        for (int triangle = 0; triangle + 2 < source.Length; triangle += 3)
        {
            var vertices = source[triangle..(triangle + 3)];
            if (allowed is not null && !allowed(vertices)) continue;
            var posed = vertices.Select(vertex => Skin(vertex, palette)).ToArray();
            var polygon = new List<Vector3> { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };
            foreach (var field in fields)
            {
                var values = new Vector3(field(vertices[0]), field(vertices[1]), field(vertices[2]));
                var clipped = new List<Vector3>();
                for (int index = 0; index < polygon.Count; index++)
                {
                    var a = polygon[index]; var b = polygon[(index + 1) % polygon.Count];
                    float da = Vector3.Dot(a, values), db = Vector3.Dot(b, values);
                    if (da >= 0) clipped.Add(a);
                    if ((da < 0) != (db < 0)) clipped.Add(Vector3.Lerp(a, b, da / (da - db)));
                }
                polygon = clipped;
            }
            Vector3 At(Vector3 bary) => posed[0] * bary.X + posed[1] * bary.Y + posed[2] * bary.Z;
            for (int index = 1; index + 1 < polygon.Count; index++)
            {
                var face = new MiddleFace(At(polygon[0]), At(polygon[index]), At(polygon[index + 1]), triangle, polygon[0], polygon[index], polygon[index + 1]);
                if (Vector3.Cross(face.B - face.A, face.C - face.A).LengthSquared() > 1e-10f) result.Add(face);
            }
        }
        return result;
    }

    static Vector3 MiddlePoint(Vertex[] source, Matrix4x4[] palette, MiddleMaterial material) =>
        Skin(source[material.Triangle], palette) * material.Bary.X + Skin(source[material.Triangle + 1], palette) * material.Bary.Y
        + Skin(source[material.Triangle + 2], palette) * material.Bary.Z;

    void CheckMiddleOracle(string sample, int side, bool rejectOldHip, Vertex[] source, Matrix4x4[] halfPalette, Matrix4x4[] armPalette,
        List<MiddleFace> halfFaces, List<MiddleFace> armFaces, Vector3 shift, Func<Vertex, float> rootShare,
        MiddleMaterial oldHip, Vector3 shoulder, Vector3 elbow, Vector3 upper, Dictionary<int, List<MiddleMaterial>> witnesses)
    {
        Assert.True(halfFaces.Count > 20 && armFaces.Count > 2, sample + ": retained half and upper-arm skin must both be measurable");
        var axis = elbow - shoulder;
        float lengthSquared = axis.LengthSquared();
        var midpoint = (shoulder + elbow) * 0.5f;
        float Share(MiddleMaterial material) => rootShare(source[material.Triangle]) * material.Bary.X
            + rootShare(source[material.Triangle + 1]) * material.Bary.Y + rootShare(source[material.Triangle + 2]) * material.Bary.Z;
        float upperRadius = armFaces.SelectMany(face => new[] { face.A, face.B, face.C }).Max(point => Vector3.Dot(point - midpoint, upper));
        Assert.True(upperRadius > 0.05f, sample + ": the supporting shaft must expose an upper skin surface");
        if (!witnesses.TryGetValue(side, out var materials))
        {
            // The initial physical step is tiny; undo each root-weighted shift before integrating the pickup geometry.
            var before = halfFaces.Select(face => face with
            {
                A = face.A - shift * Share(new(face.Triangle, face.BA)),
                B = face.B - shift * Share(new(face.Triangle, face.BB)),
                C = face.C - shift * Share(new(face.Triangle, face.BC)),
            }).ToArray();
            double area = 0, x = 0, y = 0, z = 0;
            foreach (var face in before)
            {
                double weight = Vector3.Cross(face.B - face.A, face.C - face.A).Length();
                var centre = (face.A + face.B + face.C) / 3;
                area += weight; x += centre.X * weight; y += centre.Y * weight; z += centre.Z * weight;
            }
            Assert.True(area > 1, sample + ": the retained surface must have substantial area");
            var centroid = new Vector3((float)(x / area), (float)(y / area), (float)(z / area));
            float optimum = before.Min(face => MathF.Sqrt(FoliageLodBuilder.DistanceSquared(centroid, face.A, face.B, face.C)));
            if (rejectOldHip)
            {
                var oldPoint = MiddlePoint(source, halfPalette, oldHip) - shift * Share(oldHip);
                float oldDistance = Vector3.Distance(oldPoint, centroid);
                Assert.True(oldDistance > optimum + 0.05f,
                    sample + $": fixture must reject the original hip grip (hip {oldDistance:F4}, middle {optimum:F4})");
            }
            materials = [];
            float bestContact = float.PositiveInfinity;
            foreach (var face in before)
            {
                var bary = RagdollSkinSurface.ClosestBarycentric(centroid, face.A, face.B, face.C);
                var point = face.A * bary.X + face.B * bary.Y + face.C * bary.Z;
                if (Vector3.Distance(point, centroid) > optimum + 0.02f) continue;
                var material = new MiddleMaterial(face.Triangle, face.BA * bary.X + face.BB * bary.Y + face.BC * bary.Z);
                var actual = MiddlePoint(source, halfPalette, material);
                foreach (var arm in armFaces)
                {
                    var armBary = RagdollSkinSurface.ClosestBarycentric(actual, arm.A, arm.B, arm.C);
                    var armPoint = arm.A * armBary.X + arm.B * armBary.Y + arm.C * armBary.Z;
                    float gap = Vector3.Distance(actual, armPoint);
                    bestContact = MathF.Min(bestContact, gap);
                    if (gap > 0.005f || Vector3.Dot(armPoint - midpoint, upper) < upperRadius * 0.5f) continue;
                    materials.Add(material);
                    break;
                }
            }
            Assert.True(materials.Count > 0, sample + $": area-centred material does not contact upper shaft skin (closest gap {bestContact:F5})");
            witnesses[side] = materials;
            output.WriteLine($"{sample}: retained area {area * 0.5:F2}, centroid {centroid}, nearest material {optimum:F4}");
        }
        // The half's material is fixed at pickup. The arm's upper face is queried in its current pose, including during the lift.
        Vector3? Contact(MiddleMaterial material)
        {
            var half = MiddlePoint(source, halfPalette, material);
            foreach (var face in armFaces)
            {
                var bary = RagdollSkinSurface.ClosestBarycentric(half, face.A, face.B, face.C);
                var arm = face.A * bary.X + face.B * bary.Y + face.C * bary.Z;
                if (Vector3.Distance(half, arm) <= 0.005f && Vector3.Dot(arm - midpoint, upper) >= upperRadius * 0.5f) return arm;
            }
            return null;
        }
        materials.RemoveAll(material => Contact(material) is null);
        Assert.True(materials.Count > 0, sample + ": the original half material lost contact with the current upper-arm face");
        var armContact = Contact(materials[0])!.Value;
        float station = Vector3.Dot(armContact - shoulder, axis) / lengthSquared;
        Assert.InRange(station, 0.399f, 0.601f);
        Assert.True(Vector3.Dot(armContact - midpoint, upper) > 0, sample + ": contact moved below the supporting upper-arm surface");
    }
}
