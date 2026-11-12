using Meitou.Game.Rendering.Frame;
using System.Numerics;
using Meitou.Brutality.Rendering;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;

namespace Meitou.Brutality;

internal static class StockWoundBuild
{
    static readonly bool trace = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_TRACE_HITS") == "1";
    internal sealed record WoundSpill(SkeletonRig Rig, Vertex[] Anchor);
    internal readonly record struct HeadBounds(int Bone, Vector3 Centre, Vector3 HalfExtent);
    internal sealed record Geometry(OgreMesh? Body, IReadOnlyList<(OgreMesh Mesh, string Label)> Tissue,
        IReadOnlyList<(CharacterPart Part, OgreMesh? Mesh)> Equipment, Geometry? Detached = null, WoundSpill? Spill = null,
        Vertex[]? PunchExit = null, HeadBounds? Bounds = null, Vertex[]? PunchEntry = null, StockVestTears.Payload? VestTears = null);

    internal static Geometry? Build(string folder, CharacterRigs rigs, CharacterAppearance look, StockWoundRules.View view,
        CharacterAppearance? looseHead, CancellationToken cancellation, GarmentDamageActor? garments = null)
    {
        if (look.Animal is not null || rigs.FindMesh(look.BodyMesh) is not { } path) return null;
        var body = OgreMeshReader.ReadFile(path);
        if (body.SkeletonName is not { } skeleton || rigs.Rig(skeleton) is not { } rig) return null;
        return BuildCore(folder, rigs, look, view, body, rig, look.Parts.Select(part => (part, (OgreMesh?)null)).ToArray(),
            looseHead, cancellation, out _, garments);
    }

    internal static Geometry? BuildRetained(string folder, CharacterRigs rigs, CharacterAppearance look, StockWoundRules.View view,
        OgreMesh body, SkeletonRig rig, IReadOnlyList<(CharacterPart Part, OgreMesh? Mesh)> equipment,
        CancellationToken cancellation, out CutVolume? punchVolume) =>
        BuildCore(folder, rigs, look, view, body, rig, equipment, null, cancellation, out punchVolume);

