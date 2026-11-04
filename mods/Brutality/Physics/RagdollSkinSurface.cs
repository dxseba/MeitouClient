using System.Numerics;
using Meitou.Content;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;
namespace Meitou.Brutality.Physics;

/// <summary>The actual body surface used to distinguish a continuous cut limb from an independent skin island.</summary>
internal sealed partial class RagdollSkinSurface
{
    internal readonly record struct Point(Vector3 Position, uint Bones, Vector4 Weights, uint Vertex = 0, ushort Target = 0, float UvY = 0);
    internal readonly record struct Triangle(Point A, Point B, Point C);
    readonly record struct BridgePoint(Vector3 Position, float[] Votes);
    readonly Triangle[] triangles;

    readonly OgrePose[] morphs;
    RagdollSkinSurface(Triangle[] triangles, OgrePose[] morphs) => (this.triangles, this.morphs) = (triangles, morphs);

    public static RagdollSkinSurface Read(OgreMesh mesh)
    {
        var triangles = new List<Triangle>();
        foreach (var sub in mesh.SubMeshes)
        {
            var data = sub.UseSharedVertices ? mesh.SharedVertexData : sub.VertexData;
            if (data?.ReadPositions() is not { } positions) continue;
            var assignments = sub.UseSharedVertices ? mesh.BoneAssignments : sub.BoneAssignments;
            ushort target = sub.UseSharedVertices ? (ushort)0 : (ushort)(mesh.SubMeshes.IndexOf(sub) + 1);
            var uvElement = data.Find(OgreVertexSemantic.TextureCoordinates);
            var uv = uvElement is null ? null : data.ReadFloats(uvElement);
            int uvStride = uvElement?.Type.ComponentCount() ?? 0;
            var points = positions.Select((position, i) => new Point(position, 0, default, (uint)i, target,
                uv is not null && uvStride > 1 ? uv[i * uvStride + 1] : 0)).ToArray();
            int lowest = assignments.Where(a => a.BoneIndex < 256).Select(a => (int)a.BoneIndex).DefaultIfEmpty(-1).Min();
            foreach (var group in assignments.GroupBy(a => a.VertexIndex))
            {
                if (group.Key >= points.Length) continue;
                var top = group.Where(a => a.BoneIndex < 256 && a.Weight > 0).OrderByDescending(a => a.Weight).Take(4).ToArray();
                float total = top.Sum(a => a.Weight);
                if (total <= 0 || !float.IsFinite(total)) continue;
                uint bones = 0;
                var weights = Vector4.Zero;
                for (int k = 0; k < top.Length; k++) { bones |= (uint)top[k].BoneIndex << (k * 8); weights[k] = top[k].Weight / total; }
                points[group.Key] = points[group.Key] with { Bones = bones, Weights = weights };
            }
            if (lowest >= 0)
                for (int i = 0; i < points.Length; i++)
                    if (points[i].Weights == Vector4.Zero) points[i] = points[i] with { Bones = (uint)lowest, Weights = Vector4.UnitX };
            var indices = sub.Indices.Indices;
            if (sub.Operation == OgreOperationType.TriangleList)
                for (int i = 0; i + 2 < indices.Length; i += 3) Add(indices[i], indices[i + 1], indices[i + 2]);
            else if (sub.Operation == OgreOperationType.TriangleStrip)
                for (int i = 2; i < indices.Length; i++) Add(indices[i - 2], indices[i - 1], indices[i]);
            else if (sub.Operation == OgreOperationType.TriangleFan)
                for (int i = 2; i < indices.Length; i++) Add(indices[0], indices[i - 1], indices[i]);
            void Add(uint a, uint b, uint c)
            {
                if (a < points.Length && b < points.Length && c < points.Length) triangles.Add(new(points[a], points[b], points[c]));
            }
        }
        return new([.. triangles], [.. mesh.Poses]);
    }

    /// <summary>A positive-area surviving triangle with both actor regions proves their material connection.</summary>
    public Func<int, int, bool> Bridges(SkeletonRig rig, RagdollDefinition definition, Vector4 plane, int side)
    {
        var normal = new Vector3(plane.X, plane.Y, plane.Z) * side;
        float offset = plane.W * side;
        var bodyForBone = new int[rig.BoneCount];
        for (int h = 0; h < bodyForBone.Length; h++)
        {
            int at = h;
            while (at >= 0 && definition.BodyOfBone[at] < 0) at = rig.Parent[at];
            bodyForBone[h] = at >= 0 ? definition.BodyOfBone[at] : -1;
        }
        var converted = new Dictionary<Point, BridgePoint>();
        var proved = new HashSet<(int, int)>();
        foreach (var triangle in triangles)
        {
            Point[] original = [triangle.A, triangle.B, triangle.C];
            if (original.Any(point => !Finite(point.Position))) continue;
            var source = original.Select(Convert).ToArray();
            var polygon = new List<BridgePoint>(4);
            for (int k = 0; k < source.Length; k++)
            {
                var a = source[k]; var b = source[(k + 1) % source.Length];
                float da = Distance(a.Position), db = Distance(b.Position);
                if (da >= 0) polygon.Add(a);
                if ((da < 0) != (db < 0))
                {
                    float t = da / (da - db);
                    var votes = new float[definition.Bodies.Length];
                    for (int body = 0; body < votes.Length; body++) votes[body] = a.Votes[body] + (b.Votes[body] - a.Votes[body]) * t;
                    polygon.Add(new(Vector3.Lerp(a.Position, b.Position, t), votes));
                }
            }
            if (polygon.Count < 3) continue;
            var areaVector = Vector3.Zero;
            for (int k = 1; k + 1 < polygon.Count; k++) areaVector += Vector3.Cross(polygon[k].Position - polygon[0].Position, polygon[k + 1].Position - polygon[0].Position);
            if (!Finite(areaVector) || areaVector.LengthSquared() < 1e-8f) continue;
            var dominant = new HashSet<int>();
            foreach (var vertex in polygon)
            {
                int strongest = 0;
                for (int body = 1; body < vertex.Votes.Length; body++)
                    if (vertex.Votes[body] > vertex.Votes[strongest]) strongest = body;
                if (vertex.Votes[strongest] >= 0.25f) dominant.Add(strongest);
            }
            foreach (int a in dominant)
                foreach (int b in dominant)
                    if (a < b) proved.Add((a, b));
        }
        return (a, b) => (uint)a < (uint)definition.BodyOfBone.Length && (uint)b < (uint)definition.BodyOfBone.Length
            && definition.BodyOfBone[a] is int ba and >= 0 && definition.BodyOfBone[b] is int bb and >= 0
            && proved.Contains(ba < bb ? (ba, bb) : (bb, ba));

        float Distance(Vector3 point) => Vector3.Dot(normal, point) + offset;
        BridgePoint Convert(Point point)
        {
            if (converted.TryGetValue(point, out var found)) return found;
            var votes = new float[definition.Bodies.Length];
            for (int k = 0; k < 4; k++)
            {
                int bone = (int)(point.Bones >> (k * 8) & 255);
                if (point.Weights[k] <= 0 || bone >= bodyForBone.Length || bodyForBone[bone] is not (var body and >= 0)) continue;
                votes[body] += point.Weights[k];
            }
            return converted[point] = new(point.Position, votes);
        }
    }

    static bool Finite(Vector3 point) => float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);
}
