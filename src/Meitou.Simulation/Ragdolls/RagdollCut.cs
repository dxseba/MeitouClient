using System.Numerics;
using BepuUtilities.Memory;
using Meitou.Data.Physics;

namespace Meitou.Simulation.Ragdolls;

/// <summary>Clips a ragdoll's collision bodies and joints to one side of a bind-space cut.</summary>
public static class RagdollCut
{
    const float Epsilon = 0.0001f;

    /// <summary>
    /// Keeps original skeleton bone handles; an output body's source index is <c>source.BodyOfBone[body.Bone]</c>.
    /// Crossed bodies become convex hulls with adjusted body/bone frames. Disconnected joint chains remain disconnected.
    /// Transverse cuts keep whole arms and the head above and whole legs below, matching their mesh ownership.
    /// An optional retained-skin bridge predicate receives original bone handles and keeps the corresponding joint connected.
    /// </summary>
    public static RagdollDefinition? Build(RagdollDefinition source, Vector4 plane, int side, bool fullBody, Func<int, int, bool>? skinBridge = null)
    {
        var normal = new Vector3(plane.X, plane.Y, plane.Z);
        float length = normal.Length();
        if (side is not (-1 or 1) || !float.IsFinite(length) || length < Epsilon || !float.IsFinite(plane.W)) return null;
        plane *= side / length;
        return Filter(source, body =>
        {
            int wholeSide = fullBody ? 0 : WholeSide(body.Name);
            return wholeSide == side ? body : wholeSide != 0 ? null : Clip(body, plane);
        }, joint =>
        {
            var oldA = source.Bodies[joint.A];
            var oldB = source.Bodies[joint.B];
            bool wholeChain = !fullBody && WholeSide(oldA.Name) == side && WholeSide(oldB.Name) == side;
            // A diagonal can retain a connected side of the trunk after removing the joint's original centre anchor.
            // Keep that axial chain, but sever out-of-half shoulder/elbow/hip anchors normally.
            return wholeChain || Axial(oldA.Name) && Axial(oldB.Name) || skinBridge?.Invoke(oldA.Bone, oldB.Bone) == true
                || Distance(plane, oldA.Bind.Apply(joint.OffsetA)) >= -Epsilon && Distance(plane, oldB.Bind.Apply(joint.OffsetB)) >= -Epsilon;
        }, joint =>
        {
            var a = source.Bodies[joint.A];
            var b = source.Bodies[joint.B];
            if (!(Axial(a.Name) && Axial(b.Name)) && skinBridge?.Invoke(a.Bone, b.Bone) != true) return null;
            var anchorA = a.Bind.Apply(joint.OffsetA);
            var anchorB = b.Bind.Apply(joint.OffsetB);
            if (Distance(plane, anchorA) >= -Epsilon && Distance(plane, anchorB) >= -Epsilon) return null;
            var anchor = (anchorA + anchorB) * 0.5f;
            return anchor + new Vector3(plane.X, plane.Y, plane.Z) * MathF.Max(0, Epsilon - Distance(plane, anchor));
        });
    }

