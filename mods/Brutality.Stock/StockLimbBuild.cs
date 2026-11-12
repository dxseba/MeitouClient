using Meitou.Game.Rendering.Frame;
using System.Numerics;
using Meitou.Brutality.Physics;
using Meitou.Brutality.Rendering;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Data.Ogre;
using Meitou.Data.Physics;

namespace Meitou.Brutality;

/// <summary>Clips the natural stock mesh and shared-skeleton clothing with the original limb cut plane.</summary>
internal static class StockLimbBuild
{
    static readonly bool trace = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_TRACE_HITS") == "1";
    internal sealed record Look(OgreMesh Body, IReadOnlyList<(OgreMesh Mesh, string Label)> Tissue,
        IReadOnlyList<(CharacterPart Part, OgreMesh? Mesh)> Equipment, int HiddenLimbs);
    internal sealed record Detached(StockLimbRules.Request Cut, Vector4 Plane, Look Look, RagdollDefinition Definition);
    internal sealed record Prepared(SkeletonRig Rig, Look Remaining, IReadOnlyList<Detached> NewPieces);
    readonly record struct Section(StockLimbRules.Request Cut, Vector4 Plane)
    {
        public int Slot => Cut.Shape.LimbSlot;
        public CutHalfspace Rule(int side) => new(Plane, side, Slot + 1, RejectOtherGroups: side < 0, MorphedDistance: true);
    }

