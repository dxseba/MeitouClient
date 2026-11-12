using System.Numerics;
using Meitou.Brutality.Physics;
using Meitou.Brutality.Rendering;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;
using Meitou.Game.Rendering.Frame;

namespace Meitou.Brutality;

/// <summary>Fits persistent wear to the skinned garment and leaves the underlying body to its own wound pass.</summary>
internal static class StockGarmentDamageMesh
{
    const int EdgeSteps = 8;
    const float DentSkinGap = .045f;
    internal readonly record struct Result(OgreMesh? Mesh, OgreMesh? Detail);

    readonly record struct Triangle(Vertex A, Vertex B, Vertex C, int Group);
    readonly record struct Frame(Vector3 Centre, Vector3 Normal, Vector3 Along, Vector3 Across, float Length,
        float Width, float Severity, GarmentMaterial Material, WoundKind Kind, int Seed, int Group)
    {
        internal Vector3 Point(float along, float across) => Centre + Along * along + Across * across;
    }
    readonly record struct DentProfile(Frame Frame, float Length, float Width, float Depth, float NormalDepth, Triangle[] Underneath);

    internal static Result Build(OgreMesh source, SkeletonRig rig, CharacterAppearance look,
        IReadOnlyList<GarmentDamageMark> marks, Func<IEnumerable<OgreMesh>> underLayers, CancellationToken token)
    {
        if (marks.Count == 0) return default;
        var model = MeshCut.SourceModel(source, rig.BoneCount);
        bool shaved = look.Character?.GetBool("shaved") == true;
        var groups = marks.Any(mark => BodyWoundGeometry.ExtraSite(mark.Entry.Mark) is not null) ? MeshCut.BoneGroups(rig) : null;
        var surface = ReadSurface(source, model, groups);
        var frames = new List<Frame>();
        foreach (var mark in marks.Take(6))
        {
            token.ThrowIfCancellationRequested();
            if (Place(surface, rig, mark) is { } frame) frames.Add(frame);
        }
        if (frames.Count == 0) return default;

        var metal = frames.Where(frame => frame.Material == GarmentMaterial.Metal && frame.Severity >= .48f).ToArray();
        Model? deformed = null;
        if (metal.Length > 0)
        {
            var underneath = underLayers().SelectMany(mesh => ReadSurface(mesh, MeshCut.SourceModel(mesh, rig.BoneCount), null)).ToArray();
            var dents = metal.Select(frame => FitDent(frame, surface, underneath)).ToArray();
            var working = new Model();
            bool changed = false;
            foreach (var part in model.Parts)
            {
                token.ThrowIfCancellationRequested();
                var vertices = part.Vertices.ToArray();
                var deltas = MeshCut.MorphDeltas(source, part.SubMeshIndex, vertices.Length, look.PoseWeights, shaved);
                for (int i = 0; i < vertices.Length; i++)
                {
                    var original = vertices[i] with { Position = vertices[i].Position + deltas[i] };
                    var moved = Dent(original, dents);
                    if (moved.Position == original.Position && moved.Normal == original.Normal) continue;
                    changed = true;
                    vertices[i] = moved with { Position = moved.Position - deltas[i] };
                }
                working.Parts.Add(new ModelPart
                {
                    SubMeshIndex = part.SubMeshIndex, MaterialName = part.MaterialName, Vertices = vertices, Indices = part.Indices,
                    HasUv = part.HasUv, HasTangents = part.HasTangents, HasColours = part.HasColours, Skinned = part.Skinned,
                });
            }
            if (changed)
            {
                deformed = working;
                surface = ReadSurface(source, deformed, groups);
            }
        }
        var openings = new List<CutVolume>();
        var details = new List<Vertex>();
        foreach (var frame in frames)
        {
            token.ThrowIfCancellationRequested();
            var nearby = surface.Where(triangle => MatchesGroup(triangle, frame.Group)
                && Nearby(triangle, frame.Centre, frame.Length * 2 + .6f)).ToArray();
            if (frame.Material == GarmentMaterial.Metal || frame.Kind == WoundKind.Crush
                || frame.Material == GarmentMaterial.Leather && frame.Severity < .25f)
                AddScratch(details, nearby, frame);
            else
                AddTear(openings, details, nearby, frame);
        }

        OgreMesh? mesh = null;
        if (deformed is not null || openings.Count > 0)
        {
            var clipped = MeshCut.Clip(source, rig, [], look.PoseWeights, shaved, removeVolumes: openings.Count > 0 ? openings : null,
                buildCaps: false, preparedModel: deformed, shareSourceVertices: true);
            if (clipped.HasTriangles && (clipped.RemovedSurface || deformed is not null)) mesh = clipped.Skin;
        }
        var detail = details.Count > 0 ? MeshCut.FromTriangles(details, source.SkeletonName) : null;
        if (detail is not null) OgreMeshWeld.Apply(detail);
        return new(mesh, detail);

        List<Triangle> ReadSurface(OgreMesh mesh, Model selected, int[]? boneGroups)
        {
            var triangles = new List<Triangle>();
            foreach (var part in selected.Parts)
            {
                token.ThrowIfCancellationRequested();
                var deltas = MeshCut.MorphDeltas(mesh, part.SubMeshIndex, part.Vertices.Length, look.PoseWeights, shaved);
                for (int i = 0; i + 2 < part.Indices.Length; i += 3)
                {
                    uint a = part.Indices[i], b = part.Indices[i + 1], c = part.Indices[i + 2];
                    if (a >= part.Vertices.Length || b >= part.Vertices.Length || c >= part.Vertices.Length) continue;
                    var first = At(a);
                    var second = At(b);
                    var third = At(c);
                    int group = boneGroups is null ? -1 : MeshCut.TriangleGroup(first, second, third, boneGroups);
                    triangles.Add(new(first, second, third, group));
                }

                Vertex At(uint index) => part.Vertices[index] with { Position = part.Vertices[index].Position + deltas[index] };
            }
            return triangles;
        }
    }

