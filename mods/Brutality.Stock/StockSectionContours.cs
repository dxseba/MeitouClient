using Meitou.Game.Rendering.Frame;
using System.Numerics;
using Meitou.Brutality.Rendering;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;

namespace Meitou.Brutality;

/// <summary>Reads the actual weighted boundary of a planar cut from the skin that will be drawn.</summary>
internal static class StockSectionContours
{
    const float PlaneTolerance = .001f;
    const float WeldDistance = .002f;

    readonly record struct Edge(int A, int B)
    {
        internal static Edge Of(int a, int b) => a < b ? new(a, b) : new(b, a);
    }

    internal static IReadOnlyList<Vertex[]> Read(OgreMesh clippedSkin, SkeletonRig rig, CharacterAppearance look, in CutHalfspace cut)
    {
        var normal = new Vector3(cut.Plane.X, cut.Plane.Y, cut.Plane.Z);
        float length = normal.Length();
        if (!float.IsFinite(length) || length < 1e-5f) return [];
        normal /= length;
        float offset = cut.Plane.W / length;
        int group = cut.Group;
        var groups = group >= 0 ? MeshCut.BoneGroups(rig) : [];
        bool morphedDistance = cut.MorphedDistance;
        bool shaved = look.Character?.GetBool("shaved") == true;
        var nodes = new List<Vertex>();
        var cells = new Dictionary<(int X, int Y, int Z), List<int>>();
        var uses = new Dictionary<Edge, (int Direction, bool Cut)>();
        foreach (var part in MeshCut.SourceModel(clippedSkin, rig.BoneCount).Parts)
        {
            var vertices = part.Vertices;
            var morphs = MeshCut.MorphDeltas(clippedSkin, part.SubMeshIndex, vertices.Length, look.PoseWeights, shaved);
            var indices = part.Indices;
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                uint a = indices[i], b = indices[i + 1], c = indices[i + 2];
                if (a >= vertices.Length || b >= vertices.Length || c >= vertices.Length) continue;
                bool onA = OnPlane(a), onB = OnPlane(b), onC = OnPlane(c);
                if ((onA ? 1 : 0) + (onB ? 1 : 0) + (onC ? 1 : 0) < 2) continue;
                bool crossing = !(onA && onB && onC);
                bool matches = crossing && (group < 0 || InGroup(vertices[a]) || InGroup(vertices[b]) || InGroup(vertices[c]));
                if (onA && onB) Add(a, b);
                if (onB && onC) Add(b, c);
                if (onC && onA) Add(c, a);

                void Add(uint first, uint second)
                {
                    int x = Node(vertices[first] with { Position = vertices[first].Position + morphs[first] });
                    int y = Node(vertices[second] with { Position = vertices[second].Position + morphs[second] });
                    if (x == y) return;
                    var edge = Edge.Of(x, y);
                    var prior = uses.GetValueOrDefault(edge);
                    uses[edge] = (prior.Direction + (x < y ? 1 : -1), prior.Cut || matches);
                }
            }

            bool OnPlane(uint i)
            {
                var point = vertices[i].Position + (morphedDistance ? morphs[i] : Vector3.Zero);
                return MathF.Abs(Vector3.Dot(point, normal) + offset) <= PlaneTolerance;
            }
        }

