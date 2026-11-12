// Adapted from integrated GPL-3.0-or-later source 3a15eeb9f5523b9d106b40fd0ec99ccdb2658efa.
using Meitou.Game.Rendering.Frame;
using System.Numerics;

namespace Meitou.Brutality.Rendering.Surface;

internal readonly record struct NativeSectionCut(Vector4 Plane, int KeepSide, bool FullBody = false, bool HeadOnly = false);

/// <summary>A wet cross-section rim; full-body cuts also have recessed flesh backing behind the installed anatomy.</summary>
internal static partial class NativeSectionGeometry
{
    public const int Layer = 32;
    public const float ExposureDepth = 2.8f;

    public static bool TryNormalize(in NativeSectionCut cut, out NativeSectionCut normalized)
    {
        normalized = default;
        var n = new Vector3(cut.Plane.X, cut.Plane.Y, cut.Plane.Z);
        float length = n.Length();
        if (cut.KeepSide is not (-1 or 1) || !float.IsFinite(length) || length < 1e-5f || !float.IsFinite(cut.Plane.W)) return false;
        var plane = cut.Plane / length;
        if (!cut.FullBody && !cut.HeadOnly && plane.Y < 0.5f) return false;
        normalized = new(plane, cut.KeepSide, cut.FullBody, cut.HeadOnly);
        return true;
    }

    public static Matrix4x4 Packed(in NativeSectionCut cut) => new(
        cut.Plane.X, cut.Plane.Y, cut.Plane.Z, cut.Plane.W,
        cut.KeepSide, cut.HeadOnly ? 1.1f : ExposureDepth, cut.HeadOnly ? 2 : cut.FullBody ? 1 : 0, 0,
        0, 0, 0, 0,
        0, 0, 0, 0);

    public static int Group(in Vertex vertex, uint[] groups)
    {
        int at = 0;
        for (int k = 1; k < 4; k++) if (vertex.Weights[k] > vertex.Weights[at]) at = k;
        int bone = (int)(vertex.Bones >> (at * 8) & 255);
        return bone / 8 < groups.Length ? (int)(groups[bone / 8] >> (bone % 8 * 4) & 15) : 0;
    }

    public static float Band(Vector3 position, int group, in NativeSectionCut cut) => cut.HeadOnly && group is not (6 or 7)
        ? 1000 : (cut.FullBody || cut.HeadOnly ? 0 : group) switch
    {
        1 or 2 or 6 => cut.KeepSide * 1000,
        3 or 4 => -cut.KeepSide * 1000,
        _ => (Vector3.Dot(new(cut.Plane.X, cut.Plane.Y, cut.Plane.Z), position) + cut.Plane.W) * cut.KeepSide,
    };

    /// <summary>A bind-space pick box clipped to the same kept half; ray parameter remains the caller's world distance.</summary>
    public static float? RayBox(Vector3 origin, Vector3 direction, Vector3 min, Vector3 max, int group, in NativeSectionCut cut)
    {
        float near = 0, far = float.MaxValue;
        for (int axis = 0; axis < 3; axis++)
        {
            if (MathF.Abs(direction[axis]) < 1e-9f)
            {
                if (origin[axis] < min[axis] || origin[axis] > max[axis]) return null;
                continue;
            }
            float a = (min[axis] - origin[axis]) / direction[axis], b = (max[axis] - origin[axis]) / direction[axis];
            if (a > b) (a, b) = (b, a);
            near = MathF.Max(near, a); far = MathF.Min(far, b);
            if (near > far) return null;
        }
        if (cut.HeadOnly && group is not (6 or 7)) return near;
        if (group != 0 && !cut.FullBody) return Band(default, group, cut) >= 0 ? near : null;
        var normal = new Vector3(cut.Plane.X, cut.Plane.Y, cut.Plane.Z);
        float at = Band(origin, group, cut), slope = Vector3.Dot(normal, direction) * cut.KeepSide;
        if (MathF.Abs(slope) < 1e-9f) return at >= 0 ? near : null;
        float plane = -at / slope;
        if (slope > 0) near = MathF.Max(near, plane); else far = MathF.Min(far, plane);
        return near <= far ? near : null;
    }