    static Frame? Place(List<Triangle> surface, SkeletonRig rig, GarmentDamageMark damage)
    {
        var wound = damage.Entry.Mark;
        if (wound.Shape.Kind == WoundKind.None || !(damage.Severity > 0)) return null;
        var normal = Unit(wound.Side, Vector3.UnitZ);
        float t = wound.T;
        int group = -1;
        string first, second;
        if (damage.Entry.Region >= 0 || wound.Segment == 9)
        {
            (first, second) = ("Bip01 Pelvis", "Bip01 Neck");
            if (damage.Entry.Region >= 0)
            {
                float off = wound.Age * 37 % 7 / 3f - 1;
                t = (damage.Entry.Region == 0 ? .7f : damage.Entry.Region == 1 ? .35f : .5f) + off * .13f;
                normal = Unit(new(off * .95f, 0, damage.Entry.Region == 2 ? -1 : 1), Vector3.UnitZ);
            }
        }
        else if (BodyWoundGeometry.ExtraSite(wound) is { } detailed)
        {
            (first, second, t, normal, group) = detailed;
        }
        else if (WoundSegments.IsHead(wound.Segment))
        {
            (first, second) = ("Bip01 Neck", "Bip01 Head");
            t = 2.2f + wound.T;
            float lateral = (wound.Born == 0 ? wound.Age : wound.Born) % 2 == 0 ? .9f : -.9f;
            normal = Unit(new(lateral, .5f, .2f * wound.Side.Z), Vector3.UnitZ);
        }
        else
        {
            string side = wound.Segment < 2 || wound.Segment is 4 or 5 ? "L" : "R";
            bool lower = (wound.Segment & 1) != 0;
            var bones = wound.Segment < 4 ? lower ? ("Forearm", "Hand") : ("UpperArm", "Forearm")
                : lower ? ("Calf", "Foot") : ("Thigh", "Calf");
            (first, second) = ($"Bip01 {side} {bones.Item1}", $"Bip01 {side} {bones.Item2}");
        }
        if (rig.Handle(first) is not int a || rig.Handle(second) is not int b) return null;
        bool hand = WoundSegments.IsHand(wound.Segment);
        float handFit = hand ? Math.Clamp(Vector3.Distance(rig.BindPosition[a], rig.BindPosition[b]) / 3.25f, .55f, 1.35f) : 1;
        float offset = hand ? .65f * handFit : 1.2f;
        var target = Vector3.Lerp(rig.BindPosition[a], rig.BindPosition[b], t) + normal * offset;
        float handReach = .85f * handFit;
        float best = hand ? handReach * handReach : WoundSegments.IsHead(wound.Segment) ? 3.0f : 4.0f;
        Vertex? nearest = null;
        foreach (var triangle in surface)
        {
            if (!MatchesGroup(triangle, group) || !Nearby(triangle, target, MathF.Sqrt(best))) continue;
            var bary = RagdollSkinSurface.ClosestBarycentric(target, triangle.A.Position, triangle.B.Position, triangle.C.Position);
            var point = Interpolate(triangle, bary);
            if (Vector3.Dot(point.Normal, normal) < .18f) continue;
            float distance = Vector3.DistanceSquared(point.Position, target);
            if (distance >= best) continue;
            best = distance;
            nearest = point;
        }
        if (nearest is not { } hit) return null;
        normal = Unit(hit.Normal, normal);
        var vertical = rig.BindPosition[b] - rig.BindPosition[a];
        vertical = Unit(vertical - normal * Vector3.Dot(vertical, normal), Vector3.UnitY);
        var across = Unit(Vector3.Cross(vertical, normal), Vector3.UnitX);
        float turn = WoundPolicy.VisualTurn(wound);
        float c = MathF.Cos(turn), s = MathF.Sin(turn);
        var along = Unit(across * (wound.Shape.Cosine * c - wound.Shape.Sine * s)
            + vertical * (wound.Shape.Cosine * s + wound.Shape.Sine * c), across);
        float severity = Math.Clamp(damage.Severity, 0, 1);
        float chart = (.30f + .4f * wound.Shape.Depth) * Math.Clamp(wound.Size, .5f, 1.8f);
        float length = Math.Clamp(chart * (.55f + 1.1f * severity), .13f, 1.35f);
        if (wound.Shape.Kind == WoundKind.Thrust) length *= .64f;
        if (WoundSegments.IsHead(wound.Segment)) length *= .68f;
        if (hand)
        {
            float longest = WoundSegments.IsDistalHand(wound.Segment) ? .23f : .58f;
            length = MathF.Min(length * .57f * handFit, longest * handFit);
        }
        float width = length * (wound.Shape.Kind == WoundKind.Thrust ? .32f : .065f + .12f * severity);
        if (wound.Shape.Kind == WoundKind.Slash)
            width *= Math.Clamp(wound.Shape.Aspect / .20f, .65f, 1.35f);
        if (damage.Material == GarmentMaterial.Leather) width *= .56f;
        return new(hit.Position, normal, along, Unit(Vector3.Cross(normal, along), vertical), length,
            MathF.Max(.013f, width), severity, damage.Material, wound.Shape.Kind, wound.Age, group);
    }