    internal static Prepared? Build(CharacterRigs rigs, CharacterAppearance look, StockLimbRules.Request[] cuts,
        HashSet<(int Slot, long Tick)> prior, CancellationToken cancellation)
    {
        if (look.Animal is not null || cuts.Length == 0 || rigs.FindMesh(look.BodyMesh) is not { } path) return null;
        cancellation.ThrowIfCancellationRequested();
        var body = OgreMeshReader.ReadFile(path);
        if (body.SkeletonName is not { } skeleton || rigs.Rig(skeleton) is not { } rig) return null;
        var sections = new List<Section>(cuts.Length);
        int mask = 0;
        foreach (var cut in cuts)
        {
            if (!LimbCutGeometry.TryPlane(rig, cut.Shape, out var plane)) return null;
            sections.Add(new(cut, plane));
            mask |= 1 << cut.Shape.LimbSlot;
        }
        bool robot = look.Race.GetBool("is robot");
        bool shaved = look.Character?.GetBool("shaved") == true;
        var palette = StockTissuePalette.For(look);
        var flesh = robot ? new Vector4(.28f, .31f, .32f, 1) : palette.Flesh;
        var groups = MeshCut.BoneGroups(rig);
        var retained = sections.Select(section => section.Rule(1)).ToArray();
        var kept = MeshCut.Clip(body, rig, retained, look.PoseWeights, shaved, flesh, buildCaps: robot);
        if (!kept.HasTriangles) return null;
        var keptMap = Identity(rig);
        foreach (var section in sections) MapWeights(rig, groups, section, side: 1, keptMap);
        int hidden = look.HiddenLimbs & ~mask;
        var partSources = new Dictionary<string, OgreMesh?>(StringComparer.OrdinalIgnoreCase);
        var remaining = MakeLook(kept, retained, hidden, keptMap, detached: false);
        var retainedTissue = new List<Vertex>();
        var pieces = new List<Detached>();
        RagdollDefinition? physics = null;
        OgreMesh? mechanics = null;
        foreach (var section in sections)
        {
            cancellation.ThrowIfCancellationRequested();
            CutHalfspace[] rules = [section.Rule(-1)];
            var skin = MeshCut.Clip(body, rig, rules, look.PoseWeights, shaved, flesh, buildCaps: robot);
            if (!skin.HasTriangles) return null;
            var contour = robot && skin.Wound is null ? SectionContour(skin.Skin, rig, look, section.Plane) : [];
            if (!robot)
            {
                if (!AppendOrganic(retainedTissue, remaining.Body, section, 1, keptMap))
                    remaining = FallbackOrganic(remaining, section.Rule(1), keptMap);
            }
            else if (contour.Count >= 3) remaining = AddCap(remaining, contour, section.Rule(1), keptMap);
            if (prior.Contains((section.Slot, section.Cut.Tick))) continue;
            physics ??= LoadDefinition(rigs, look, rig, robot);
            if (physics is null || RagdollSeverance.Split(physics, section.Slot, section.Plane).Detached is not { } definition)
                return null;
            var map = Identity(rig);
            MapWeights(rig, groups, section, side: -1, map);
            var distal = MakeLook(skin, rules, hidden, map, detached: true);
            if (!robot)
            {
                var detailed = new List<Vertex>();
                distal = AppendOrganic(detailed, distal.Body, section, -1, map)
                    ? AddDetailed(distal, detailed)
                    : FallbackOrganic(distal, section.Rule(-1), map);
            }
            if (robot)
            {
                if (contour.Count >= 3) distal = AddCap(distal, contour, section.Rule(-1), map);
                mechanics ??= MechanicalInterior(body, rig, look.Headless);
                if (mechanics is not null)
                {
                    var shell = MeshCut.Clip(mechanics, rig, [section.Rule(-1), new(section.Plane with { W = section.Plane.W + 1.15f })],
                        buildCaps: false);
                    if (shell.HasTriangles)
                    {
                        Remap(shell.Skin, map);
                        distal = distal with { Tissue = [.. distal.Tissue, (shell.Skin, "mechanical interior")] };
                    }
                }
            }
            pieces.Add(new(section.Cut, section.Plane, distal, definition));
        }
        return new(rig, AddDetailed(remaining, retainedTissue), pieces);

        bool AppendOrganic(List<Vertex> detail, OgreMesh clippedSkin, Section section, int side, int[] map)
        {
            var rule = section.Rule(side);
            int bone = MeshCut.SectionBone(rig, rule);
            if (bone < 0) return false;
            int start = detail.Count;
            foreach (var contour in StockSectionContours.Read(clippedSkin, rig, look, rule))
                StockLimbSections.Append(detail, contour, rig, section.Cut.Shape, section.Plane, side, map[bone], palette);
            if (trace) Console.WriteLine($"Brutality stock limb section slot={section.Slot} side={side} triangles={(detail.Count - start) / 3}");
            return detail.Count != start;
        }

        Look FallbackOrganic(Look appearance, CutHalfspace rule, int[] map)
        {
            var clipped = MeshCut.Clip(body, rig, [rule], look.PoseWeights, shaved, flesh, capWeldDistance: .002f);
            return clipped.Wound is { } cap ? AddTissue(appearance, cap, map, "fracture surface") : appearance;
        }

        Look AddDetailed(Look appearance, List<Vertex> vertices)
        {
            if (vertices.Count == 0) return appearance;
            var mesh = MeshCut.FromTriangles(vertices, skeleton);
            OgreMeshWeld.Apply(mesh);
            return appearance with { Tissue = [.. appearance.Tissue, (mesh, "muscle and bone section")] };
        }

        Look AddTissue(Look appearance, OgreMesh cap, int[] map, string label)
        {
            Remap(cap, map);
            OgreMeshWeld.Apply(cap);
            return appearance with { Tissue = [.. appearance.Tissue, (cap, label)] };
        }

        Look AddCap(Look appearance, IReadOnlyList<Vector3> contour, CutHalfspace rule, int[] weights)
        {
            int bone = MeshCut.SectionBone(rig, rule);
            if (bone < 0) return appearance;
            var cap = CloseContour(contour, rule, flesh, weights[bone], skeleton);
            OgreMeshWeld.Apply(cap);
            return appearance with { Tissue = [.. appearance.Tissue, (cap, robot ? "mechanical section" : "fracture surface")] };
        }

        Look MakeLook(CutMeshResult skin, CutHalfspace[] rules, int limbFlags, int[] map, bool detached)
        {
            Remap(skin.Skin, map);
            var tissue = new List<(OgreMesh Mesh, string Label)>(1);
            if (skin.Wound is { } cap)
            {
                Remap(cap, map);
                OgreMeshWeld.Apply(cap);
                tissue.Add((cap, robot ? "mechanical section" : "fracture surface"));
            }
            var equipment = new List<(CharacterPart Part, OgreMesh? Mesh)>();
            foreach (var part in look.Parts)
            {
                cancellation.ThrowIfCancellationRequested();
                if (part.Record.Type == FcsRecordType.LimbReplacement
                    && part.Record.GetInt("slot") - 50 is >= 0 and <= 3 and var slot && (mask & (1 << slot)) != 0) continue;
                if (part.Mode == AttachMode.Bone)
                {
                    if (!detached && IsOnBody(look, rig, groups, part, rules)) equipment.Add((part, null));
                    continue;
                }
                // Hair, a hat and the other limbs are independent of this local cut.
                if (part.Slot is AttachSlot.Hair or AttachSlot.Beard or AttachSlot.Hat or AttachSlot.Eyes or AttachSlot.Neck)
                {
                    if (!detached) equipment.Add((part, null));
                    continue;
                }
                if (!partSources.TryGetValue(part.Mesh, out var source))
                    partSources[part.Mesh] = source = ReadPart(rigs, part.Mesh);
                if (source is null) continue;
                var clipped = MeshCut.Clip(source, rig, rules, buildCaps: false);
                if (!clipped.HasTriangles) continue;
                Remap(clipped.Skin, map);
                equipment.Add((part, clipped.Skin));
            }
            return new(skin.Skin, tissue, equipment, limbFlags);
        }
    }