    static float Ragged(Vector3 p) => 0.018f * MathF.Sin(p.X * 13 + p.Y * 7 + p.Z * 11) + 0.012f * MathF.Sin(p.X * 29 + p.Z * 17);
    static Vector3 Round(Vector3 p) => new(MathF.Round(p.X, 4), MathF.Round(p.Y, 4), MathF.Round(p.Z, 4));
    readonly record struct Point(Vector3 Position, Vector3 Morph, uint Bones, Vector4 Weights, int Component = -1, int Region = -1);

    static Point Interpolate(Point a, Point b, float t)
    {
        var weights = new Dictionary<uint, float>();
        for (int k = 0; k < 4; k++)
        {
            uint ba = a.Bones >> (8 * k) & 255, bb = b.Bones >> (8 * k) & 255;
            weights[ba] = weights.GetValueOrDefault(ba) + a.Weights[k] * (1 - t);
            weights[bb] = weights.GetValueOrDefault(bb) + b.Weights[k] * t;
        }
        var top = weights.Where(p => p.Value > 0).OrderByDescending(p => p.Value).Take(4).ToArray();
        float sum = top.Sum(p => p.Value);
        uint bones = 0;
        var w = Vector4.Zero;
        for (int k = 0; k < top.Length; k++) { bones |= top[k].Key << (k * 8); w[k] = top[k].Value / sum; }
        return new(Vector3.Lerp(a.Position, b.Position, t), Vector3.Lerp(a.Morph, b.Morph, t), bones, w, a.Component == b.Component ? a.Component : -1, a.Region == b.Region ? a.Region : -1);
    }

    /// <summary>Kept body samples and exact cut-edge samples, with the body morph applied once before skinning.</summary>
    public static IEnumerable<Vertex> KeptVertices(Vertex[] source, uint[] groups, NativeSectionCut cut, Vector4[]? morphs = null)
    {
        if (!TryNormalize(cut, out var normalized)) yield break;
        cut = normalized;
        Vector3 Delta(in Vertex vertex)
        {
            uint slot = BitConverter.SingleToUInt32Bits(vertex.Colour.X);
            return morphs is not null && slot > 0 && slot <= morphs.Length ? new(morphs[slot - 1].X, morphs[slot - 1].Y, morphs[slot - 1].Z) : default;
        }
        for (int i = 0; i + 2 < source.Length; i += 3)
            for (int k = 0; k < 3; k++)
            {
                var a = source[i + k]; var b = source[i + (k + 1) % 3];
                float da = Band(a.Position + (cut.HeadOnly ? Delta(a) : default), Group(a, groups), cut);
                float db = Band(b.Position + (cut.HeadOnly ? Delta(b) : default), Group(b, groups), cut);
                if (da >= 0) yield return a with { Position = a.Position + Delta(a) };
                if ((da < 0) == (db < 0)) continue;
                float t = da / (da - db);
                var weights = new Dictionary<uint, float>();
                for (int influence = 0; influence < 4; influence++)
                {
                    uint ba = a.Bones >> (8 * influence) & 255, bb = b.Bones >> (8 * influence) & 255;
                    weights[ba] = weights.GetValueOrDefault(ba) + a.Weights[influence] * (1 - t);
                    weights[bb] = weights.GetValueOrDefault(bb) + b.Weights[influence] * t;
                }
                var top = weights.Where(p => p.Value > 0).OrderByDescending(p => p.Value).Take(4).ToArray();
                float sum = top.Sum(p => p.Value);
                uint bones = 0;
                var w = Vector4.Zero;
                for (int influence = 0; influence < top.Length; influence++) { bones |= top[influence].Key << (influence * 8); w[influence] = top[influence].Value / sum; }
                yield return new() { Position = Vector3.Lerp(a.Position + Delta(a), b.Position + Delta(b), t), Bones = bones, Weights = w };
            }
    }