    static bool MatchesGroup(in Triangle triangle, int group) => group < 0 || triangle.Group == group || group == 7 && triangle.Group == 6;

    static void AddTear(List<CutVolume> openings, List<Vertex> details, Triangle[] surface, Frame frame)
    {
        var edges = new Vector2[EdgeSteps + 1];
        bool textile = frame.Material == GarmentMaterial.Cloth;
        for (int i = 0; i <= EdgeSteps; i++)
        {
            float x = i * (2f / EdgeSteps) - 1;
            float taper = MathF.Pow(MathF.Max(.02f, 1 - x * x), textile ? .66f : .94f);
            float centre = frame.Width * (.25f * MathF.Sin(x * 4.7f + frame.Seed) + .14f * Noise(i, frame.Seed));
            float edge = frame.Width * taper * (.82f + .27f * Noise(i * 11, frame.Seed));
            edges[i] = new(centre - edge, centre + edge);
        }
        for (int i = 0; i < EdgeSteps; i++)
        {
            float a = frame.Length * (i * (2f / EdgeSteps) - 1);
            float b = a + frame.Length * (2f / EdgeSteps);
            var left = edges[i]; var right = edges[i + 1];
            Vector3[] polygon = [frame.Point(a, left.X), frame.Point(b, right.X),
                frame.Point(b, right.Y), frame.Point(a, left.Y)];
            var planes = new List<CutHalfspace>(6);
            for (int side = 0; side < polygon.Length; side++)
            {
                var normal = Vector3.Normalize(Vector3.Cross(frame.Normal, polygon[(side + 1) % polygon.Length] - polygon[side]));
                planes.Add(new(new(normal, -Vector3.Dot(normal, polygon[side])), MorphedDistance: true));
            }
            float depth = .19f + .27f * frame.Severity;
            planes.Add(new(new(frame.Normal, -Vector3.Dot(frame.Normal, frame.Centre) + depth), MorphedDistance: true));
            planes.Add(new(new(-frame.Normal, Vector3.Dot(frame.Normal, frame.Centre) + depth), MorphedDistance: true));
            openings.Add(new(planes, frame.Normal, Bounds: new(frame.Centre, frame.Length + depth + .15f), Group: frame.Group));

            float border = Math.Clamp(frame.Width * .13f, .009f, .039f);
            var edgeColour = textile ? new Vector4(.35f, .29f, .23f, 1) : new(.25f, .15f, .09f, 1);
            for (int side = -1; side <= 1; side += 2)
            {
                float edgeA = side < 0 ? left.X : left.Y, edgeB = side < 0 ? right.X : right.Y;
                var p = Sample(surface, frame, a, edgeA, .0045f, edgeColour);
                var q = Sample(surface, frame, b, edgeB, .0045f, edgeColour);
                var pOuter = Sample(surface, frame, a, edgeA + side * border, .009f, edgeColour * new Vector4(.58f, .58f, .58f, 1));
                var qOuter = Sample(surface, frame, b, edgeB + side * border, .009f, edgeColour * new Vector4(.58f, .58f, .58f, 1));
                TriangleFace(details, p, q, pOuter, frame.Normal);
                TriangleFace(details, q, qOuter, pOuter, frame.Normal);
                if (!textile || i == 0 || i == EdgeSteps - 1 || p is not { } start || q is not { } end) continue;
                var root = MeshCut.Interpolate(start, end, .23f + .51f * Noise(i * 9 + side, frame.Seed));
                var fibre = root with { Position = root.Position - side * frame.Across * border * (1 + 2.6f * Noise(i + side, frame.Seed))
                    + frame.Along * border * .7f - frame.Normal * border * .45f, Colour = new(.46f, .38f, .28f, 1) };
                var baseEdge = root with { Position = root.Position + frame.Along * border * .7f };
                TriangleFace(details, root, baseEdge, fibre, frame.Normal);
            }
        }
    }

