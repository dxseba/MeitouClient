using Meitou.Game.Rendering.Frame;
using System.Numerics;
using Meitou.Data.Characters;

namespace Meitou.Brutality.Rendering;

/// <summary>Original mechanical assemblies fitted inside a robot's own skinned body, rigid per physical bone.</summary>
static class MechanicalAnatomy
{
    public const int Layer = 34, VertexBudget = 24000;
    static readonly Vector4 Steel = new(0.28f, 0.31f, 0.32f, 0);
    static readonly Vector4 Board = new(0.035f, 0.19f, 0.13f, 0);
    static readonly Vector4 Copper = new(0.64f, 0.40f, 0.14f, 0);
    static readonly Vector4 Chip = new(0.055f, 0.065f, 0.075f, 0);

    public static List<Vertex>? Build(SkeletonRig rig, Vertex[] bodySource, Vertex[] headSource,
        bool headless = false, bool headOnly = false)
    {
        var made = new MeshBuilder();
        var source = bodySource.Length > 0 ? bodySource : headSource;
        foreach (var bone in rig.Skeleton.Bones.OrderBy(b => Priority(b.Name)))
        {
            if (!Supported(bone.Name) || (headless && Head(bone.Name)) || (headOnly && !Head(bone.Name))) continue;
            var points = source.Where(v => Dominant(v, bone.Handle)).Select(v => v.Position).Where(Finite).ToArray();
            if (points.Length < 3) continue;
            var min = points.Aggregate(new Vector3(float.MaxValue), Vector3.Min);
            var max = points.Aggregate(new Vector3(float.MinValue), Vector3.Max);
            var half = (max - min) * 0.34f;
            if (MathF.Min(half.X, MathF.Min(half.Y, half.Z)) < 0.025f) continue;
            made.Module((min + max) * 0.5f, half, bone.Handle, 0);
        }
        return made.Vertices.Count == 0 ? null : made.Vertices;
    }

    static bool Head(string name) => name.Contains("Head") || name.Contains("Jaw");
    static int Priority(string name) => Head(name) ? 0 : name.Contains("Spine") || name.Contains("Pelvis") ? 1 : 2;
    static bool Supported(string name) => name == "Bip01 Head" || name == "Bip01 Pelvis" || name.Contains("Spine")
        || name.Contains("UpperArm") || name.Contains("Forearm") || name.Contains("Thigh") || name.Contains("Calf")
        || name.Contains("Hand") || name.Contains("Foot");

    static bool Dominant(in Vertex vertex, int bone)
    {
        float weight = 0;
        for (int k = 0; k < 4; k++)
            if ((vertex.Bones >> (8 * k) & 255) == bone) weight += vertex.Weights[k];
        return weight >= 0.55f;
    }