    static IReadOnlyList<Vector3> SectionContour(OgreMesh mesh, SkeletonRig rig, CharacterAppearance look, Vector4 plane)
    {
        var normal = new Vector3(plane.X, plane.Y, plane.Z);
        var u = Vector3.Normalize(Vector3.Cross(MathF.Abs(normal.Y) < .9f ? Vector3.UnitY : Vector3.UnitX, normal));
        var v = Vector3.Cross(normal, u);
        var seam = new Dictionary<(int X, int Y), (Vector2 Projected, Vector3 Point)>();
        bool shaved = look.Character?.GetBool("shaved") == true;
        foreach (var part in MeshCut.SourceModel(mesh, rig.BoneCount).Parts)
        {
            var deltas = MeshCut.MorphDeltas(mesh, part.SubMeshIndex, part.Vertices.Length, look.PoseWeights, shaved);
            for (int i = 0; i < part.Vertices.Length; i++)
            {
                var point = part.Vertices[i].Position + deltas[i];
                float distance = Vector3.Dot(point, normal) + plane.W;
                if (MathF.Abs(distance) > .025f) continue;
                point -= normal * distance;
                var projected = new Vector2(Vector3.Dot(point, u), Vector3.Dot(point, v));
                seam.TryAdd(((int)MathF.Round(projected.X * 500), (int)MathF.Round(projected.Y * 500)), (projected, point));
            }
        }
        if (seam.Count < 3) return [];
        var sorted = seam.Values.OrderBy(pair => pair.Projected.X).ThenBy(pair => pair.Projected.Y).ToArray();
        var hull = new List<(Vector2 Projected, Vector3 Point)>();
        foreach (var p in sorted) Append(p, 0);
        int lower = hull.Count;
        for (int i = sorted.Length - 2; i >= 0; i--) Append(sorted[i], lower - 1);
        hull.RemoveAt(hull.Count - 1);
        return hull.Select(pair => pair.Point).ToArray();

        void Append((Vector2 Projected, Vector3 Point) point, int minimum)
        {
            while (hull.Count >= minimum + 2)
            {
                var a = hull[^1].Projected - hull[^2].Projected;
                var b = point.Projected - hull[^1].Projected;
                if (a.X * b.Y - a.Y * b.X > 1e-6f) break;
                hull.RemoveAt(hull.Count - 1);
            }
            hull.Add(point);
        }
    }

    static OgreMesh CloseContour(IReadOnlyList<Vector3> contour, CutHalfspace rule, Vector4 flesh, int bone, string skeleton)
    {
        var kept = new Vector3(rule.Plane.X, rule.Plane.Y, rule.Plane.Z) * rule.KeepSide;
        var centre = contour.Aggregate(Vector3.Zero, (sum, point) => sum + point) / contour.Count;
        var floor = Sample(centre + kept * .17f, .45f);
        var vertices = new List<Vertex>(contour.Count * 9);
        for (int i = 0; i < contour.Count; i++)
        {
            var p = contour[i];
            var q = contour[(i + 1) % contour.Count];
            var a = Sample(p + kept * .012f, 1);
            var b = Sample(q + kept * .012f, 1);
            var c = Sample(Vector3.Lerp(centre, p, .78f) + kept * .11f, .67f);
            var d = Sample(Vector3.Lerp(centre, q, .78f) + kept * .11f, .67f);
            Add(a, b, c); Add(b, d, c); Add(c, d, floor);
        }
        return MeshCut.FromTriangles(vertices, skeleton);

        Vertex Sample(Vector3 point, float shade) => new()
        {
            Position = point, Normal = -kept, Colour = (flesh * shade) with { W = 1 },
            Bones = (uint)bone, Weights = Vector4.UnitX,
        };

        void Add(Vertex a, Vertex b, Vertex c)
        {
            if (Vector3.Dot(Vector3.Cross(b.Position - a.Position, c.Position - a.Position), -kept) < 0) (b, c) = (c, b);
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
        }
    }