    static void AddScratch(List<Vertex> details, Triangle[] surface, Frame frame)
    {
        bool metal = frame.Material == GarmentMaterial.Metal;
        float length = frame.Kind == WoundKind.Crush ? frame.Length * .30f : frame.Length;
        float width = Math.Clamp(frame.Width * .26f, .012f, .043f);
        var shade = metal ? new Vector4(.13f, .13f, .13f, 1) : new(.16f, .10f, .066f, 1);
        var glint = metal ? new Vector4(.46f, .45f, .40f, 1) : new(.30f, .20f, .13f, 1);
        for (int i = 0; i < EdgeSteps; i++)
        {
            float a = length * (i * (2f / EdgeSteps) - 1), b = a + length * (2f / EdgeSteps);
            float pa = width * .58f * Noise(i, frame.Seed), pb = width * .58f * Noise(i + 1, frame.Seed);
            float size = width * (.35f + .65f * MathF.Sin((i + .5f) * MathF.PI / EdgeSteps));
            var p = Sample(surface, frame, a, pa, .013f, shade);
            var q = Sample(surface, frame, b, pb, .013f, shade);
            var r = Sample(surface, frame, a, pa + size, .011f, glint);
            var s = Sample(surface, frame, b, pb + size, .011f, glint);
            TriangleFace(details, p, q, r, frame.Normal);
            TriangleFace(details, q, s, r, frame.Normal);
        }
    }

