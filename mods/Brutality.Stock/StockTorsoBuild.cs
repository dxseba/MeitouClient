using Meitou.Game.Rendering.Frame;
using System.Numerics;
using Meitou.Brutality.Anatomy;
using Meitou.Brutality.Physics;
using Meitou.Brutality.Rendering;
using Meitou.Brutality.Rendering.Surface;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;

namespace Meitou.Brutality;

/// <summary>Builds both stock character appearances from the same bind-space section on the cut worker.</summary>
internal static class StockTorsoBuild
{
    internal sealed record Prior(StockLimbRules.Request[] Limbs, StockWoundRules.View? Wounds);
    internal sealed record Cut(SkeletonRig Rig, Vector4 Plane, IReadOnlyList<Piece> Pieces);

    internal sealed record Piece(int Side, OgreMesh Body, IReadOnlyList<(OgreMesh Mesh, string Label)> Tissue,
        IReadOnlyList<(CharacterPart Part, OgreMesh? Mesh)> Equipment, Vector3 Min, Vector3 Max, bool[] KeptBones,
        int? HiddenLimbs = null, bool ShowHead = false)
    {
        internal Vector3 Centre => (Min + Max) * .5f;
        internal Vector3 HalfExtent => (Max - Min) * .5f;
    }

    // Morphs can move the matching endpoints of two source shells a fraction of a millimetre apart.
    const float SectionWeldDistance = .002f;
    // Only the anatomy near the opening needs to be drawn.
    const float OrganicInteriorDepth = 1.02f, MechanicalInteriorDepth = 1.5f;
    static readonly Vector3 AnatomyPelvis = new(-.0446148f, 9.956298f, -.227702f);
    static readonly object anatomyLock = new();
    static (string Path, Lazy<IReadOnlyList<AnatomyBatch>> Batches)? anatomy;