        var links = new Dictionary<int, List<int>>();
        var available = new HashSet<Edge>();
        foreach (var (edge, use) in uses)
        {
            // Paired edges are internal triangulation and UV/material seams, not an opening.
            if (use.Direction == 0 || !use.Cut) continue;
            available.Add(edge);
            Link(edge.A, edge.B);
            Link(edge.B, edge.A);
        }
        var u = Vector3.Normalize(Vector3.Cross(MathF.Abs(normal.Y) < .9f ? Vector3.UnitY : Vector3.UnitX, normal));
        var v = Vector3.Cross(normal, u);
        var rings = new List<(Vertex[] Vertices, Vector2[] Plane, float Area)>();
        foreach (var (start, neighbors) in links.OrderBy(link => link.Value.Count != 1))
            foreach (int next in neighbors)
            {
                var edge = Edge.Of(start, next);
                if (!available.Remove(edge)) continue;
                var chain = new List<int> { start };
                var visited = new HashSet<int> { start };
                float perimeter = Vector3.Distance(nodes[start].Position, nodes[next].Position);
                int previous = start, current = next;
                while (current != start && visited.Add(current))
                {
                    chain.Add(current);
                    int following = Next(previous, current);
                    if (following < 0) break;
                    var step = Edge.Of(current, following);
                    available.Remove(step);
                    perimeter += Vector3.Distance(nodes[current].Position, nodes[following].Position);
                    (previous, current) = (current, following);
                }
                if (chain.Count < 3) continue;
                // A split UV seam can leave the loop slightly open. Only bridge a small gap
                // between the two ends of this same chain, never between unrelated sections.
                if (current != start && (chain.Count < 5 || current != chain[^1]
                    || Vector3.Distance(nodes[start].Position, nodes[current].Position) > Math.Min(.045f, perimeter * .035f))) continue;
                var ring = chain.Select(index => nodes[index]).ToArray();
                var projected = ring.Select(vertex => new Vector2(Vector3.Dot(vertex.Position, u), Vector3.Dot(vertex.Position, v))).ToArray();
                float area = 0;
                for (int i = 0; i < projected.Length; i++)
                {
                    var a = projected[i] - projected[0];
                    var b = projected[(i + 1) % projected.Length] - projected[0];
                    area += a.X * b.Y - a.Y * b.X;
                }
                if (MathF.Abs(area) > 1e-6f) rings.Add((ring, projected, MathF.Abs(area)));
            }
        var outer = new List<(Vertex[] Vertices, Vector2[] Plane, float Area)>();
        foreach (var ring in rings.OrderByDescending(ring => ring.Area))
            if (!outer.Any(larger => ring.Plane.All(point => Contains(larger.Plane, point)))) outer.Add(ring);
        return outer.Select(ring => ring.Vertices).ToArray();

        bool InGroup(in Vertex vertex)
        {
            float weight = 0;
            for (int i = 0; i < 4; i++)
            {
                int bone = (int)(vertex.Bones >> (i * 8) & 255);
                if ((uint)bone < (uint)groups.Length && groups[bone] == group)
                    weight += vertex.Weights[i];
            }
            return weight >= .12f;
        }

        int Node(in Vertex vertex)
        {
            var p = vertex.Position;
            int x = (int)MathF.Floor(p.X / WeldDistance), y = (int)MathF.Floor(p.Y / WeldDistance), z = (int)MathF.Floor(p.Z / WeldDistance);
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                        if (cells.TryGetValue((x + dx, y + dy, z + dz), out var candidates))
                            foreach (int candidate in candidates)
                                if (Vector3.DistanceSquared(nodes[candidate].Position, p) <= WeldDistance * WeldDistance) return candidate;
            if (!cells.TryGetValue((x, y, z), out var cell)) cells.Add((x, y, z), cell = []);
            int index = nodes.Count;
            nodes.Add(vertex);
            cell.Add(index);
            return index;
        }

        void Link(int from, int to)
        {
            if (!links.TryGetValue(from, out var neighbors)) links.Add(from, neighbors = []);
            neighbors.Add(to);
        }

        int Next(int previous, int current)
        {
            int result = -1;
            float best = float.NegativeInfinity;
            var incoming = Vector3.Normalize(nodes[current].Position - nodes[previous].Position);
            foreach (int neighbor in links[current])
            {
                if (!available.Contains(Edge.Of(current, neighbor))) continue;
                float score = Vector3.Dot(incoming, Vector3.Normalize(nodes[neighbor].Position - nodes[current].Position));
                if (score <= best) continue;
                best = score;
                result = neighbor;
            }
            return result;
        }
    }

    static bool Contains(Vector2[] polygon, Vector2 point)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            var a = polygon[i];
            var b = polygon[j];
            if ((a.Y > point.Y) != (b.Y > point.Y)
                && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }
}