    static DentProfile FitDent(Frame frame, List<Triangle> metal, Triangle[] underneath)
    {
        float length = frame.Length * 1.38f + .20f;
        float width = frame.Length * .70f + .19f;
        float desired = .07f + .17f * frame.Severity;
        // A garment can move relative to its lining when the rig bends. Leave clearance even in the bind pose.
        float depth = MathF.Min(desired, .12f);
        float reach = length + .65f;
        var shell = metal.Where(triangle => Nearby(triangle, frame.Centre, reach)).ToArray();
        var blockers = underneath.Where(triangle => Nearby(triangle, frame.Centre, reach)).ToArray();
        LimitAt(0, 0, 1);
        for (int ring = 1; ring <= 2; ring++)
        {
            float radius = .31f * ring;
            float falloff = 1 - radius * radius;
            for (int sector = 0; sector < 8; sector++)
            {
                float angle = (sector + .5f * ring) * (MathF.Tau / 8);
                LimitAt(MathF.Cos(angle) * radius * length, MathF.Sin(angle) * radius * width, falloff);
            }
        }
        // On tight armour the remaining impression comes from its normals rather than intersecting the next layer.
        float normalDepth = MathF.Max(depth, desired * .62f);
        return new(frame, length, width, depth, normalDepth, blockers);

        void LimitAt(float along, float across, float falloff)
        {
            if (depth <= 0 || Sample(shell, frame, along, across, 0, default) is not { } point) return;
            float weight = falloff * falloff;
            depth = MathF.Min(depth, Clearance(point.Position, frame.Normal, blockers, depth * weight) / weight);
        }
    }

    static float Clearance(Vector3 position, Vector3 normal, Triangle[] underneath, float travel)
    {
        const float outward = .03f;
        var start = position + normal * outward;
        var direction = -normal;
        float nearest = outward + DentSkinGap + travel;
        foreach (var triangle in underneath)
        {
            if (!Nearby(triangle, start, nearest)) continue;
            if (RayHit(triangle, start, direction, nearest, out _, out float distance)) nearest = distance;
        }
        return Math.Clamp(nearest - outward - DentSkinGap, 0, travel);
    }

    static Vertex Dent(Vertex vertex, DentProfile[] dents)
    {
        foreach (var dent in dents)
        {
            var frame = dent.Frame;
            var offset = vertex.Position - frame.Centre;
            float depth = Vector3.Dot(offset, frame.Normal);
            if (MathF.Abs(depth) > .37f || Vector3.Dot(vertex.Normal, frame.Normal) < .18f) continue;
            float x = Vector3.Dot(offset, frame.Along) / dent.Length, y = Vector3.Dot(offset, frame.Across) / dent.Width;
            float falloff = 1 - x * x - y * y;
            if (falloff <= 0) continue;
            float travel = dent.Depth * falloff * falloff;
            if (travel > 0) travel = Clearance(vertex.Position, frame.Normal, dent.Underneath, travel);
            var slope = (frame.Along * (x / dent.Length) + frame.Across * (y / dent.Width)) * (4 * dent.NormalDepth * falloff);
            vertex = vertex with { Position = vertex.Position - frame.Normal * travel,
                Normal = Unit(vertex.Normal - slope, vertex.Normal) };
        }
        return vertex;
    }