    internal static StockTorsoPhysics.Prepared? Build(string folder, CharacterRigs rigs, CharacterAppearance look, TorsoBisectionState cut,
        CancellationToken cancellation, Prior? prior, StockTorsoSave.Pose? saved)
    {
        if (!cut.IsValid || look.Animal is not null || rigs.FindMesh(look.BodyMesh) is not { } path) return null;
        cancellation.ThrowIfCancellationRequested();
        var body = OgreMeshReader.ReadFile(path);
        if (body.SkeletonName is not { } skeleton || rigs.Rig(skeleton) is not { } rig
            || rig.Handle("Bip01 Pelvis") is null || rig.Handle("Bip01 Neck") is null) return null;

        // Build retained injuries from their immutable states on the worker, even if their standalone looks were evicted.
        var naturalBody = body;
        IReadOnlyList<(CharacterPart Part, OgreMesh? Mesh)> sourceEquipment = look.Parts.Select(part => (part, (OgreMesh?)null)).ToArray();
        var sourceTissue = new List<(OgreMesh Mesh, string Label)>();
        int? hiddenLimbs = null;
        bool showHead = false;
        if (prior is { Limbs.Length: > 0 })
        {
            var existing = prior.Limbs.Select(limb => (limb.Shape.LimbSlot, limb.Tick)).ToHashSet();
            if (StockLimbBuild.Build(rigs, look, prior.Limbs, existing, cancellation)?.Remaining is { } remaining)
            {
                body = remaining.Body;
                sourceEquipment = remaining.Equipment;
                sourceTissue.AddRange(remaining.Tissue);
                hiddenLimbs = remaining.HiddenLimbs;
            }
        }
        CutVolume? punchVolume = null;
        Vector4? attachedHeadPlane = null;
        if (prior?.Wounds is { } wounds)
        {
            if (!look.Headless && wounds.AttachedHead is not null) wounds = wounds with { AttachedHead = null };
            if (StockWoundBuild.BuildRetained(folder, rigs, look, wounds, body, rig, sourceEquipment, cancellation, out punchVolume) is { } composed)
            {
                if (wounds.AttachedHead is { Mode: HeadCutMode.AttachedSplit } attached)
                    attachedHeadPlane = StockWoundBuild.HeadPlane(body, rig, look, attached.Plane);
                body = composed.Body ?? body;
                sourceEquipment = composed.Equipment;
                sourceTissue.AddRange(composed.Tissue);
                showHead = wounds.AttachedHead is not null;
            }
            else punchVolume = null;
        }
        if (StockTorsoPhysics.BuildDefinitions(rigs, look, rig, cut, naturalBody, body,
                hiddenLimbs is null ? null : prior, attachedHeadPlane, saved, cancellation) is not { } definitions) return null;
        CutVolume[]? openTunnel = punchVolume is null ? null : [punchVolume with { Group = -1 }];

        var groups = MeshCut.BoneGroups(rig);
        bool shaved = look.Character?.GetBool("shaved") == true;
        bool robot = look.Race.GetBool("is robot");
        var palette = StockTissuePalette.For(look);
        var flesh = robot ? new Vector4(.28f, .31f, .32f, 1) : palette.Flesh;
        var sectionSkin = robot ? null : SectionSkin(body, rig, look, shaved);
        var interior = robot
            ? RobotInterior(body, rig, cut.Plane, look.Headless, cancellation)
            : OrganicInterior(folder, rig, skeleton, cut.Plane, palette, new StockTorsoMuscle(sectionSkin!, groups), cancellation);
        var equipmentSources = new Dictionary<string, OgreMesh?>(StringComparer.OrdinalIgnoreCase);
        var pieces = new List<Piece>(2);
        // Organic full-body cuts can cross overlapping neck and torso shells. The layered section builder
        // extracts their bounded faces before adding recessed backing, instead of requiring a closed edge chain.
        var packedGroups = cut.FullBody && sectionSkin is not null ? new uint[(rig.BoneCount + 7) / 8] : [];
        if (packedGroups.Length > 0)
            for (int bone = 0; bone < groups.Length; bone++) packedGroups[bone / 8] |= (uint)groups[bone] << (bone % 8 * 4);
        foreach (int side in new[] { 1, -1 })
        {
            cancellation.ThrowIfCancellationRequested();
            var section = new CutHalfspace(cut.Plane, side, LegacyTorso: !cut.FullBody, MorphedDistance: true);
            var kept = KeptBones(rig, groups, section);
            var definition = side > 0 ? definitions.Positive : definitions.Negative;
            var components = cut.FullBody && !robot ? AdvancedFragmentScene.StockBoneComponents(definition, rig) : null;
            if (components is not null)
                foreach (var actor in definition.Bodies)
                    if (actor.Bone <= byte.MaxValue) kept[actor.Bone] = true;
            var fallback = NearestKeptBones(rig, groups, kept, components);
            if (fallback is null) return null;
            var backing = components is not null && sectionSkin is not null
                ? OrganicSection(sectionSkin, packedGroups, cut.Plane, side, palette, skeleton, kept, fallback, components) : null;
            var skin = MeshCut.Clip(body, rig, [section], look.PoseWeights, shaved, flesh, buildCaps: backing is null,
                capWeldDistance: robot ? 0 : SectionWeldDistance, shareSourceVertices: true, components: backing is null ? components : null);
            if (!robot && backing is null && skin.HasTriangles && skin.Wound is null)
                backing = MeshCut.Clip(body, rig, [section], look.PoseWeights, shaved, flesh, components: components).Wound;
            if (!skin.HasTriangles) return null;
            var tissue = new List<(OgreMesh Mesh, string Label)>(2);
            if ((backing ?? skin.Wound) is { } cap) AddTissue(cap, robot ? "mechanical section" : "fracture surface", maskPunch: true);
            if (interior is not null)
            {
                var slab = cut.Plane with { W = cut.Plane.W - side * OrganicInteriorDepth };
                CutHalfspace[] interiorBounds = robot ? [section] : [section, new(slab, -side)];
                var lining = MeshCut.Clip(interior, rig, interiorBounds, removeVolumes: openTunnel, buildCaps: false);
                if (lining.HasTriangles) AddTissue(lining.Skin, robot ? "mechanical interior" : "anatomy section");
            }
            if (cut.FullBody && !robot && !look.Headless)
                foreach (var (mesh, label) in StockHeadAnatomy.BodySection(folder, naturalBody, rig, look, section, cancellation)) AddTissue(mesh, label);
            foreach (var (mesh, label) in sourceTissue)
            {
                cancellation.ThrowIfCancellationRequested();
                var clipped = MeshCut.Clip(mesh, rig, [section], buildCaps: false);
                if (clipped.HasTriangles) AddTissue(clipped.Skin, label);
            }
            var equipment = new List<(CharacterPart Part, OgreMesh? Mesh)>(sourceEquipment.Count);
            foreach (var (part, existingMesh) in sourceEquipment)
            {
                cancellation.ThrowIfCancellationRequested();
                if (part.Mode == AttachMode.Bone)
                {
                    // A rigid item goes with the body that owns its attachment bone. The origin of its mesh is item-local.
                    if (AttachmentSide(look, rig, groups, part, cut) == side) equipment.Add((part, null));
                    continue;
                }
                var source = existingMesh;
                if (source is null && !equipmentSources.TryGetValue(part.Mesh, out source))
                    equipmentSources[part.Mesh] = source = ReadPart(rigs, part.Mesh);
                if (source is null) continue;
                int wholeSide = cut.FullBody ? EntirelyOnOneSide(source, cut.Plane) : 0;
                if (wholeSide != 0)
                {
                    if (wholeSide != side) continue;
                    if (AssignmentsStayOnSide(source, kept))
                    {
                        equipment.Add((part, existingMesh));
                        continue;
                    }
                }
                var clipped = MeshCut.Clip(source, rig, [section], buildCaps: false, shareSourceVertices: true);
                if (!clipped.HasTriangles) continue;
                RestrictWeights(clipped.Skin, kept, fallback);
                equipment.Add((part, clipped.Skin));
            }
            RestrictWeights(skin.Skin, kept, fallback);
            pieces.Add(new(side, skin.Skin, tissue, equipment, skin.Min, skin.Max, kept, hiddenLimbs, showHead));

            void AddTissue(OgreMesh mesh, string label, bool maskPunch = false)
            {
                if (maskPunch && openTunnel is not null)
                {
                    var clear = MeshCut.Clip(mesh, rig, [], removeVolumes: openTunnel, buildCaps: false);
                    if (!clear.HasTriangles) return;
                    if (clear.RemovedSurface) mesh = clear.Skin;
                }
                RestrictWeights(mesh, kept, fallback);
                // Generated anatomy repeats identical corners. Share those before reserving native cache space.
                OgreMeshWeld.Apply(mesh);
                tissue.Add((mesh, label));
            }
        }
#if BRUTALITY_STOCK_SAVE_PROBE
        if (prior is not null && Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_TRACE_HITS") == "1")
        {
            var labels = pieces.SelectMany(piece => piece.Tissue.Select(surface => surface.Label)).ToArray();
            Console.WriteLine($"Brutality stock composed torso priorLimbs={prior?.Limbs.Length ?? 0} punch={labels.Contains("punch-through lining")} stump={labels.Contains("muscle and bone section")}");
        }
#endif
        return new(new(rig, cut.Plane, pieces), definitions.Positive, definitions.Negative);
    }

    static Vertex[] SectionSkin(OgreMesh body, SkeletonRig rig, CharacterAppearance look, bool shaved)
    {
        var vertices = new List<Vertex>();
        foreach (var part in MeshCut.SourceModel(body, rig.BoneCount).Parts)
        {
            var morphs = MeshCut.MorphDeltas(body, part.SubMeshIndex, part.Vertices.Length, look.PoseWeights, shaved);
            for (int i = 0; i + 2 < part.Indices.Length; i += 3)
            {
                uint a = part.Indices[i], b = part.Indices[i + 1], c = part.Indices[i + 2];
                if (a >= part.Vertices.Length || b >= part.Vertices.Length || c >= part.Vertices.Length) continue;
                vertices.Add(At(a)); vertices.Add(At(b)); vertices.Add(At(c));
            }
            Vertex At(uint index) => part.Vertices[index] with { Position = part.Vertices[index].Position + morphs[index] };
        }
        return vertices.ToArray();
    }

    static OgreMesh? OrganicSection(Vertex[] skin, uint[] groups, Vector4 plane, int side, StockTissuePalette palette,
        string skeleton, bool[] kept, int[] fallback, int[] components)
    {
        const float depth = .85f;
        var outward = -Vector3.Normalize(new(plane.X, plane.Y, plane.Z)) * side;
        var cut = new NativeSectionCut(plane, side, FullBody: true);
        var section = NativeSectionGeometry.Build(skin, groups, cut, components: components,
            weldDistance: SectionWeldDistance, backingDepth: depth, bindBacking: BindBacking);
        // A contour on an arm or the neck is not enough: the wider weld can close one of those
        // while collapsing nearby edges of the actual trunk opening.
        if (!CoversTorso(section))
            section = NativeSectionGeometry.Build(skin, groups, cut, components: components, backingDepth: depth, bindBacking: BindBacking) ?? section;
        if (!CoversTorso(section))
            section = NativeSectionGeometry.Build(skin, groups, cut, components: components,
                weldDistance: .006f, backingDepth: depth, bindBacking: BindBacking) ?? section;
        if (section is not { Length: > 0 }) return null;

        Vertex BindBacking(Vertex vertex)
        {
            var weights = vertex.Weights;
            int strongest = 0;
            float sum = 0;
            for (int k = 0; k < 4; k++)
            {
                if (vertex.Weights[k] > vertex.Weights[strongest]) strongest = k;
                int bone = (int)(vertex.Bones >> (k * 8) & 255);
                if (bone >= kept.Length || !kept[bone]) weights[k] = 0;
                sum += weights[k];
            }
            if (sum > 1e-5f) return vertex with { Weights = weights / sum };
            int source = (int)(vertex.Bones >> (strongest * 8) & 255);
            return vertex with { Bones = (uint)fallback[Math.Min(source, fallback.Length - 1)], Weights = Vector4.UnitX };
        }

        bool CoversTorso(Vertex[]? vertices) => vertices is not null
            && vertices.Count(vertex => NativeSectionGeometry.Group(vertex, groups) == 0) >= 24;
        // Translate native surface metadata to stock material colours. Recessed flesh is darker, but remains tissue.
        for (int i = 0; i < section.Length; i++)
        {
            var vertex = section[i];
            Vector4 colour;
            if (vertex.Colour.Y > .45f) colour = palette.Bone;
            else if (vertex.Uv.X >= NativeSectionGeometry.BackingBand)
            {
                var p = vertex.Position;
                float grain = .71f + .10f * MathF.Sin(p.X * 12 + p.Y * 5 + p.Z * 8);
                colour = palette.Muscle * new Vector4(grain, grain, grain, 1);
            }
            else colour = palette.Tint(vertex.Colour with { W = 1 }, .72f);
            var normal = vertex.Uv.X >= NativeSectionGeometry.BackingBand
                ? Vector3.Normalize(Vector3.Lerp(outward, vertex.Normal, .25f)) : vertex.Normal;
            section[i] = vertex with { Colour = colour, Normal = normal, Tangent = Vector4.UnitW };
        }
        return MeshCut.FromTriangles(section, skeleton);
    }

    static bool[] KeptBones(SkeletonRig rig, int[] groups, CutHalfspace section)
    {
        var kept = new bool[rig.BoneCount];
        for (int i = 0; i < kept.Length; i++)
            kept[i] = i <= byte.MaxValue && rig.ByHandle[i] is not null && section.Band(rig.BindPosition[i], groups[i]) >= -.0001f;
        return kept;
    }

    static int[]? NearestKeptBones(SkeletonRig rig, int[] groups, bool[] kept, int[]? components)
    {
        int[] allowed = Enumerable.Range(0, kept.Length).Where(i => kept[i]).ToArray();
        if (allowed.Length == 0) return null;
        var fallback = new int[kept.Length];
        for (int bone = 0; bone < fallback.Length; bone++)
        {
            if (kept[bone]) { fallback[bone] = bone; continue; }
            int closest = allowed[0];
            float distance = float.PositiveInfinity;
            foreach (int candidate in allowed)
            {
                if (components is not null && components[candidate] != components[bone]) continue;
                float score = Vector3.DistanceSquared(rig.BindPosition[bone], rig.BindPosition[candidate]);
                if (groups[candidate] != groups[bone]) score += 25;
                if (score < distance) (closest, distance) = (candidate, score);
            }
            fallback[bone] = closest;
        }
        return fallback;
    }

    static void RestrictWeights(OgreMesh mesh, bool[] kept, int[] fallback)
    {
        // MeshCut encodes in vertex order. Compact in place to avoid allocating another body-sized array.
        foreach (var submesh in mesh.SubMeshes)
        {
            var weights = submesh.BoneAssignments;
            int write = 0, read = 0;
            while (read < weights.Count)
            {
                int start = read;
                var strongest = weights[start];
                float sum = 0;
                for (; read < weights.Count && weights[read].VertexIndex == strongest.VertexIndex; read++)
                {
                    var value = weights[read];
                    if (value.Weight > strongest.Weight) strongest = value;
                    if (value.BoneIndex < kept.Length && kept[value.BoneIndex]) sum += value.Weight;
                }
                if (sum <= .00001f)
                {
                    ushort mapped = (ushort)fallback[Math.Min(strongest.BoneIndex, fallback.Length - 1)];
                    weights[write++] = strongest with { BoneIndex = mapped, Weight = 1 };
                    continue;
                }
                for (int index = start; index < read; index++)
                {
                    var value = weights[index];
                    if (value.BoneIndex < kept.Length && kept[value.BoneIndex])
                        weights[write++] = value with { Weight = value.Weight / sum };
                }
            }
            if (write < weights.Count) weights.RemoveRange(write, weights.Count - write);
        }
    }

    static int AttachmentSide(CharacterAppearance look, SkeletonRig rig, int[] groups, CharacterPart part, TorsoBisectionState cut)
    {
        var point = part.Point is { } name && look.AttachmentPoints.TryGetValue(name, out var found) ? found : null;
        if (point is null || rig.Handle(point.Bone) is not { } bone) return 1;
        return new CutHalfspace(cut.Plane, LegacyTorso: !cut.FullBody).Band(rig.BindPosition[bone], groups[bone]) >= 0 ? 1 : -1;
    }

    static OgreMesh? ReadPart(CharacterRigs rigs, string part)
    {
        if (rigs.FindMesh(part) is not { } path) return null;
        try { return OgreMeshReader.ReadFile(path); }
        catch (Exception e) when (e is OgreFormatException or IOException or InvalidDataException or UnauthorizedAccessException) { return null; }
    }

    static bool AssignmentsStayOnSide(OgreMesh source, bool[] kept)
    {
        bool Stays(OgreBoneAssignment weight) => weight.Weight <= 0 || (weight.BoneIndex < kept.Length && kept[weight.BoneIndex]);
        return source.BoneAssignments.All(Stays) && source.SubMeshes.All(submesh => submesh.BoneAssignments.All(Stays));
    }

    static int EntirelyOnOneSide(OgreMesh mesh, Vector4 plane)
    {
        if (mesh.Bounds is not { } bounds || mesh.Poses.Count != 0) return 0;
        var centre = (bounds.Min + bounds.Max) * .5f;
        var extent = (bounds.Max - bounds.Min) * .5f;
        var normal = new Vector3(plane.X, plane.Y, plane.Z);
        float middle = Distance(plane, centre), reach = Vector3.Dot(extent, Vector3.Abs(normal));
        return middle - reach > .02f ? 1 : middle + reach < -.02f ? -1 : 0;
    }

    static OgreMesh? OrganicInterior(string folder, SkeletonRig rig, string skeleton, Vector4 plane,
        StockTissuePalette palette, StockTorsoMuscle muscles, CancellationToken cancellation)
    {
        if (rig.Handle("Bip01 Pelvis") is not { } pelvis || rig.Handle("Bip01 Neck") is not { } neck
            || new AnatomyAssets(folder).Ecorche is not { } path) return null;
        var hip = rig.BindPosition[pelvis];
        float scale = Vector3.Distance(rig.BindPosition[neck], hip) / 6.1649f;
        if (!float.IsFinite(scale) || scale is < .5f or > 2) return null;
        var source = OrganicBatches(path);
        // Conservative fit around the rig's pelvis, keeping protruding muscle out of the stock skin.
        var fit = new Vector3(.84f, 1, .82f) * scale;
        var vertices = new List<Vertex>();
        foreach (var batch in source.Value)
        {
            if (rig.Handle(batch.Bone) is not { } bone || bone > byte.MaxValue) continue;
            for (int index = 0; index + 2 < batch.Vertices.Length; index += 3)
            {
                if ((index & 2047) == 0) cancellation.ThrowIfCancellationRequested();
                var a = batch.Vertices[index]; var b = batch.Vertices[index + 1]; var c = batch.Vertices[index + 2];
                var p = Fit(a); var q = Fit(b); var r = Fit(c);
                if (!NearSection(plane, OrganicInteriorDepth + .7f, p, q, r)) continue;
                if (muscles.Fit(p) is not { } first || muscles.Fit(q) is not { } second || muscles.Fit(r) is not { } third) continue;
                if (!NearSection(plane, OrganicInteriorDepth, first.Position, second.Position, third.Position)) continue;
                var edge = second.Position - first.Position;
                var other = third.Position - first.Position;
                if (Vector3.Cross(edge, other).LengthSquared() < 1e-8f) continue;
                // Neighbours fitted against different folds of skin must not create a sheet across the cavity.
                float longest = LongestEdge(p, q, r);
                if (LongestEdge(first.Position, second.Position, third.Position) > MathF.Max(.8f * .8f, longest * 3)) continue;
                vertices.Add(AsVertex(a, first)); vertices.Add(AsVertex(b, second)); vertices.Add(AsVertex(c, third));
            }

            Vector3 Fit(AnatomyVertex vertex) => hip + (vertex.Position - AnatomyPelvis) * fit;
            Vertex AsVertex(AnatomyVertex vertex, StockTorsoMuscle.Fitted fitted)
            {
                var position = fitted.Position;
                var normal = vertex.Normal / fit;
                float striation = .80f + .20f * MathF.Pow(.5f + .5f * MathF.Sin(position.X * 27 + position.Z * 23 + position.Y * 4), 2);
                var colour = batch.Layer switch
                {
                    0 => palette.Bone,
                    1 => palette.Muscle * new Vector4(striation, striation, striation, 1),
                    _ => palette.Organ((int)MathF.Floor(vertex.Uv.X / 10)),
                };
                return new()
                {
                    Position = position, Normal = normal.LengthSquared() > 1e-10f ? Vector3.Normalize(normal) : Vector3.UnitY,
                    Colour = colour, Bones = fitted.Bones, Weights = fitted.Weights,
                };
            }
        }
        return vertices.Count == 0 ? null : MeshCut.FromTriangles(vertices, skeleton);
    }

    internal static void WarmAnatomy(string folder)
    {
        if (new AnatomyAssets(folder).Ecorche is { } path)
            _ = Task.Run(() => OrganicBatches(path).Value);
    }

    static Lazy<IReadOnlyList<AnatomyBatch>> OrganicBatches(string path)
    {
        lock (anatomyLock)
        {
            if (anatomy is not { } cached || !string.Equals(cached.Path, path, StringComparison.OrdinalIgnoreCase))
                anatomy = (path, new(() => ReadAnatomy(path)));
            return anatomy.Value.Batches;
        }
    }

    static IReadOnlyList<AnatomyBatch> ReadAnatomy(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return AnatomyFile.Read(stream).Where(batch => batch.Layer is 0 or 1 or 4
                && batch.Bone is "Bip01 Pelvis" or "Bip01 Spine" or "Bip01 Spine1" or "Bip01 Spine2" or "Bip01 Neck")
                .Select(batch => batch.Layer == 1 ? StockTorsoMuscle.Compact(batch) : batch).ToArray();
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { return []; }
    }

    static OgreMesh? RobotInterior(OgreMesh body, SkeletonRig rig, Vector4 plane, bool headless, CancellationToken cancellation)
    {
        var source = MeshCut.SourceModel(body, rig.BoneCount).Parts.SelectMany(part => part.Vertices).ToArray();
        if (MechanicalAnatomy.Build(rig, source, [], headless) is not { } built) return null;
        cancellation.ThrowIfCancellationRequested();
        var near = new List<Vertex>();
        for (int i = 0; i + 2 < built.Count; i += 3)
        {
            var a = built[i]; var b = built[i + 1]; var c = built[i + 2];
            if (!NearSection(plane, MechanicalInteriorDepth, a.Position, b.Position, c.Position)) continue;
            near.Add(a with { Colour = a.Colour with { W = 1 } });
            near.Add(b with { Colour = b.Colour with { W = 1 } });
            near.Add(c with { Colour = c.Colour with { W = 1 } });
        }
        return near.Count == 0 ? null : MeshCut.FromTriangles(near, body.SkeletonName);
    }

    static float LongestEdge(Vector3 a, Vector3 b, Vector3 c) =>
        MathF.Max(Vector3.DistanceSquared(a, b), MathF.Max(Vector3.DistanceSquared(b, c), Vector3.DistanceSquared(c, a)));

    static bool NearSection(Vector4 plane, float depth, Vector3 a, Vector3 b, Vector3 c)
    {
        float da = Distance(plane, a), db = Distance(plane, b), dc = Distance(plane, c);
        return MathF.Min(da, MathF.Min(db, dc)) <= depth && MathF.Max(da, MathF.Max(db, dc)) >= -depth;
    }

    static float Distance(Vector4 plane, Vector3 point) => plane.X * point.X + plane.Y * point.Y + plane.Z * point.Z + plane.W;
}