    /// <summary>Rebinds selected or clipped actors without changing original bone handles or source data.</summary>
    internal static RagdollDefinition? Filter(RagdollDefinition source, Func<RagdollBody, RagdollBody?> select, Func<RagdollJoint, bool>? keepJoint = null, Func<RagdollJoint, Vector3?>? moveAnchor = null)
    {
        var bodies = new List<RagdollBody>();
        var remap = new int[source.Bodies.Length];
        Array.Fill(remap, -1);
        var boneMap = new int[source.BodyOfBone.Length];
        Array.Fill(boneMap, -1);
        for (int b = 0; b < source.Bodies.Length; b++)
        {
            var original = source.Bodies[b];
            if ((uint)original.Bone >= (uint)boneMap.Length || select(original) is not { } kept) continue;
            remap[b] = bodies.Count;
            boneMap[kept.Bone] = bodies.Count;
            bodies.Add(kept);
        }
        if (bodies.Count == 0) return null;

        var joints = new List<RagdollJoint>();
        var joined = new ulong[bodies.Count];
        foreach (var joint in source.Joints)
        {
            if ((uint)joint.A >= (uint)remap.Length || (uint)joint.B >= (uint)remap.Length) continue;
            int a = remap[joint.A], b = remap[joint.B];
            if (a < 0 || b < 0 || keepJoint?.Invoke(joint) == false) continue;
            var oldA = source.Bodies[joint.A];
            var oldB = source.Bodies[joint.B];
            var anchorA = oldA.Bind.Apply(joint.OffsetA);
            var anchorB = oldB.Bind.Apply(joint.OffsetB);
            if (moveAnchor?.Invoke(joint) is { } moved) anchorA = anchorB = moved;
            var newA = bodies[a].Bind;
            var newB = bodies[b].Bind;
            joints.Add(new RagdollJoint
            {
                Type = joint.Type, A = a, B = b,
                OffsetA = newA.Inverse().Apply(anchorA), OffsetB = newB.Inverse().Apply(anchorB),
                BasisA = Quaternion.Normalize(Quaternion.Inverse(newA.Rotation) * oldA.Bind.Rotation * joint.BasisA),
                BasisB = Quaternion.Normalize(Quaternion.Inverse(newB.Rotation) * oldB.Bind.Rotation * joint.BasisB),
                Limits = joint.Limits, Low = joint.Low, High = joint.High, Swing = joint.Swing,
                SpringStiffness = joint.SpringStiffness, SpringDamper = joint.SpringDamper, SpringTarget = joint.SpringTarget,
            });
            if (a < 64) joined[b] |= 1UL << a;
            if (b < 64) joined[a] |= 1UL << b;
        }
        int root = (uint)source.Root < (uint)remap.Length ? remap[source.Root] : -1;
        if (root < 0)
        {
            root = 0;
            for (int b = 1; b < bodies.Count; b++) if (bodies[b].Mass > bodies[root].Mass) root = b;
        }
        return new RagdollDefinition { Bodies = [.. bodies], Joints = [.. joints], BodyOfBone = boneMap, Joined = joined, Root = root };
    }

    static bool Axial(string name) => name is "Bip01" or "Bip01 Pelvis" or "Bip01 Neck" or "Bip01 Head"
        || name.StartsWith("Bip01 Spine", StringComparison.Ordinal);

    static int WholeSide(string name)
    {
        if (name is "Bip01 Head" or "Bip01 Neck" or "Bip01 Jaw") return 1;
        if (!(name.Contains(" L ", StringComparison.Ordinal) || name.Contains(" R ", StringComparison.Ordinal)) || name.Contains("Clavicle", StringComparison.Ordinal)) return 0;
        return name.Contains("Thigh", StringComparison.Ordinal) || name.Contains("Calf", StringComparison.Ordinal) || name.Contains("Foot", StringComparison.Ordinal) || name.Contains("Toe", StringComparison.Ordinal) ? -1 : 1;
    }

    static float Distance(Vector4 plane, Vector3 point) => Vector3.Dot(new(plane.X, plane.Y, plane.Z), point) + plane.W;