    static Vertex? Sample(Triangle[] surface, Frame frame, float along, float across, float lift, Vector4 colour)
    {
        var start = frame.Point(along, across) + frame.Normal * .48f;
        var ray = -frame.Normal;
        float best = .96f;
        Vertex? hit = null;
        foreach (var triangle in surface)
        {
            if (!RayHit(triangle, start, ray, best, out var barycentric, out float distance)) continue;
            var vertex = Interpolate(triangle, barycentric);
            if (Vector3.Dot(vertex.Normal, frame.Normal) < .12f) continue;
            best = distance;
            hit = vertex with { Position = vertex.Position + vertex.Normal * lift, Colour = colour };
        }
        return hit;
    }

    static bool RayHit(in Triangle triangle, Vector3 start, Vector3 ray, float limit, out Vector3 barycentric, out float distance)
    {
        barycentric = default;
        distance = 0;
        var edge = triangle.B.Position - triangle.A.Position;
        var other = triangle.C.Position - triangle.A.Position;
        var cross = Vector3.Cross(ray, other);
        float determinant = Vector3.Dot(edge, cross);
        if (MathF.Abs(determinant) < 1e-8f) return false;
        var offset = start - triangle.A.Position;
        float u = Vector3.Dot(offset, cross) / determinant;
        if (u < 0 || u > 1) return false;
        var q = Vector3.Cross(offset, edge);
        float v = Vector3.Dot(ray, q) / determinant;
        if (v < 0 || u + v > 1) return false;
        distance = Vector3.Dot(other, q) / determinant;
        if (distance < 0 || distance >= limit) return false;
        barycentric = new(1 - u - v, u, v);
        return true;
    }

    static void TriangleFace(List<Vertex> details, Vertex? a, Vertex? b, Vertex? c, Vector3 normal)
    {
        if (a is not { } first || b is not { } second || c is not { } third) return;
        var cross = Vector3.Cross(second.Position - first.Position, third.Position - first.Position);
        if (cross.LengthSquared() < 1e-12f || Vector3.DistanceSquared(first.Position, third.Position) > 1) return;
        if (Vector3.Dot(cross, normal) < 0) (second, third) = (third, second);
        details.Add(first); details.Add(second); details.Add(third);
    }

    static Vertex Interpolate(Triangle triangle, Vector3 barycentric)
    {
        float edge = barycentric.X + barycentric.Y;
        return edge < 1e-6f ? triangle.C
            : MeshCut.Interpolate(MeshCut.Interpolate(triangle.A, triangle.B, barycentric.Y / edge), triangle.C, barycentric.Z);
    }

    static bool Nearby(Triangle triangle, Vector3 point, float radius)
    {
        var minimum = Vector3.Min(triangle.A.Position, Vector3.Min(triangle.B.Position, triangle.C.Position));
        var maximum = Vector3.Max(triangle.A.Position, Vector3.Max(triangle.B.Position, triangle.C.Position));
        return Vector3.DistanceSquared(Vector3.Clamp(point, minimum, maximum), point) <= radius * radius;
    }

    static Vector3 Unit(Vector3 value, Vector3 fallback) => value.LengthSquared() > 1e-10f ? Vector3.Normalize(value) : fallback;

    static float Noise(int step, int seed)
    {
        uint bits = unchecked((uint)step * 0x9e3779b9u + (uint)seed * 0x85ebca6bu);
        bits = unchecked((bits ^ bits >> 16) * 0x7feb352du);
        return (bits >> 8) / 8388607.5f - 1;
    }
}