    static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);

    sealed class MeshBuilder
    {
        public readonly List<Vertex> Vertices = [];
        uint bone;

        public void Module(Vector3 centre, Vector3 half, int owner, int bodyGroup)
        {
            if (Vertices.Count + 1500 > VertexBudget) return;
            bone = (uint)owner;
            Box(centre, half * new Vector3(0.22f, 0.96f, 0.24f), Steel);
            BoardAt(centre, half, false);
            BoardAt(centre, half, true);
            float radius = MathF.Min(half.X, MathF.Min(half.Y, half.Z)) * 0.13f;
            for (int k = 0; k < 3; k++)
            {
                var shift = new Vector3((k - 1) * half.X * 0.48f, 0, -half.Z * 0.52f);
                var colour = k == 0 ? new Vector4(0.30f, 0.065f, 0.045f, 0)
                    : k == 1 ? new Vector4(0.055f, 0.075f, 0.095f, 0) : Copper;
                Tube(centre + shift - Vector3.UnitY * half.Y * 0.86f,
                    centre + shift + Vector3.UnitY * half.Y * 0.86f, radius, colour);
            }
        }

        void BoardAt(Vector3 centre, Vector3 half, bool side)
        {
            var size = half * new Vector3(0.72f, 0.78f, 0.075f);
            var place = centre + new Vector3(0, 0, half.Z * 0.32f);
            if (side)
            {
                size = half * new Vector3(0.075f, 0.66f, 0.68f);
                place = centre + new Vector3(half.X * 0.34f, 0, 0);
            }
            Box(place, size, Board);
            for (int k = -1; k <= 1; k++)
            {
                var p = place + new Vector3(k * size.X * 0.48f, k * size.Y * 0.36f, size.Z * 1.15f);
                var chipSize = size * new Vector3(0.22f, 0.16f, 0.7f);
                if (side)
                {
                    p = place + new Vector3(size.X * 1.15f, k * size.Y * 0.36f, k * size.Z * 0.48f);
                    chipSize = size * new Vector3(0.7f, 0.16f, 0.22f);
                }
                Box(p, chipSize, Chip);
            }
            for (int k = -2; k <= 2; k++)
            {
                var offset = side ? new Vector3(size.X * 1.08f, k * size.Y * 0.29f, 0)
                    : new Vector3(0, k * size.Y * 0.29f, size.Z * 1.08f);
                var trace = side ? new Vector3(size.X * 0.15f, size.Y * 0.018f, size.Z * 0.85f)
                    : new Vector3(size.X * 0.85f, size.Y * 0.018f, size.Z * 0.15f);
                Box(place + offset, trace, Copper);
            }
        }

        void Box(Vector3 centre, Vector3 half, Vector4 colour)
        {
            if (Vertices.Count + 36 > VertexBudget) return;
            for (int axis = 0; axis < 3; axis++)
                foreach (int sign in new[] { -1, 1 })
                {
                    int x = (axis + 1) % 3, y = (axis + 2) % 3;
                    var normal = Vector3.Zero;
                    normal[axis] = sign;
                    Vector3 Point(float a, float b)
                    {
                        var p = centre + normal * half[axis];
                        p[x] += a * half[x];
                        p[y] += b * half[y];
                        return p;
                    }
                    Quad(Point(-1, -1), Point(1, -1), Point(1, 1), Point(-1, 1), normal, colour);
                }
        }

        void Tube(Vector3 a, Vector3 b, float radius, Vector4 colour)
        {
            const int segments = 8;
            if (Vertices.Count + segments * 12 > VertexBudget || radius < 0.002f) return;
            var axis = Vector3.Normalize(b - a);
            var u = Vector3.Normalize(Vector3.Cross(axis, MathF.Abs(axis.X) < 0.8f ? Vector3.UnitX : Vector3.UnitZ));
            var v = Vector3.Cross(axis, u);
            for (int i = 0; i < segments; i++)
            {
                var p = u * MathF.Cos(MathF.Tau * i / segments) + v * MathF.Sin(MathF.Tau * i / segments);
                var q = u * MathF.Cos(MathF.Tau * (i + 1) / segments) + v * MathF.Sin(MathF.Tau * (i + 1) / segments);
                Quad(a + p * radius, b + p * radius, b + q * radius, a + q * radius, Vector3.Normalize(p + q), colour);
                Triangle(a, a + q * radius, a + p * radius, -axis, colour);
                Triangle(b, b + p * radius, b + q * radius, axis, colour);
            }
        }

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Vector4 colour)
        {
            Triangle(a, b, c, normal, colour);
            Triangle(a, c, d, normal, colour);
        }

        void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Vector4 colour)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0) (b, c) = (c, b);
            foreach (var p in new[] { a, b, c })
                Vertices.Add(new Vertex { Position = p, Normal = normal, Tangent = new(Vector3.Normalize(Vector3.Cross(MathF.Abs(normal.Y) < .9f ? Vector3.UnitY : Vector3.UnitX, normal)), 1),
                    Colour = colour with { W = 1 }, Bones = bone, Weights = Vector4.UnitX });
        }
    }
}