    static int[] Identity(SkeletonRig rig) => Enumerable.Range(0, rig.BoneCount).ToArray();

    static void MapWeights(SkeletonRig rig, int[] groups, Section section, int side, int[] map)
    {
        int group = section.Slot + 1;
        var normal = new Vector3(section.Plane.X, section.Plane.Y, section.Plane.Z);
        var kept = new List<int>();
        for (int bone = 0; bone < groups.Length && bone <= byte.MaxValue; bone++)
            if (groups[bone] == group && (Vector3.Dot(rig.BindPosition[bone], normal) + section.Plane.W) * side >= -.0001f)
                kept.Add(bone);
        if (kept.Count == 0) return;
        for (int bone = 0; bone < groups.Length; bone++)
        {
            if ((side > 0 && groups[bone] != group) || kept.Contains(bone)) continue;
            int nearest = kept[0];
            float best = float.PositiveInfinity;
            foreach (int candidate in kept)
            {
                float distance = Vector3.DistanceSquared(rig.BindPosition[bone], rig.BindPosition[candidate]);
                if (distance < best) (nearest, best) = (candidate, distance);
            }
            map[bone] = nearest;
        }
    }

    static void Remap(OgreMesh mesh, int[] map)
    {
        // All changed meshes are MeshCut output, with assignments ordered by vertex and no shared vertex table.
        foreach (var part in mesh.SubMeshes)
            for (int i = 0; i < part.BoneAssignments.Count; i++)
            {
                var weight = part.BoneAssignments[i];
                if (weight.BoneIndex < map.Length && map[weight.BoneIndex] != weight.BoneIndex)
                    part.BoneAssignments[i] = weight with { BoneIndex = (ushort)map[weight.BoneIndex] };
            }
    }

    static bool IsOnBody(CharacterAppearance look, SkeletonRig rig, int[] groups, CharacterPart part, CutHalfspace[] rules)
    {
        if (part.Point is not { } point || !look.AttachmentPoints.TryGetValue(point, out var binding)
            || rig.Handle(binding.Bone) is not { } bone) return true;
        // A weapon in the hand is still handled by the native inventory when the hand is lost.
        return rules.All(rule => rule.Band(rig.BindPosition[bone], groups[bone]) >= 0);
    }

    static OgreMesh? ReadPart(CharacterRigs rigs, string mesh)
    {
        if (rigs.FindMesh(mesh) is not { } path) return null;
        try { return OgreMeshReader.ReadFile(path); }
        catch (Exception error) when (error is OgreFormatException or IOException or InvalidDataException or UnauthorizedAccessException) { return null; }
    }

    static RagdollDefinition? LoadDefinition(CharacterRigs rigs, CharacterAppearance look, SkeletonRig rig, bool robot)
    {
        string file = look.Race.GetPath(look.Female ? "female ragdoll" : "male ragdoll");
        if (string.IsNullOrWhiteSpace(file)) file = look.Race.GetPath("male ragdoll");
        if (rigs.Find(file) is not { } path) return null;
        var definition = RagdollDefinition.Build(PhysicsFile.ReadFile(path), rig, 1, Hull);
        return robot && definition is not null ? RobotRagdollWeight.Weight(definition) : definition;

        Vector3[]? Hull(string name) => rigs.Find(name) is { } mesh ? CookedMeshReader.ReadConvex(File.ReadAllBytes(mesh)).Vertices : null;
    }

    static OgreMesh? MechanicalInterior(OgreMesh body, SkeletonRig rig, bool headless)
    {
        var source = MeshCut.SourceModel(body, rig.BoneCount).Parts.SelectMany(part => part.Vertices).ToArray();
        if (MechanicalAnatomy.Build(rig, source, [], headless) is not { } vertices) return null;
        for (int i = 0; i < vertices.Count; i++)
        {
            var vertex = vertices[i];
            vertices[i] = vertex with { Colour = vertex.Colour with { W = 1 } };
        }
        return MeshCut.FromTriangles(vertices, body.SkeletonName);
    }
}