    static Geometry? BuildCore(string folder, CharacterRigs rigs, CharacterAppearance look, StockWoundRules.View view,
        OgreMesh body, SkeletonRig rig, IReadOnlyList<(CharacterPart Part, OgreMesh? Mesh)> sourceEquipment,
        CharacterAppearance? looseHead, CancellationToken cancellation, out CutVolume? punchVolume, GarmentDamageActor? garments = null)
    {
        cancellation.ThrowIfCancellationRequested();
        punchVolume = null;

        // The stock renderer cannot project a bruise into skin and cloth materials. Its standalone
        // opaque crush cards can cover clothes or hover over a missing head. Keep the actual cuts.
        bool headRetained = !look.Headless && view.AttachedHead is null;
        var recent = new BodyWoundState(view.Body.Entries.Where(entry => entry.Mark.Shape.Kind is WoundKind.Slash or WoundKind.Thrust
                && (headRetained || !WoundSegments.IsHead(entry.Mark.Segment)))
            .OrderByDescending(entry => entry.Mark.Age).Take(6).ToArray(), view.Body.BellyHits);
        var ordinary = BodyWoundGeometry.Build(body, rig, look, recent);
        WoundSpill? spill = null;
        if (view.Body.BellyHits >= 10 && look.Race.Name is ("Greenlander" or "Scorchlander")
            && view.Body.Entries.Any(entry => (entry.Region == 1 || entry.Region < 0 && entry.Mark.Segment == 9 && entry.Mark.T <= .54f)
                && entry.Mark.Shape.Kind is WoundKind.Slash or WoundKind.Thrust))
        {
            var anchor = ordinary.AbdomenAnchor.Count > 0 ? ordinary.AbdomenAnchor
                : BodyWoundGeometry.Build(body, rig, look, view.Body, anchorsOnly: true).AbdomenAnchor;
            if (anchor.Count > 0) spill = new(rig, anchor.ToArray());
        }
        var severe = look.Headless ? null : HeadReliefGeometry.Build(body, rig, look, view.Head);
        var punch = view.Hole is { } hole ? StockPunchGeometry.Build(body, rig, look, hole) : null;
        punchVolume = punch?.Skin;
        var tissue = new List<(OgreMesh Mesh, string Label)>();
        if (ordinary.Relief is { } relief)
        {
            if (punch is null) tissue.Add((relief, "wound relief"));
            else
            {
                // Impact and older wound overlays use their own meshes and can bridge the missing skin.
                // They are already morphed into the body frame, so clip them without applying pose deltas again.
                var exposed = MeshCut.Clip(relief, rig, [], removeVolumes: [punch.Skin], buildCaps: false);
                if (exposed.HasTriangles) tissue.Add((exposed.RemovedSurface ? exposed.Skin : relief, "wound relief"));
            }
        }
        if (severe?.Relief is { } headRelief) tissue.Add((headRelief, "fracture surface"));
        if (punch is not null) tissue.Add((punch.Lining, "punch-through lining"));
        if (severe is not null && view.Head.Kind == SevereHeadWoundKind.BrokenSkull)
            AddSkullInterior(folder, body, look, rig, severe, tissue, cancellation);

        cancellation.ThrowIfCancellationRequested();
        var volumes = new List<CutVolume>(ordinary.Skin);
        if (severe is not null) volumes.AddRange(severe.Skin);
        if (punch is not null) volumes.Add(punch.Skin);
        var rules = new List<CutHalfspace>(severe?.Rules ?? []);
        bool robot = look.Race.GetBool("is robot");
        bool organicNeck = !robot && view.AttachedHead is { Mode: HeadCutMode.NeckDetach };
        Vector4? split = view.AttachedHead is { } cut ? HeadPlane(body, rig, look, cut.Plane) : null;
        if (view.AttachedHead is not null && split is null) return null;
        if (split is { } retain) rules.Add(new(retain, -1, organicNeck ? -1 : 7, MorphedDistance: true));
        OgreMesh? skin = null;
        bool shaved = look.Character?.GetBool("shaved") == true;
        var flesh = robot ? new Vector4(.14f, .15f, .16f, 1) : StockTissuePalette.For(look).Flesh;
        if (volumes.Count > 0 || rules.Count > 0)
        {
            var clipped = MeshCut.Clip(body, rig, rules, look.PoseWeights, shaved, removeVolumes: volumes, buildCaps: false);
            if (!clipped.HasTriangles) return null;
            skin = clipped.Skin;
        }
        if (view.Bruises is { HasAny: true } bruises && StockBruiseGeometry.Build(skin ?? body, rig, look, bruises) is { } bruise)
        {
            tissue.Add((bruise, "skin bruise"));
            if (trace) Console.WriteLine($"Brutality stock skin bruise mesh ready marks={bruises.Entries.Count}");
        }

        var equipment = new List<(CharacterPart Part, OgreMesh? Mesh)>(sourceEquipment.Count);
        bool retainLimbEquipment = StockHeadMeshes.IsGeneratedBody(look);
        var punchedClothing = punch is null ? [] : new[] { punch.Clothing };
        bool equipmentChanged = false;
        foreach (var (part, previous) in sourceEquipment)
        {
            cancellation.ThrowIfCancellationRequested();
            if (part.Mode != AttachMode.SharedSkeleton || StockHeadMeshes.IsTissue(part.Mesh))
            {
                equipment.Add((part, previous));
                continue;
            }
            bool hair = part.Slot is AttachSlot.Hair or AttachSlot.Beard;
            var pockets = hair ? retainLimbEquipment ? [] : severe?.Hair ?? []
                : part.Slot is AttachSlot.Body or AttachSlot.Shirt or AttachSlot.Legs or AttachSlot.Neck ? punchedClothing : [];
            var marks = garments?.Items.FirstOrDefault(item => item.Parts.Any(worn => ReferenceEquals(worn, part)
                || worn.Record.StringId == part.Record.StringId && worn.Slot == part.Slot && worn.Field == part.Field))?.Marks;
            if (pockets.Count == 0 && marks is not { Length: > 0 })
            {
                equipment.Add((part, previous));
                continue;
            }
            OgreMesh? source = previous ?? ReadEquipment(part);
            if (source is null)
            {
                equipment.Add((part, previous));
                continue;
            }
            OgreMesh? changed = previous;
            if (pockets.Count > 0 && (source.Bounds is not { } bounds || pockets.Any(pocket => Intersects(bounds.Min, bounds.Max, pocket.Bounds))))
            {
                var clipped = MeshCut.Clip(source, rig, [], removeVolumes: pockets, buildCaps: false);
                if (!clipped.HasTriangles) { equipmentChanged = true; continue; }
                if (clipped.RemovedSurface) changed = clipped.Skin;
            }
            if (marks is { Length: > 0 })
            {
                var wear = StockGarmentDamageMesh.Build(changed ?? source, rig, look, marks, () => UnderLayers(part.Slot), cancellation);
                changed = wear.Mesh ?? changed;
                if (wear.Detail is { } detail) tissue.Add((detail, "garment wear"));
                if (trace && (wear.Mesh is not null || wear.Detail is not null))
                    Console.WriteLine($"Brutality stock garment visual record={part.Record.StringId} marks={marks.Length} material={marks[^1].Material}");
            }
            equipmentChanged |= !ReferenceEquals(previous, changed);
            equipment.Add((part, changed));
        }

        OgreMesh? ReadEquipment(CharacterPart part)
        {
            if (rigs.FindMesh(part.Mesh) is not { } path) return null;
            try { return OgreMeshReader.ReadFile(path); }
            catch (Exception error) when (error is IOException or InvalidDataException or OgreFormatException or UnauthorizedAccessException) { return null; }
        }

        IEnumerable<OgreMesh> UnderLayers(AttachSlot? shell)
        {
            yield return body;
            var layer = shell switch
            {
                AttachSlot.Body => AttachSlot.Shirt,
                AttachSlot.Boots => AttachSlot.Legs,
                AttachSlot.Hat => AttachSlot.Hair,
                _ => AttachSlot.None,
            };
            if (layer == AttachSlot.None) yield break;
            foreach (var (part, mesh) in sourceEquipment)
                if (part.Slot == layer && part.Mode == AttachMode.SharedSkeleton && (mesh ?? ReadEquipment(part)) is { } under)
                    yield return under;
        }

        var vestTears = garments is not null ? StockVestTears.Build(body, rig, look, garments) : null;
        if (trace && vestTears is not null) Console.WriteLine("Brutality stock vest tear ready");
        if (skin is null && tissue.Count == 0 && !equipmentChanged && vestTears is null) return null;
        if (split is not { } plane) return new(skin, tissue, equipment, Spill: spill, PunchExit: punch?.Exit,
            PunchEntry: punch?.Entry, VestTears: vestTears);

        var detachedTissue = new List<(OgreMesh Mesh, string Label)>();
        var looseRule = new CutHalfspace(plane, 1, organicNeck ? -1 : 7, RejectOtherGroups: !organicNeck, MorphedDistance: true);
        var detached = looseHead is null ? null
            : MeshCut.Clip(body, rig, [looseRule], look.PoseWeights, shaved, buildCaps: false);
        if (detached is { HasTriangles: false }) return null;
        CutMeshResult? headOnly = organicNeck ? null
            : MeshCut.Clip(body, rig, [new(new(0, 1, 0, 1_000_000), 1, 7, true)], look.PoseWeights, shaved, buildCaps: false);
        IReadOnlyList<Vertex[]> neckRings = [];
        if (organicNeck)
        {
            neckRings = StockSectionContours.Read(skin!, rig, look, looseRule with { KeepSide = -1 });
            // Both halves use the same cut plane and skin weights. A mesh seam may break a loop on only one side.
            if (detached is not null)
            {
                var looseRings = StockSectionContours.Read(detached.Skin, rig, look, looseRule);
                if (LargestRing(looseRings) > LargestRing(neckRings) * 1.05f) neckRings = looseRings;
            }
        }
        string label = organicNeck && neckRings.Count > 0 ? "neck muscle and bone section" : "fracture surface";
        if ((organicNeck ? NeckBacking(-1) : Backing(-1)) is { } cover) tissue.Insert(0, (cover, label));
        if (!organicNeck) AddSplitInterior(folder, body, look, rig, plane, tissue, detachedTissue, cancellation);
        if (looseHead is null || detached is null) return new(skin, tissue, equipment, Spill: spill, PunchExit: punch?.Exit,
            PunchEntry: punch?.Entry, VestTears: vestTears);
        if ((organicNeck ? NeckBacking(1) : Backing(1)) is { } face) detachedTissue.Insert(0, (face, label));
        var accessories = new List<(CharacterPart Part, OgreMesh? Mesh)>();
        foreach (var part in looseHead.Parts)
        {
            cancellation.ThrowIfCancellationRequested();
            if (part.Mode != AttachMode.SharedSkeleton || rigs.FindMesh(part.Mesh) is not { } mesh) continue;
            try
            {
                var original = OgreMeshReader.ReadFile(mesh);
                var kept = MeshCut.Clip(original, rig, [new(plane, -1)], buildCaps: false);
                var away = MeshCut.Clip(original, rig, [new(plane, 1)], buildCaps: false);
                if (kept.HasTriangles) equipment.Add((part, kept.Skin));
                if (away.HasTriangles) accessories.Add((part, away.Skin));
            }
            catch (Exception error) when (error is IOException or InvalidDataException or OgreFormatException or UnauthorizedAccessException) { }
        }
        HeadBounds? detachedBounds = rig.Handle("Bip01 Head") is int head
            ? new(head, (detached.Min + detached.Max) * .5f, (detached.Max - detached.Min) * .5f) : null;
        return new(skin, tissue, equipment, new(detached.Skin, detachedTissue, accessories, Bounds: detachedBounds), spill, punch?.Exit,
            PunchEntry: punch?.Entry, VestTears: vestTears);

        OgreMesh? NeckBacking(int side)
        {
            var rule = new CutHalfspace(plane, side, MorphedDistance: true);
            int? anchor = rig.Handle(side < 0 ? "Bip01 Neck" : "Bip01 Head");
            var vertices = new List<Vertex>();
            if (anchor is >= 0 and <= byte.MaxValue)
            {
                var palette = StockTissuePalette.For(look);
                foreach (var contour in neckRings)
                    StockLimbSections.Append(vertices, contour, rig, null, plane, side, anchor.Value, palette);
            }
            if (trace) Console.WriteLine($"Brutality stock neck section side={side} triangles={vertices.Count / 3}");
            var section = vertices.Count > 0 ? MeshCut.FromTriangles(vertices, body.SkeletonName)
                : MeshCut.Clip(body, rig, [rule], look.PoseWeights, shaved, capColour: flesh, capWeldDistance: .002f).Wound;
            if (section is not null) OgreMeshWeld.Apply(section);
            return section;
        }

        OgreMesh? Backing(int side)
        {
            if (headOnly is not { HasTriangles: true }) return null;
            if (StockHeadAnatomy.SkinBacking(headOnly.Skin, rig, look, plane, side, flesh) is { } lining) return lining;
            var recessed = plane with { W = plane.W - side * .16f };
            return MeshCut.Clip(headOnly.Skin, rig, [new(recessed, side, MorphedDistance: true)],
                look.PoseWeights, shaved, capColour: flesh).Wound;
        }
    }