    internal static RagdollBody? Clip(RagdollBody body, Vector4 plane)
    {
        var points = Points(body);
        if (points.Length < 4 || points.Any(p => !Finite(p))) return null;
        var normal = new Vector3(plane.X, plane.Y, plane.Z);
        var localNormal = Vector3.Transform(normal, Quaternion.Inverse(body.Bind.Rotation));
        float offset = Vector3.Dot(normal, body.Bind.Position) + plane.W;
        var distances = points.Select(p => Vector3.Dot(localNormal, p) + offset).ToArray();
        float minimum = distances.Min(), maximum = distances.Max();
        if (body.Type is PhysicsShapeType.Sphere or PhysicsShapeType.Capsule)
        {
            // Reuse a native curved primitive only when its actual support, not just sampled points, clears the plane.
            float extent = body.Size.X + (body.Type == PhysicsShapeType.Capsule ? body.Size.Y * 0.5f * MathF.Abs(localNormal.Y) : 0);
            minimum = offset - extent;
            maximum = offset + extent;
        }
        if (minimum >= 0) return body;
        if (maximum <= Epsilon) return null;
        var kept = new List<Vector3>();
        for (int p = 0; p < points.Length; p++) if (distances[p] >= 0) AddUnique(kept, points[p]);
        var crossings = new List<Vector3>();
        // Every true hull edge is among these chords; extra chord intersections lie inside the same convex section.
        // Reducing their planar convex hull bounds the final collider point count instead of retaining quadratic interior points.
        for (int a = 0; a < points.Length; a++)
            for (int b = a + 1; b < points.Length; b++)
            {
                if (!(distances[a] < 0 && distances[b] > 0 || distances[a] > 0 && distances[b] < 0)) continue;
                var point = Vector3.Lerp(points[a], points[b], distances[a] / (distances[a] - distances[b]));
                point -= localNormal * (Vector3.Dot(localNormal, point) + offset);
                crossings.Add(point);
            }
        foreach (var point in SectionHull(crossings, localNormal)) AddUnique(kept, point);
        if (!HasVolume(kept)) return null;
        // Span along the normal gives a stable mass estimate; the hull provides the actual centre and inertia geometry.
        float share = Math.Clamp(maximum / (maximum - minimum), 0.01f, 1);
        float mass = MathF.Max(body.Mass * share, 0.001f);
        if (!MakeStableHull(kept, localNormal, offset, mass, out var hullPoints, out var centre)) return null;
        var bind = body.Bind with { Position = body.Bind.Apply(centre) };
        var boneBind = body.BoneInBody.Then(body.Bind);
        return new RagdollBody
        {
            Bone = body.Bone, Name = body.Name, Type = PhysicsShapeType.Mesh,
            Hull = hullPoints, Mass = mass,
            Bind = bind, BoneInBody = boneBind.Then(bind.Inverse()),
        };
    }

    static bool MakeStableHull(List<Vector3> points, Vector3 normal, float offset, float mass, out Vector3[] hull, out Vector3 centre)
    {
        if (CheckHull(points.ToArray(), mass, out hull, out centre)) return true;
        var min = points.Aggregate(new Vector3(float.MaxValue), Vector3.Min);
        var max = points.Aggregate(new Vector3(float.MinValue), Vector3.Max);
        var origin = (min + max) * 0.5f;
        float scale = Vector3.Distance(min, max);
        float normalSquared = normal.LengthSquared();
        float movedOffset = offset + Vector3.Dot(normal, origin);
        // Near-collinear section points can make the hull builder produce a wrong centroid or an indefinite inertia.
        // Weld them on deterministic, shape-scaled grids and keep the cut face on its exact half-space boundary.
        foreach (float factor in new[] { 0.00001f, 0.00002f, 0.00005f, 0.0001f, 0.0002f, 0.0005f, 0.001f })
        {
            float grid = MathF.Max(0.00001f, scale * factor);
            var welded = new List<Vector3>();
            foreach (var point in points)
            {
                var local = point - origin;
                float before = Vector3.Dot(normal, local) + movedOffset;
                var snapped = new Vector3(MathF.Round(local.X / grid) * grid, MathF.Round(local.Y / grid) * grid, MathF.Round(local.Z / grid) * grid);
                float distance = Vector3.Dot(normal, snapped) + movedOffset;
                if (distance < 0 || MathF.Abs(before) < grid * 2) snapped -= normal * (distance / normalSquared);
                if (!welded.Any(p => Vector3.DistanceSquared(p, snapped) < grid * grid * 0.01f)) welded.Add(snapped);
            }
            var candidate = welded.OrderBy(p => p.X).ThenBy(p => p.Y).ThenBy(p => p.Z).ToArray();
            if (!CheckHull(candidate, mass, out hull, out centre)) continue;
            centre += origin;
            return true;
        }
        // Last-resort approximations use only original retained points, so they cannot extend through the cut.
        var centred = points.Select(p => p - origin).ToArray();
        var supports = new List<Vector3>();
        for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
        {
            if (x == 0 && y == 0 && z == 0) continue;
            var direction = new Vector3(x, y, z);
            AddUnique(supports, centred.MaxBy(p => Vector3.Dot(p, direction)));
        }
        if (CheckHull(supports.ToArray(), mass, out hull, out centre)) { centre += origin; return true; }
        var a = centred[0];
        var b = centred.MaxBy(p => Vector3.DistanceSquared(p, a));
        var c = centred.MaxBy(p => Vector3.Cross(b - a, p - a).LengthSquared());
        var face = Vector3.Cross(b - a, c - a);
        var d = centred.MaxBy(p => MathF.Abs(Vector3.Dot(face, p - a)));
        if (CheckHull([a, b, c, d], mass, out hull, out centre)) { centre += origin; return true; }
        return false;
    }

