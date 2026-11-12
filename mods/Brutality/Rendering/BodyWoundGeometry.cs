using Meitou.Game.Rendering.Frame;
using System.Numerics;
using Meitou.Brutality.Physics;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;

namespace Meitou.Brutality.Rendering;

internal sealed record BodyWoundPlan(IReadOnlyList<CutVolume> Skin, IReadOnlyList<CutVolume> Clothing, OgreMesh? Relief, IReadOnlyList<Vertex> AbdomenAnchor);

/// <summary>Projects the pinned wound marks into shallow outward-facing pockets; the opposite body surface is never opened.</summary>
internal static class BodyWoundGeometry
{
    const int MaximumMarks = 64, PocketSections = 6, ReliefSections = 18, SlashSections = 12;
    readonly record struct Triangle(Vertex A, Vertex B, Vertex C, int Group);
    readonly record struct Mark(string A, string B, float T, Vector3 Side, WoundShape Shape, float Size, int Age,
        bool Abdomen = false, int Segment = -1);
    readonly record struct Frame(Vector3 Centre, Vector3 Normal, Vector3 Along, Vector3 Across, float HalfLength, float Chart, float Depth,
        int Group, float Reach);

    public static BodyWoundPlan Build(OgreMesh body, SkeletonRig rig, CharacterAppearance look, BodyWoundState state, bool anchorsOnly = false)
    {
        if (!state.HasAny || anchorsOnly && state.BellyHits < 10) return new([], [], null, []);
        var triangles = new List<Triangle>(); var groups = MeshCut.BoneGroups(rig);
        foreach (var part in MeshCut.SourceModel(body, rig.BoneCount).Parts)
        {
            var morph = MeshCut.MorphDeltas(body, part.SubMeshIndex, part.Vertices.Length, look.PoseWeights, look.Character?.GetBool("shaved") == true);
            for (int i = 0; i + 2 < part.Indices.Length; i += 3)
            {
                Vertex At(uint index) => part.Vertices[index] with { Position = part.Vertices[index].Position + morph[index] };
                var a = At(part.Indices[i]); var b = At(part.Indices[i + 1]); var c = At(part.Indices[i + 2]);
                triangles.Add(new(a, b, c, MeshCut.TriangleGroup(a, b, c, groups)));
            }
        }
        var skin = new List<CutVolume>(); var cloth = new List<CutVolume>(); var relief = new List<Vertex>();
        IReadOnlyList<Vertex> abdomenAnchor = [];
        uint blood = unchecked((uint)look.Race.GetInt("blood colour", 0x800000)) & 0xFFFFFF;
        bool robot = look.Race.GetBool("is robot");
        foreach (var mark in Marks(state).Take(MaximumMarks))
        {
            if (anchorsOnly && !mark.Abdomen) continue;
            if (rig.Handle(mark.A) is not int a || rig.Handle(mark.B) is not int b) continue;
            bool hand = WoundSegments.IsHand(mark.Segment);
            bool distal = WoundSegments.IsDistalHand(mark.Segment);
            bool face = mark.Segment is WoundSegments.Cheek or WoundSegments.Jaw;
            int group = mark.B.Contains("Head", StringComparison.Ordinal) ? 7 : Group(mark.A);
            var axis = rig.BindPosition[b] - rig.BindPosition[a];
            float fit = hand ? Math.Clamp(axis.Length() / 3.25f, .55f, 1.35f)
                : face ? Math.Clamp(axis.Length() / 1.4f, .55f, 1.35f) : 1;
            var normal = Unit(mark.Side);
            float traceOut = distal ? .34f : .48f;
            var target = Vector3.Lerp(rig.BindPosition[a], rig.BindPosition[b], mark.T) + normal * (hand ? traceOut * fit : 1.2f);
            var candidates = triangles.Where(t => group == 7 ? t.Group is 6 or 7 : t.Group == group).ToArray();
            if (hand) candidates = HandTriangles(candidates, rig, b);
            float nearest = hand ? .72f * fit : face ? 1.15f * fit : float.PositiveInfinity;
            if (Closest(candidates, target, normal, nearest) is not { } centre) continue;
            if (distal && Vector3.Dot(centre.Position - rig.BindPosition[b], Unit(axis)) < .29f * fit) continue;
            normal = Unit(centre.Normal);
            var vertical = Unit(axis - normal * Vector3.Dot(axis, normal));
            var across = Unit(Vector3.Cross(vertical, normal));
            var along = Unit(across * mark.Shape.Cosine + vertical * mark.Shape.Sine);
            var side = Unit(Vector3.Cross(normal, along));
            float thin = look.Race.Name.Contains("Hive", StringComparison.OrdinalIgnoreCase) || look.Race.Name.Contains("Drone", StringComparison.OrdinalIgnoreCase) ? .55f : 1;
            float size = mark.Size * thin;
            float chart = (.3f + .4f * mark.Shape.Depth) * size * (group == 7 ? .5f : hand ? .58f : 1);
            float footprint = MathF.Max(MathF.Pow(Math.Clamp(mark.Shape.Depth, 0, 1), .4f), .45f) * 1.5f;
            float halfLength = 1.25f * footprint * chart;
            float depth = Math.Clamp((.05f + mark.Shape.Depth * .35f) * thin, .025f, .45f);
            float reach = 1;
            if (hand || face)
            {
                float longest = (distal ? .17f : hand ? .38f : .46f) * fit;
                if (halfLength > longest)
                {
                    chart *= longest / halfLength;
                    halfLength = longest;
                }
                depth = Math.Min(depth, (distal ? .06f : hand ? .12f : .18f) * fit);
                reach = (distal ? .23f : hand ? .43f : .62f) * fit;
            }
            var frame = new Frame(centre.Position, normal, along, side, halfLength, chart, depth, group, reach);
            var local = candidates.Where(t => Near(t, centre.Position, halfLength * 2 + 1)).ToArray();
            if (state.BellyHits >= 10 && mark.Abdomen && mark.Shape.Kind != WoundKind.Crush && abdomenAnchor.Count == 0)
            {
                var right = Project(local, centre.Position + along * .1f, normal, 1) ?? centre;
                var above = Project(local, centre.Position + side * .1f, normal, 1) ?? centre;
                abdomenAnchor = Array.AsReadOnly(new[] { centre, right, above });
                if (anchorsOnly) return new([], [], null, abdomenAnchor);
            }
            if (!robot && mark.Shape.Kind == WoundKind.Slash)
            {
                float start = -halfLength * .91f;
                float end = halfLength * .91f;
                // A long cut can approach the silhouette on one side. Shorten that end first.
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    int missed = AddSlash(local, frame, mark.Shape, mark.Age, blood, start, end, skin, cloth, relief);
                    if (missed < 0) break;
                    if (missed <= SlashSections / 2) start *= .68f;
                    if (missed >= SlashSections / 2) end *= .68f;
                }
                continue;
            }
            if (mark.Shape.Kind != WoundKind.Crush)
                for (int section = 0; section < PocketSections; section++)
                {
                    float u = halfLength * (2f * section / PocketSections - 1), v = halfLength * (2f * (section + 1) / PocketSections - 1);
                    var l = Width(u, frame, mark.Shape); var r = Width(v, frame, mark.Shape);
                    var polygon = new[] { centre.Position + along * u + side * l.Left, centre.Position + along * v + side * r.Left,
                        centre.Position + along * v + side * r.Right, centre.Position + along * u + side * l.Right };
                    skin.Add(Pocket(polygon, frame, depth, depth + .08f));
                    cloth.Add(Pocket(polygon, frame, depth, MathF.Max(1, size * 1.5f)));
                }
            Patch(local, frame, mark.Shape, robot, blood, relief);
        }
        return new(skin.AsReadOnly(), cloth.AsReadOnly(), relief.Count == 0 ? null : MeshCut.FromTriangles(relief, body.SkeletonName), abdomenAnchor);
    }

    /// <summary>Builds a tapered incision and its lining from the same footprint on the skin.</summary>
    static int AddSlash(Triangle[] source, Frame frame, WoundShape shape, int age, uint blood,
        float start, float end, List<CutVolume> skin, List<CutVolume> cloth, List<Vertex> relief)
    {
        float length = (end - start) * .5f;
        float width = length * (.041f + shape.Depth * .11f) * Math.Clamp(shape.Aspect / .26f, .65f, 1.25f);
        float depth = frame.Depth * (.31f + shape.Depth * .34f);
        float handedness = age % 2 == 0 ? 1 : -1;
        float bend = width * .37f * handedness;
        float reach = MathF.Max(frame.Reach, length);
        var polygon = new Vector3[SlashSections * 2];
        ReadOnlySpan<float> bands = [-1f, -.77f, -.34f, 0, .38f, .81f, 1f];
        var lining = new Vertex[SlashSections + 1, bands.Length];
        var lip = AnatomyGeometry.Recolour(new(.24f, .064f, .045f, 1), blood);
        var tissue = AnatomyGeometry.Recolour(new(.35f, .095f, .075f, 1), blood);
        var bed = AnatomyGeometry.Recolour(new(.13f, .026f, .028f, 1), blood);
        float near = 0, far = 0;
        for (int row = 0; row <= SlashSections; row++)
        {
            float angle = MathF.PI * row / SlashSections;
            float x = -MathF.Cos(angle);
            float arc = row is 0 or SlashSections ? 0 : MathF.Sin(angle);
            float taper = arc * (.30f + .70f * arc);
            float along = x * length + (start + end) * .5f;
            var middle = frame.Centre + frame.Along * along + frame.Across * (arc * arc * bend * (1 + .12f * x));
            float left = width * taper * (.84f - .14f * x * handedness);
            float right = width * taper * (1.16f + .19f * x * handedness);
            polygon[row] = middle - frame.Across * left;
            if (row is > 0 and < SlashSections)
                polygon[polygon.Length - row] = middle + frame.Across * right;

            for (int band = 0; band < bands.Length; band++)
            {
                float v = bands[band];
                bool edge = band == 0 || band == bands.Length - 1;
                // A narrow overlap hides numerical seams without producing loose strips outside the rim.
                float across = v * (v < 0 ? left : right) * (edge ? 1.025f : 1);
                var target = middle + frame.Across * across;
                if (Project(source, target, frame.Normal, reach) is not { } point) return row;

                float offset = Vector3.Dot(point.Position - frame.Centre, frame.Normal);
                near = MathF.Max(near, offset);
                far = MathF.Max(far, -offset);
                float side = MathF.Abs(v);
                float inset = depth * taper * MathF.Pow(1 - side, .84f);
                float grain = .91f + .09f * MathF.Sin(row * 2.81f + band * 1.7f + age * .73f);
                var colour = side > .80f ? lip : Vector4.Lerp(bed, tissue, MathF.Sqrt(side));
                lining[row, band] = point with
                {
                    Position = point.Position + frame.Normal * (edge ? .0012f * taper : -inset),
                    Normal = edge ? point.Normal : Unit(point.Normal - frame.Across * (v * taper * .47f)),
                    Colour = (colour * grain) with { W = 1 },
                };
            }
        }

        var incision = frame with { HalfLength = MathF.Max(-start, end) };
        skin.Add(Pocket(polygon, incision, far + .015f, near + .015f));
        cloth.Add(Pocket(polygon, incision, far + .015f, MathF.Max(near + .015f, length * 1.5f)));
        for (int row = 0; row < SlashSections; row++)
        for (int band = 0; band + 1 < bands.Length; band++)
        {
            AddTriangle(lining[row, band], lining[row + 1, band], lining[row, band + 1]);
            AddTriangle(lining[row + 1, band], lining[row + 1, band + 1], lining[row, band + 1]);
        }
        return -1;

        void AddTriangle(Vertex a, Vertex b, Vertex c)
        {
            var cross = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);
            if (cross.LengthSquared() < 1e-12f) return;
            if (Vector3.Dot(cross, frame.Normal) < 0) (b, c) = (c, b);
            relief.Add(a);
            relief.Add(b);
            relief.Add(c);
        }
    }

    static IEnumerable<Mark> Marks(BodyWoundState state)
    {
        foreach (var entry in state.Entries.Where(entry => entry.Region >= 0))
        {
            var w = entry.Mark; float off = w.Age * 37 % 7 / 3f - 1;
            yield return new("Bip01 Pelvis", "Bip01 Neck", (entry.Region == 0 ? .7f : entry.Region == 1 ? .35f : .5f) + off * .13f,
                Vector3.Normalize(new(off * .95f, 0, entry.Region == 2 ? -1 : 1)), Turn(w), w.Size, w.Age, entry.Region == 1);
        }
        foreach (var entry in state.Entries.Where(entry => entry.Region < 0).OrderByDescending(entry => entry.Mark.Age))
        {
            var w = entry.Mark; var shape = Turn(w);
            if (w.Segment == 9) { yield return new("Bip01 Pelvis", "Bip01 Neck", w.T, w.Side, shape, w.Size, w.Age, w.T <= .54f); continue; }
            if (w.Segment == 8)
            {
                float lateral = (w.Born == 0 ? w.Age : w.Born) % 2 == 0 ? .9f : -.9f;
                yield return new("Bip01 Neck", "Bip01 Head", 2.2f + w.T, new(lateral, .5f, .2f * w.Side.Z), shape, w.Size, w.Age);
                continue;
            }
            if (ExtraSite(w) is { } site)
            {
                yield return new(site.From, site.To, site.Along, site.Side, shape, w.Size, w.Age, Segment: w.Segment);
                continue;
            }
            string side = w.Segment < 2 || w.Segment is 4 or 5 ? "L" : "R";
            bool lower = (w.Segment & 1) != 0;
            var (a, b) = w.Segment < 4 ? lower ? ("Forearm", "Hand") : ("UpperArm", "Forearm") : lower ? ("Calf", "Foot") : ("Thigh", "Calf");
            yield return new($"Bip01 {side} {a}", $"Bip01 {side} {b}", w.T, w.Side, shape, w.Size, w.Age);
        }
    }
    /// <summary>Returns the local anatomy anchor shared by the skin and an overlying garment.</summary>
    internal static (string From, string To, float Along, Vector3 Side, int Group)? ExtraSite(in Wound wound)
    {
        if (WoundSegments.IsHand(wound.Segment))
        {
            bool left = wound.Segment is WoundSegments.LeftHand or WoundSegments.LeftDistalHand;
            float along = WoundSegments.IsDistalHand(wound.Segment) ? 1.38f + .14f * wound.T : 1.08f + .18f * wound.T;
            return (left ? "Bip01 L Forearm" : "Bip01 R Forearm", left ? "Bip01 L Hand" : "Bip01 R Hand",
                along, wound.Side, left ? 1 : 2);
        }
        if (wound.Segment is not (WoundSegments.Cheek or WoundSegments.Jaw)) return null;

        long seed = wound.Born == 0 ? wound.Age : wound.Born;
        float side = MathF.Abs(wound.Side.X) > .12f ? MathF.Sign(wound.Side.X) : seed % 2 == 0 ? 1 : -1;
        float lateral = (.60f + MathF.Abs(wound.Side.X) * .24f) * side;
        float front = wound.Side.Z < -.25f ? -.85f : .85f;
        bool jaw = wound.Segment == WoundSegments.Jaw;
        return ("Bip01 Neck", "Bip01 Head", jaw ? .96f + .22f * wound.T : 1.24f + .30f * wound.T,
            Unit(new(lateral, jaw ? -.12f : 0, front)), 7);
    }

    /// <summary>Restricts palm projection to triangles whose skin follows the hand joint.</summary>
    static Triangle[] HandTriangles(Triangle[] triangles, SkeletonRig rig, int hand)
    {
        var bones = new bool[rig.BoneCount];
        for (int bone = 0; bone < bones.Length; bone++)
        for (int parent = bone; parent >= 0 && parent < bones.Length; parent = rig.Parent[parent])
        {
            if (parent != hand) continue;
            bones[bone] = true;
            break;
        }
        return triangles.Where(triangle => Weight(triangle.A) + Weight(triangle.B) + Weight(triangle.C) >= 1.5f).ToArray();

        float Weight(in Vertex vertex)
        {
            float total = 0;
            for (int k = 0; k < 4; k++)
            {
                int bone = (int)(vertex.Bones >> (k * 8) & 255);
                if (bone < bones.Length && bones[bone]) total += vertex.Weights[k];
            }
            return total;
        }
    }

    static WoundShape Turn(in Wound mark)
    {
        var shape = mark.Shape;
        float turn = WoundPolicy.VisualTurn(mark), c = MathF.Cos(turn), s = MathF.Sin(turn);
        return shape with { Cosine = shape.Cosine * c - shape.Sine * s, Sine = shape.Cosine * s + shape.Sine * c };
    }
    static (float Left, float Right) Width(float u, Frame frame, WoundShape shape)
    {
        float width = MathF.Max(.001f, shape.Aspect * frame.Chart * 3);
        float Edge(int side)
        {
            float low = 0, high = width;
            for (int i = 0; i < 9; i++)
            {
                float mid = (low + high) * .5f;
                var profile = WoundPolicy.Sample(u / frame.Chart, side * mid / frame.Chart, shape with { Cosine = 1, Sine = 0 });
                if ((shape.Kind == WoundKind.Crush ? profile.Ring : profile.Area) > .18f) low = mid; else high = mid;
            }
            return MathF.Max(.001f, low);
        }
        return (-Edge(-1), Edge(1));
    }
    static CutVolume Pocket(Vector3[] polygon, Frame frame, float back, float front)
    {
        var planes = new List<CutHalfspace>();
        for (int i = 0; i < polygon.Length; i++)
        {
            var normal = Unit(Vector3.Cross(frame.Normal, polygon[(i + 1) % polygon.Length] - polygon[i]));
            planes.Add(new(new(normal, -Vector3.Dot(normal, polygon[i])), MorphedDistance: true));
        }
        planes.Add(new(new(frame.Normal, -Vector3.Dot(frame.Normal, frame.Centre - frame.Normal * back)), MorphedDistance: true));
        planes.Add(new(new(-frame.Normal, Vector3.Dot(frame.Normal, frame.Centre + frame.Normal * front)), MorphedDistance: true));
        float radius = frame.HalfLength + front + back + 1;
        return new(planes, frame.Normal, Group: frame.Group, Bounds: new Vector4(frame.Centre, radius));
    }
    static void Patch(Triangle[] source, Frame frame, WoundShape shape, bool robot, uint blood, List<Vertex> output)
    {
        float[] bands = [-1.35f, -1.05f, -.78f, -.4f, 0, .4f, .78f, 1.05f, 1.35f];
        var points = new Vertex?[ReliefSections + 1, bands.Length];
        for (int row = 0; row <= ReliefSections; row++)
        {
            float u = frame.HalfLength * (2f * row / ReliefSections - 1); var width = Width(u, frame, shape);
            for (int band = 0; band < bands.Length; band++)
            {
                float v = bands[band] < 0 ? -width.Left * bands[band] : width.Right * bands[band];
                var wanted = frame.Centre + frame.Along * u + frame.Across * v;
                if (Project(source, wanted, frame.Normal, MathF.Max(frame.Reach, frame.HalfLength)) is not { } hit) continue;
                float distance = MathF.Abs(bands[band]); bool crush = shape.Kind == WoundKind.Crush;
                float inset = crush ? .003f : distance > 1 ? .002f : -frame.Depth * Math.Clamp((1 - distance) * 2, 0, 1);
                var tissue = robot ? new Vector4(.16f, .18f, .18f, 1) : AnatomyGeometry.Recolour(new(.32f, .035f, .035f, 1), blood);
                var colour = crush ? Vector4.Lerp(hit.Colour, robot ? new(.06f, .065f, .07f, 1) : AnatomyGeometry.Recolour(new(.24f, .08f, .09f, 1), blood), .65f)
                    : distance > 1.1f ? tissue * .85f : tissue * (.86f + .14f * MathF.Sin(u * 31 + band * 2));
                points[row, band] = hit with { Position = hit.Position + frame.Normal * inset, Normal = frame.Normal, Colour = colour with { W = 1 } };
            }
        }
        for (int row = 0; row < ReliefSections; row++) for (int band = 0; band + 1 < bands.Length; band++)
        {
            Add(points[row, band], points[row + 1, band], points[row, band + 1]);
            Add(points[row + 1, band], points[row + 1, band + 1], points[row, band + 1]);
        }
        void Add(Vertex? a, Vertex? b, Vertex? c)
        {
            if (a is not { } va || b is not { } vb || c is not { } vc) return;
            var cross = Vector3.Cross(vb.Position - va.Position, vc.Position - va.Position);
            if (cross.LengthSquared() < 1e-12f) return;
            if (Vector3.Dot(cross, frame.Normal) < 0) (vb, vc) = (vc, vb);
            output.Add(va); output.Add(vb); output.Add(vc);
        }
    }
    static Vertex? Closest(Triangle[] triangles, Vector3 target, Vector3 normal, float maximumDistance)
    {
        float best = maximumDistance * maximumDistance; Vertex? found = null;
        foreach (var t in triangles)
        {
            var bary = RagdollSkinSurface.ClosestBarycentric(target, t.A.Position, t.B.Position, t.C.Position);
            var point = Interpolate(t, bary);
            if (Vector3.Dot(point.Normal, normal) < .2f) continue;
            float distance = Vector3.DistanceSquared(point.Position, target);
            if (distance >= best) continue;
            best = distance; found = point;
        }
        return found;
    }
    static Vertex? Project(Triangle[] triangles, Vector3 point, Vector3 normal, float reach)
    {
        var start = point + normal * reach; var direction = -normal;
        float best = float.PositiveInfinity; Vertex? found = null;
        foreach (var t in triangles)
        {
            var a = t.A.Position; var edge = t.B.Position - a; var other = t.C.Position - a;
            var cross = Vector3.Cross(direction, other); float determinant = Vector3.Dot(edge, cross);
            if (MathF.Abs(determinant) < 1e-8f) continue;
            var delta = start - a; float u = Vector3.Dot(delta, cross) / determinant;
            if (u < 0 || u > 1) continue;
            var q = Vector3.Cross(delta, edge); float v = Vector3.Dot(direction, q) / determinant;
            if (v < 0 || u + v > 1) continue;
            float distance = Vector3.Dot(other, q) / determinant;
            if (distance < 0 || distance > reach * 2 || distance >= best) continue;
            var hit = Interpolate(t, new(1 - u - v, u, v));
            if (Vector3.Dot(hit.Normal, normal) < .05f) continue;
            best = distance; found = hit;
        }
        return found;
    }
    static Vertex Interpolate(Triangle triangle, Vector3 bary)
    {
        var weights = new Dictionary<int, float>();
        void Add(Vertex vertex, float share) { for (int k = 0; k < 4; k++) if (vertex.Weights[k] > 0) { int bone = (int)(vertex.Bones >> (k * 8) & 255); weights[bone] = weights.GetValueOrDefault(bone) + vertex.Weights[k] * share; } }
        Add(triangle.A, bary.X); Add(triangle.B, bary.Y); Add(triangle.C, bary.Z);
        var selected = weights.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).Take(4).ToArray();
        float total = selected.Sum(pair => pair.Value); uint bones = 0; var blend = Vector4.Zero;
        for (int k = 0; k < selected.Length; k++) { bones |= (uint)selected[k].Key << (8 * k); blend[k] = selected[k].Value / MathF.Max(total, 1e-8f); }
        return triangle.A with { Position = triangle.A.Position * bary.X + triangle.B.Position * bary.Y + triangle.C.Position * bary.Z,
            Normal = Unit(triangle.A.Normal * bary.X + triangle.B.Normal * bary.Y + triangle.C.Normal * bary.Z), Bones = bones, Weights = blend,
            Colour = triangle.A.Colour * bary.X + triangle.B.Colour * bary.Y + triangle.C.Colour * bary.Z };
    }
    static bool Near(Triangle t, Vector3 point, float radius)
    {
        var min = Vector3.Min(t.A.Position, Vector3.Min(t.B.Position, t.C.Position)); var max = Vector3.Max(t.A.Position, Vector3.Max(t.B.Position, t.C.Position));
        return Vector3.DistanceSquared(Vector3.Clamp(point, min, max), point) <= radius * radius;
    }
    static int Group(string bone) => bone.Contains(" L ", StringComparison.Ordinal) ? bone.Contains("Thigh", StringComparison.Ordinal) || bone.Contains("Calf", StringComparison.Ordinal) ? 3 : 1
        : bone.Contains(" R ", StringComparison.Ordinal) ? bone.Contains("Thigh", StringComparison.Ordinal) || bone.Contains("Calf", StringComparison.Ordinal) ? 4 : 2 : 0;
    static Vector3 Unit(Vector3 value) => value.LengthSquared() > 1e-12f ? Vector3.Normalize(value) : Vector3.UnitX;
}