    static float LargestRing(IReadOnlyList<Vertex[]> rings)
    {
        float largest = 0;
        foreach (var ring in rings)
        {
            var origin = ring[0].Position;
            var area = Vector3.Zero;
            for (int i = 1; i + 1 < ring.Length; i++)
                area += Vector3.Cross(ring[i].Position - origin, ring[i + 1].Position - origin);
            largest = MathF.Max(largest, area.LengthSquared());
        }
        return largest;
    }

    internal static Vector4? HeadPlane(OgreMesh body, SkeletonRig rig, CharacterAppearance look, Vector4 plane)
    {
        var (min, max) = AnatomyGeometry.Bounds(HeadReliefGeometry.HeadPoints(body, rig, look));
        var span = max - min;
        if (span.X <= 0 || span.Y <= 0 || span.Z <= 0) return null;
        var normal = new Vector3(plane.X, plane.Y, plane.Z) / Vector3.Max(span * .5f, new(.01f));
        float length = normal.Length();
        return float.IsFinite(length) && length > 1e-5f
            ? new Vector4(normal, plane.W - Vector3.Dot(normal, (min + max) * .5f)) / length : null;
    }

    static void AddSplitInterior(string folder, OgreMesh body, CharacterAppearance look, SkeletonRig rig, Vector4 plane,
        List<(OgreMesh Mesh, string Label)> kept, List<(OgreMesh Mesh, string Label)> detached, CancellationToken cancellation)
    {
        if (look.Race.GetBool("is robot"))
        {
            Add(StockHeadBuild.MechanicalInterior(body, rig), "fracture circuitry");
            return;
        }
        var (min, max) = AnatomyGeometry.Bounds(HeadReliefGeometry.HeadPoints(body, rig, look));
        var palette = StockTissuePalette.For(look);
        var skeleton = body.SkeletonName!;
        var (skull, brain) = StockHeadAnatomy.Load(folder, rig, body, look, skeleton, min, max, cancellation);
        if (StockHeadAnatomy.CutInterior(skull, brain, rig, new(plane, -1), palette, skeleton, cancellation) is { } stay)
            kept.Add((stay, "skull and brain"));
        if (StockHeadAnatomy.CutInterior(skull, brain, rig, new(plane), palette, skeleton, cancellation) is { } looseAnatomy)
            detached.Add((looseAnatomy, "skull and brain"));

        void Add(OgreMesh? source, string label)
        {
            if (source is null) return;
            cancellation.ThrowIfCancellationRequested();
            var keep = MeshCut.Clip(source, rig, [new(plane, -1), new(plane with { W = plane.W + .8f })], buildCaps: false);
            var loose = MeshCut.Clip(source, rig, [new(plane), new(plane with { W = plane.W - .8f }, -1)], buildCaps: false);
            if (keep.HasTriangles) kept.Add((keep.Skin, label));
            if (loose.HasTriangles) detached.Add((loose.Skin, label));
        }
    }