    static bool CheckHull(Vector3[] points, float mass, out Vector3[] centred, out Vector3 centre)
    {
        centred = [];
        centre = default;
        if (!HasVolume(points.ToList()) || !float.IsFinite(mass) || mass <= 0) return false;
        var min = points.Aggregate(new Vector3(float.MaxValue), Vector3.Min);
        var max = points.Aggregate(new Vector3(float.MinValue), Vector3.Max);
        float tolerance = MathF.Max(0.000001f, Vector3.Distance(min, max) * 0.00001f);
        var pool = new BufferPool();
        try
        {
            var first = new BepuPhysics.Collidables.ConvexHull(points, pool, out var firstCentre);
            try
            {
                if (!Finite(firstCentre) || !Positive(first.ComputeInertia(mass)) || firstCentre.X < min.X - tolerance || firstCentre.X > max.X + tolerance
                    || firstCentre.Y < min.Y - tolerance || firstCentre.Y > max.Y + tolerance || firstCentre.Z < min.Z - tolerance || firstCentre.Z > max.Z + tolerance) return false;
                var moved = points.Select(p => p - firstCentre).ToArray();
                var repeat = new BepuPhysics.Collidables.ConvexHull(moved, pool, out var repeatCentre);
                try
                {
                    // Shapes() constructs this same centred point cloud again: validate that exact operation before publishing it.
                    if (!Finite(repeatCentre) || repeatCentre.Length() > tolerance || !Positive(repeat.ComputeInertia(mass))) return false;
                    centred = moved;
                    centre = firstCentre;
                    return true;
                }
                finally { repeat.Dispose(pool); }
            }
            finally { first.Dispose(pool); }
        }
        finally { pool.Clear(); }
    }