    /// <summary>Intersects torso triangles, or every closed body contour in full-body mode, preserving weights and one interpolated body morph.</summary>
    public static Vertex[]? Build(Vertex[] source, uint[] groups, in NativeSectionCut cut, Vector4[]? morphs = null, int[]? components = null,
        uint bloodColour = NativeFluidPalette.DefaultBlood, bool brainSection = false, bool mechanical = false, float weldDistance = .0001f, float backingDepth = ExposureDepth + .35f
#if BRUTALITY_STOCK_MASTER
        , Vector4? corticalColour = null, Func<Vertex, Vertex>? bindBacking = null
#endif
        )
    {
        if (!TryNormalize(cut, out var normalized)) return null;
        var n = new Vector3(normalized.Plane.X, normalized.Plane.Y, normalized.Plane.Z);
        // Equivalent halfspaces must use the same section basis and triangulation, including reversed input planes.
        if (normalized.FullBody && (MathF.Abs(n.X) > 1e-6f ? n.X < 0 : MathF.Abs(n.Y) > 1e-6f ? n.Y < 0 : n.Z < 0))
        {
            normalized = normalized with { Plane = -normalized.Plane, KeepSide = -normalized.KeepSide };
            n = -n;
        }
        var points = new List<Point>();
        var byPosition = new Dictionary<Vector3, int>();
        var weld = new Dictionary<(int X, int Y, int Z), List<int>>();
        var adjacent = new List<HashSet<int>>();
        float weldScale = 1 / weldDistance;
        float weldDistanceSquared = weldDistance * weldDistance;
        int Add(Point point)
        {
            var cell = ((int)MathF.Floor(point.Position.X * weldScale), (int)MathF.Floor(point.Position.Y * weldScale), (int)MathF.Floor(point.Position.Z * weldScale));
            if (normalized.FullBody)
                for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                    if (weld.TryGetValue((cell.Item1 + x, cell.Item2 + y, cell.Item3 + z), out var candidates))
                        foreach (int candidate in candidates)
                            if (points[candidate].Component == point.Component && Vector3.DistanceSquared(points[candidate].Position, point.Position) <= weldDistanceSquared) return candidate;
            var key = Round(point.Position);
            if (!normalized.FullBody && byPosition.TryGetValue(key, out int existing)) return existing;
            int index = points.Count;
            points.Add(point); adjacent.Add([]); byPosition[key] = index;
            if (!weld.TryGetValue(cell, out var own)) weld[cell] = own = [];
            own.Add(index);
            return index;
        }
        Vector3 Delta(in Vertex vertex)
        {
            uint slot = BitConverter.SingleToUInt32Bits(vertex.Colour.X);
            return morphs is not null && slot > 0 && slot <= morphs.Length ? new(morphs[slot - 1].X, morphs[slot - 1].Y, morphs[slot - 1].Z) : default;
        }
        (int Component, uint Bone) Component(in Vertex a, in Vertex b, in Vertex c)
        {
            if (!normalized.FullBody || components is not { Length: > 0 }) return (-1, 0);
            var votes = new Dictionary<int, float>();
            foreach (var vertex in new[] { a, b, c })
                for (int k = 0; k < 4; k++)
                {
                    int bone = (int)(vertex.Bones >> (8 * k) & 255);
                    if (vertex.Weights[k] <= 0 || (uint)bone >= (uint)components.Length) continue;
                    int component = components[bone];
                    votes[component] = votes.GetValueOrDefault(component) + vertex.Weights[k];
                }
            if (votes.Count == 0) return (-1, 0);
            int selected = votes.OrderByDescending(v => v.Value).ThenBy(v => v.Key).First().Key;
            uint fallback = 0;
            float strongest = -1;
            foreach (var vertex in new[] { a, b, c })
                for (int k = 0; k < 4; k++)
                {
                    uint bone = vertex.Bones >> (8 * k) & 255;
                    if (bone < components.Length && components[bone] == selected && vertex.Weights[k] > strongest)
                        (fallback, strongest) = (bone, vertex.Weights[k]);
                }
            return (selected, fallback);
        }
        Point Edge(in Vertex a, in Vertex b, float t, int component, uint fallbackBone)
        {
            var da = Delta(a); var db = Delta(b);
            var point = normalized.HeadOnly
                ? Interpolate(new(a.Position + da, default, a.Bones, a.Weights), new(b.Position + db, default, b.Bones, b.Weights), t)
                : Interpolate(new(a.Position, da, a.Bones, a.Weights), new(b.Position, db, b.Bones, b.Weights), t);
            int region = Group(new Vertex { Bones = point.Bones, Weights = point.Weights }, groups);
            point = point with { Region = region == 6 ? 0 : region };
            if (component < 0 || components is null) return point;
            var weights = point.Weights;
            for (int k = 0; k < 4; k++)
            {
                uint bone = point.Bones >> (8 * k) & 255;
                if (bone >= components.Length || components[bone] != component) weights[k] = 0;
            }
            float sum = weights.X + weights.Y + weights.Z + weights.W;
            return sum > 1e-8f ? point with { Weights = weights / sum, Component = component }
                : point with { Bones = fallbackBone, Weights = Vector4.UnitX, Component = component };
        }
        for (int triangle = 0; triangle + 2 < source.Length; triangle += 3)
        {
            var a = source[triangle]; var b = source[triangle + 1]; var c = source[triangle + 2];
            float da = Distance(a), db = Distance(b), dc = Distance(c);
            if (MathF.Min(da, MathF.Min(db, dc)) > 1e-5f || MathF.Max(da, MathF.Max(db, dc)) < -1e-5f) continue;
            if (!normalized.FullBody && (Group(a, groups) != 0 || Group(b, groups) != 0 || Group(c, groups) != 0)) continue;
            var (component, fallbackBone) = Component(a, b, c);
            var hit = new List<int>(3);
            void Intersect(Vertex p, Vertex q, float dp, float dq)
            {
                if (MathF.Abs(dp) < 1e-5f) hit.Add(Add(Edge(p, q, 0, component, fallbackBone)));
                else if ((dp < 0) != (dq < 0)) hit.Add(Add(Edge(p, q, dp / (dp - dq), component, fallbackBone)));
            }
            Intersect(a, b, da, db); Intersect(b, c, db, dc); Intersect(c, a, dc, da);
            var unique = hit.Distinct().ToArray();
            if (unique.Length != 2 || unique[0] == unique[1]) continue;
            adjacent[unique[0]].Add(unique[1]); adjacent[unique[1]].Add(unique[0]);
        }
        float Distance(in Vertex vertex) =>
            Vector3.Dot(n, vertex.Position + (normalized.HeadOnly ? Delta(vertex) : default)) + normalized.Plane.W;
        if (points.Count < 3) return null;
        var reference = MathF.Abs(n.X) < 0.8f ? Vector3.UnitX : Vector3.UnitZ;
        var u = Vector3.Normalize(reference - n * Vector3.Dot(reference, n));
        var v = Vector3.Cross(n, u);
        var visited = new HashSet<int>();
        var rims = new List<(List<Point> Points, float Area)>();
        if (normalized.FullBody)
        {
            // Separate skin pieces can overlap: the installed Hiver head is an open neck tube inserted into
            // the closed torso. Split actual section-edge crossings so its visible head outline joins the body.
            Vector2 Project(int i) => new(Vector3.Dot(points[i].Position, u), Vector3.Dot(points[i].Position, v));
            var segments = Enumerable.Range(0, points.Count).SelectMany(a => adjacent[a].Where(b => a < b).Select(b => (A: a, B: b, From: Project(a), To: Project(b)))).ToArray();
            var shell = new int[points.Count];
            Array.Fill(shell, -1);
            int shellCount = 0;
            for (int start = 0; start < shell.Length; start++)
            {
                if (shell[start] >= 0) continue;
                var pending = new Stack<int>();
                pending.Push(start); shell[start] = shellCount;
                while (pending.TryPop(out int at))
                    foreach (int neighbour in adjacent[at])
                        if (shell[neighbour] < 0) { shell[neighbour] = shellCount; pending.Push(neighbour); }
                shellCount++;
            }
            var splits = segments.Select(s => new List<(float At, int Index)> { (0, s.A), (1, s.B) }).ToArray();
            static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
            static uint Regions(Point a, Point b) => a.Region < 0 || b.Region < 0 ? uint.MaxValue : 1u << a.Region | 1u << b.Region;
            bool crossed = false;
            for (int first = 0; first < segments.Length; first++)
                for (int second = first + 1; second < segments.Length; second++)
                {
                    var a = segments[first]; var b = segments[second];
                    if (points[a.A].Component != points[b.A].Component) continue;
                    // A wrist beside a hip can intersect the torso outline while sharing its articulated component.
                    // Their cap surfaces must remain separate when the carry pose moves that wrist away.
                    if (shell[a.A] != shell[b.A] && (Regions(points[a.A], points[a.B]) != 1 || Regions(points[b.A], points[b.B]) != 1)) continue;
                    if (a.A == b.A || a.A == b.B || a.B == b.A || a.B == b.B
                        || MathF.Max(a.From.X, a.To.X) + 1e-5f < MathF.Min(b.From.X, b.To.X) || MathF.Max(b.From.X, b.To.X) + 1e-5f < MathF.Min(a.From.X, a.To.X)
                        || MathF.Max(a.From.Y, a.To.Y) + 1e-5f < MathF.Min(b.From.Y, b.To.Y) || MathF.Max(b.From.Y, b.To.Y) + 1e-5f < MathF.Min(a.From.Y, a.To.Y)) continue;
                    var stepA = a.To - a.From; var stepB = b.To - b.From;
                    float denominator = Cross(stepA, stepB);
                    if (MathF.Abs(denominator) < 1e-10f) continue;
                    float ta = Cross(b.From - a.From, stepB) / denominator, tb = Cross(b.From - a.From, stepA) / denominator;
                    if (ta < -1e-5f || ta > 1.00001f || tb < -1e-5f || tb > 1.00001f) continue;
                    ta = Math.Clamp(ta, 0, 1); tb = Math.Clamp(tb, 0, 1);
                    int at = Add(Interpolate(points[a.A], points[a.B], ta));
                    splits[first].Add((ta, at)); splits[second].Add((tb, at));
                    crossed = true;
                }
            if (crossed)
            {
                foreach (var neighbours in adjacent) neighbours.Clear();
                foreach (var split in splits)
                {
                    var order = split.OrderBy(p => p.At).Select(p => p.Index).Distinct().ToArray();
                    for (int i = 1; i < order.Length; i++) { adjacent[order[i - 1]].Add(order[i]); adjacent[order[i]].Add(order[i - 1]); }
                }
            }
            // Open mouth/eyelid edges can touch a valid skin loop. Remove only their dangling chains, then
            // walk the bounded faces of the remaining planar graph; a shared seam need not have degree two.
            var dangling = new Queue<int>(Enumerable.Range(0, points.Count).Where(i => adjacent[i].Count < 2));
            while (dangling.TryDequeue(out int at))
            {
                foreach (int other in adjacent[at].ToArray())
                {
                    adjacent[other].Remove(at);
                    if (adjacent[other].Count == 1) dangling.Enqueue(other);
                }
                adjacent[at].Clear();
            }
            var edges = new HashSet<(int From, int To)>();
            for (int from = 0; from < points.Count; from++)
                foreach (int to in adjacent[from])
                {
                    if (edges.Contains((from, to))) continue;
                    var path = new List<int>();
                    int a = from, b = to;
                    do
                    {
                        if (!edges.Add((a, b))) break;
                        path.Add(a);
                        var reverse = points[a].Position - points[b].Position;
                        float incoming = MathF.Atan2(Vector3.Dot(reverse, v), Vector3.Dot(reverse, u));
                        int next = adjacent[b].Where(i => i != a).MinBy(i =>
                        {
                            var step = points[i].Position - points[b].Position;
                            float turn = incoming - MathF.Atan2(Vector3.Dot(step, v), Vector3.Dot(step, u));
                            return turn > 0 ? turn : turn + MathF.Tau;
                        });
                        a = b; b = next;
                    } while ((a != from || b != to) && path.Count <= points.Count * 2);
                    if (a != from || b != to || path.Count < 3) continue;
                    var centre = path.Aggregate(Vector3.Zero, (sum, i) => sum + points[i].Position) / path.Count;
                    float area = 0;
                    for (int i = 0; i < path.Count; i++) area += Vector3.Dot(Vector3.Cross(points[path[i]].Position - centre, points[path[(i + 1) % path.Count]].Position - centre), n);
                    if (area > 1e-4f) rims.Add((path.Select(i => points[i]).ToList(), area));
                }
        }
        for (int start = 0; start < points.Count; start++)
        {
            if (normalized.FullBody) break;
            if (!visited.Add(start)) continue;
            var component = new List<int>();
            var queue = new Queue<int>(); queue.Enqueue(start);
            while (queue.TryDequeue(out int next))
            {
                component.Add(next);
                foreach (int other in adjacent[next]) if (visited.Add(other)) queue.Enqueue(other);
            }
            if (component.Count < 3) continue;
            var centre = component.Aggregate(Vector3.Zero, (sum, i) => sum + points[i].Position) / component.Count;
            var ordered = component.Select(i => points[i]).OrderBy(p => MathF.Atan2(Vector3.Dot(p.Position - centre, v), Vector3.Dot(p.Position - centre, u))).ToList();
            float area = 0;
            for (int i = 0; i < ordered.Count; i++) area += Vector3.Dot(Vector3.Cross(ordered[i].Position - centre, ordered[(i + 1) % ordered.Count].Position - centre), n);
            if (MathF.Abs(area) < 1e-4f) continue;
            rims.Add((ordered, MathF.Abs(area)));
        }
        if (rims.Count == 0) return null;
        rims = rims.OrderByDescending(r => r.Area).Take(normalized.FullBody ? 128 : 1).ToList();
        if (brainSection) return BuildBrainSection(rims, normalized, n, u, v, bloodColour);
#if BRUTALITY_STOCK_MASTER
        if (corticalColour is { } cortex) return BuildCranialShell(rims, normalized, n, u, v, cortex);
#endif
        int total = rims.Sum(r => r.Points.Count), budget = normalized.FullBody ? 2048 : 512;
        var outward = n * -normalized.KeepSide;
        var result = new List<Vertex>();
        var sectionSkin = new SectionSkin(normalized.FullBody ? source.Select(point => point.Position + Delta(point)).ToArray() : [], n);
        int unprocessedRimPoints = total;
        for (int contour = 0; contour < rims.Count; contour++)
        {
        var rim = rims[contour].Points;
        unprocessedRimPoints -= rim.Count;
        int allotted = normalized.FullBody ? Math.Max(3, Math.Min(budget - 3 * (rims.Count - contour - 1), 2048 * rim.Count / total)) : 512;
        if (rim.Count > allotted)
        {
            var original = rim;
            rim = Enumerable.Range(0, allotted).Select(i => original[i * original.Count / allotted]).ToList();
        }
        budget -= rim.Count;
        var middle = rim.Aggregate(Vector3.Zero, (sum, p) => sum + p.Position) / rim.Count;
        var middleMorph = rim.Aggregate(Vector3.Zero, (sum, p) => sum + p.Morph) / rim.Count;
        var inset = normalized.FullBody ? Insets(rim, n, u, v) : null;
        var depths = inset is not null ? SectionDepths(rim, inset, n * normalized.KeepSide, sectionSkin, backingDepth) : null;
        Vertex At(int index, int band)
        {
            var point = rim[index];
            float radius = band switch { 0 => 1.005f, 1 => 0.90f, _ => 0.72f };
            float depth = band switch { 0 => Ragged(point.Position), 1 => 0.025f, _ => 0.20f };
            if (depths is not null && band == 3) depth = depths[index];
            else if (inset is not null && band > 0) depth = MathF.Min(depth, MathF.Max(0.01f, inset[index].Length() * 1.2f));
            var p = inset is null ? middle + (point.Position - middle) * radius + middleMorph + (point.Morph - middleMorph) * radius
                : point.Position + point.Morph + inset[index] * (band == 0 ? 0 : band == 1 ? 0.45f : 1);
            p += n * (depth * normalized.KeepSide);
            float grain = 0.90f + 0.10f * MathF.Sin(point.Position.X * 31 + point.Position.Z * 23);
            var colour = (band == 1 ? new Vector4(0.43f, 0.17f, 0.10f, 0) : new Vector4(0.29f, 0.018f, 0.025f, 0)) * grain;
            colour = mechanical
                ? (band == 1 ? new Vector4(0.37f, 0.40f, 0.39f, 0) : new Vector4(0.10f, 0.12f, 0.13f, 0)) * grain
                : NativeFluidPalette.Recolour(colour, bloodColour);
            return new() { Position = p, Normal = outward, Uv = new(band, contour), Tangent = new(p, Layer), Colour = colour, Bones = point.Bones, Weights = point.Weights };
        }
        void Triangle(Vertex a, Vertex b, Vertex c)
        {
            var cross = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);
            if (cross.LengthSquared() < 1e-12f) return;
            var normal = Vector3.Normalize(cross);
            if (Vector3.Dot(normal, outward) < 0) { (b, c) = (c, b); normal = -normal; }
            result.Add(a with { Normal = normal }); result.Add(b with { Normal = normal }); result.Add(c with { Normal = normal });
        }
        for (int i = 0; i < rim.Count; i++)
            for (int band = 0; band < 2; band++)
            {
                int next = (i + 1) % rim.Count;
                var a = At(i, band); var b = At(next, band); var c = At(i, band + 1); var d = At(next, band + 1);
                Triangle(a, b, c); Triangle(b, d, c);
            }
        if (normalized.FullBody)
        {
            var floor = Enumerable.Range(0, rim.Count).Select(i => At(i, 3)).ToArray();
#if BRUTALITY_STOCK_MASTER
            if (bindBacking is not null)
                for (int i = 0; i < floor.Length; i++) floor[i] = bindBacking(floor[i]);
#endif
            var triangles = SectionTriangles(floor, u, v);
            var outline = rim.Select(p => p.Position + p.Morph).ToArray();
            // Reserve only vertices that later contours can actually use.
            int reserve = 15 * Math.Min(budget, unprocessedRimPoints);
            AppendSectionBacking(floor, triangles, outline, normalized, sectionSkin, backingDepth, 32768 - result.Count - reserve, Triangle);
            // Reserve the remaining contours' flesh first, so a bone detail can never overflow the shared cap buffer.
            if (!mechanical) AppendLimbBoneSection(floor, triangles, outline, groups, normalized, u, v,
                32768 - result.Count - reserve, Triangle, bloodColour);
        }
        }
        if (normalized.FullBody) FitSectionBacking(result, normalized, sectionSkin, backingDepth);
        return result.Count > 0 ? result.ToArray() : null;
    }

    static Vector3[] Insets(List<Point> rim, Vector3 normal, Vector3 u, Vector3 v)
    {
        var polygon = rim.Select(p => new Vector2(Vector3.Dot(p.Position + p.Morph, u), Vector3.Dot(p.Position + p.Morph, v))).ToArray();
        static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
        bool Inside(Vector2 point)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
                if ((polygon[i].Y > point.Y) != (polygon[j].Y > point.Y)
                    && point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X) inside = !inside;
            return inside;
        }
        var result = new Vector3[rim.Count];
        for (int i = 0; i < rim.Count; i++)
        {
            int previous = (i + rim.Count - 1) % rim.Count, next = (i + 1) % rim.Count;
            var before = Vector3.Normalize(rim[i].Position + rim[i].Morph - rim[previous].Position - rim[previous].Morph);
            var after = Vector3.Normalize(rim[next].Position + rim[next].Morph - rim[i].Position - rim[i].Morph);
            var first = Vector3.Cross(normal, before);
            var inward = first + Vector3.Cross(normal, after);
            inward = inward.LengthSquared() > 1e-8f ? Vector3.Normalize(inward) : first;
            float amount = MathF.Min(0.28f, 0.14f / MathF.Max(0.5f, Vector3.Dot(inward, first)));
            var direction = new Vector2(Vector3.Dot(inward, u), Vector3.Dot(inward, v));
            var start = polygon[i] + direction * 0.0001f;
            for (int edge = 0; edge < polygon.Length; edge++)
            {
                int end = (edge + 1) % polygon.Length;
                if (edge == i || end == i) continue;
                var step = polygon[end] - polygon[edge];
                float denominator = Cross(direction, step);
                if (MathF.Abs(denominator) < 1e-8f) continue;
                float along = Cross(polygon[edge] - start, step) / denominator;
                float onEdge = Cross(polygon[edge] - start, direction) / denominator;
                if (along > 0.001f && onEdge is >= 0 and <= 1) amount = MathF.Min(amount, along * 0.30f);
            }
            while (amount > 0.0005f && !Inside(polygon[i] + direction * amount)) amount *= 0.5f;
            result[i] = amount > 0.0005f ? inward * amount : default;
        }
        bool InOrOn(Vector2 point)
        {
            if (Inside(point)) return true;
            for (int edge = 0; edge < polygon.Length; edge++)
            {
                var a = polygon[edge]; var step = polygon[(edge + 1) % polygon.Length] - a;
                float length = step.LengthSquared();
                float t = length < 1e-12f ? 0 : Math.Clamp(Vector2.Dot(point - a, step) / length, 0, 1);
                if (Vector2.DistanceSquared(point, a + step * t) < 1e-10f) return true;
            }
            return false;
        }
        bool EdgeInside(Vector2 a, Vector2 b)
        {
            if (!InOrOn(a) || !InOrOn(b) || !InOrOn((a + b) * 0.5f)) return false;
            var direction = b - a;
            for (int edge = 0; edge < polygon.Length; edge++)
            {
                var start = polygon[edge]; var step = polygon[(edge + 1) % polygon.Length] - start;
                float determinant = Cross(direction, step);
                if (MathF.Abs(determinant) < 1e-10f) continue;
                float along = Cross(start - a, step) / determinant;
                float crossing = Cross(start - a, direction) / determinant;
                if (along is > 0.00001f and < 0.99999f && crossing is >= 0 and <= 1) return false;
            }
            return true;
        }
        Vector2 Shifted(int index) => polygon[index] + new Vector2(Vector3.Dot(result[index], u), Vector3.Dot(result[index], v));
        // Individually valid inset vertices can still connect across a concave throat or hip gap. Keep every inset edge inside its own original loop.
        for (int pass = 0; pass < 20; pass++)
        {
            bool changed = false;
            for (int i = 0; i < rim.Count; i++)
            {
                int next = (i + 1) % rim.Count;
                if (EdgeInside(Shifted(i), Shifted(next))) continue;
                result[i] *= 0.5f; result[next] *= 0.5f;
                if (result[i].LengthSquared() < 1e-12f) result[i] = default;
                if (result[next].LengthSquared() < 1e-12f) result[next] = default;
                changed = true;
            }
            if (!changed) break;
        }
        return result;
    }

}