    static bool Intersects(Vector3 min, Vector3 max, Vector4? volume)
    {
        if (volume is not { } bounds) return true;
        var centre = new Vector3(bounds.X, bounds.Y, bounds.Z);
        return Vector3.DistanceSquared(Vector3.Clamp(centre, min, max), centre) <= bounds.W * bounds.W;
    }

    static void AddSkullInterior(string folder, OgreMesh body, CharacterAppearance look, SkeletonRig rig, HeadReliefPlan wound,
        List<(OgreMesh Mesh, string Label)> tissue, CancellationToken cancellation)
    {
        var (min, max) = AnatomyGeometry.Bounds(HeadReliefGeometry.HeadPoints(body, rig, look));
        var (skull, brain) = StockHeadAnatomy.Load(folder, rig, body, look, body.SkeletonName!, min, max, cancellation);
        var frame = wound.Frame;
        var neighbourhood = new CutHalfspace[]
        {
            Keep(frame.Along, frame.HalfLength * 1.22f), Keep(-frame.Along, frame.HalfLength * 1.22f),
            Keep(frame.Side, frame.HalfWidth * 1.45f), Keep(-frame.Side, frame.HalfWidth * 1.45f), Keep(frame.Normal, 1.3f),
        };
        CutHalfspace Keep(Vector3 normal, float radius) => new(new(normal, radius - Vector3.Dot(normal, frame.Centre)));
        if (skull is not null)
        {
            var shell = MeshCut.Clip(skull, rig, neighbourhood, removeVolumes: wound.Inside, buildCaps: false);
            if (shell.HasTriangles) tissue.Add((shell.Skin, "skull"));
        }
        cancellation.ThrowIfCancellationRequested();
        if (brain is not null)
        {
            var exposed = MeshCut.Clip(brain, rig, neighbourhood, buildCaps: false);
            if (exposed.HasTriangles) tissue.Add((exposed.Skin, "brain"));
        }
    }
}