    static bool Positive(BepuPhysics.BodyInertia inertia)
    {
        var t = inertia.InverseInertiaTensor;
        if (!float.IsFinite(inertia.InverseMass) || inertia.InverseMass <= 0 || !float.IsFinite(t.XX) || !float.IsFinite(t.YX)
            || !float.IsFinite(t.YY) || !float.IsFinite(t.ZX) || !float.IsFinite(t.ZY) || !float.IsFinite(t.ZZ)) return false;
        double xx = t.XX, yx = t.YX, yy = t.YY, zx = t.ZX, zy = t.ZY, zz = t.ZZ;
        double minor = xx * yy - yx * yx;
        double determinant = xx * (yy * zz - zy * zy) - yx * (yx * zz - zx * zy) + zx * (yx * zy - zx * yy);
        return xx > 0 && minor > 0 && determinant > 0;
    }
    internal static Vector3[] Points(RagdollBody body)
    {
        if (body.Type == PhysicsShapeType.Mesh) return body.Hull ?? [];
        if (body.Type is not (PhysicsShapeType.Sphere or PhysicsShapeType.Capsule))
        {
            var half = body.Size * 0.5f;
            return [new(-half.X,-half.Y,-half.Z), new(half.X,-half.Y,-half.Z), new(-half.X,half.Y,-half.Z), new(half.X,half.Y,-half.Z),
                new(-half.X,-half.Y,half.Z), new(half.X,-half.Y,half.Z), new(-half.X,half.Y,half.Z), new(half.X,half.Y,half.Z)];
        }
        float radius = body.Size.X;
        float halfLength = body.Type == PhysicsShapeType.Capsule ? body.Size.Y * 0.5f : 0;
        var points = new List<Vector3> { new(0, halfLength + radius, 0), new(0, -halfLength - radius, 0) };
        for (int hemisphere = -1; hemisphere <= 1; hemisphere += 2)
            for (int latitude = 0; latitude <= 60; latitude += 30)
            {
                if (halfLength == 0 && hemisphere == -1 && latitude == 0) continue;
                float angle = latitude * (MathF.PI / 180);
                float y = hemisphere * (halfLength + radius * MathF.Sin(angle));
                float width = radius * MathF.Cos(angle);
                for (int longitude = 0; longitude < 8; longitude++)
                {
                    float turn = longitude * (MathF.Tau / 8);
                    points.Add(new(width * MathF.Cos(turn), y, width * MathF.Sin(turn)));
                }
            }
        return [.. points];
    }

    static IEnumerable<Vector3> SectionHull(List<Vector3> points, Vector3 normal)
    {
        if (points.Count < 3) return points;
        var first = Vector3.Normalize(Vector3.Cross(normal, MathF.Abs(normal.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
        var second = Vector3.Cross(normal, first);
        var ordered = points.Select(p => (Point: p, X: Vector3.Dot(p, first), Y: Vector3.Dot(p, second))).OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
        var hull = new List<(Vector3 Point, float X, float Y)>();
        static float Turn((Vector3 Point, float X, float Y) a, (Vector3 Point, float X, float Y) b, (Vector3 Point, float X, float Y) c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        foreach (var point in ordered)
        {
            while (hull.Count >= 2 && Turn(hull[^2], hull[^1], point) <= 1e-7f) hull.RemoveAt(hull.Count - 1);
            hull.Add(point);
        }
        int lower = hull.Count;
        for (int p = ordered.Length - 2; p >= 0; p--)
        {
            var point = ordered[p];
            while (hull.Count > lower && Turn(hull[^2], hull[^1], point) <= 1e-7f) hull.RemoveAt(hull.Count - 1);
            hull.Add(point);
        }
        if (hull.Count > 1) hull.RemoveAt(hull.Count - 1);
        return hull.Select(p => p.Point);
    }

    static bool HasVolume(List<Vector3> points)
    {
        if (points.Count < 4) return false;
        var a = points[0];
        var b = points.MaxBy(p => Vector3.DistanceSquared(p, a));
        var axis = b - a;
        float squared = axis.LengthSquared();
        if (squared < Epsilon * Epsilon) return false;
        var c = points.MaxBy(p => Vector3.Cross(axis, p - a).LengthSquared());
        var normal = Vector3.Cross(axis, c - a);
        float area = normal.Length();
        float thickness = MathF.Max(Epsilon, MathF.Sqrt(squared) * Epsilon);
        if (area < thickness * MathF.Sqrt(squared)) return false;
        return points.Max(p => MathF.Abs(Vector3.Dot(p - a, normal))) / area > thickness;
    }

    static void AddUnique(List<Vector3> points, Vector3 point)
    {
        if (!points.Any(p => Vector3.DistanceSquared(p, point) < 1e-10f)) points.Add(point);
    }

    static bool Finite(Vector3 point) => float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);
}
