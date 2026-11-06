using System.Numerics;
using Meitou.Brutality.Rendering;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;
using Meitou.Rendering;

namespace Meitou.Brutality;

internal static class StockWoundBuild
{
    internal sealed record WoundSpill(SkeletonRig Rig, Vertex[] Anchor);
    internal readonly record struct HeadBounds(int Bone, Vector3 Centre, Vector3 HalfExtent);
    internal sealed record Geometry(OgreMesh? Body, IReadOnlyList<(OgreMesh Mesh, string Label)> Tissue,
        IReadOnlyList<(CharacterPart Part, OgreMesh? Mesh)> Equipment, Geometry? Detached = null, WoundSpill? Spill = null,
        Vertex[]? PunchExit = null, HeadBounds? Bounds = null, Vertex[]? PunchEntry = null);

    internal static Geometry? Build(string folder, CharacterRigs rigs, CharacterAppearance look, StockWoundRules.View view,
        CharacterAppearance? looseHead, CancellationToken cancellation)
    {
        if (look.Animal is not null || rigs.FindMesh(look.BodyMesh) is not { } path) return null;
        var body = OgreMeshReader.ReadFile(path);
        if (body.SkeletonName is not { } skeleton || rigs.Rig(skeleton) is not { } rig) return null;
        return BuildCore(folder, rigs, look, view, body, rig, look.Parts.Select(part => (part, (OgreMesh?)null)).ToArray(),
            looseHead, cancellation, out _);
    }

    internal static Geometry? BuildRetained(string folder, CharacterRigs rigs, CharacterAppearance look, StockWoundRules.View view,
        OgreMesh body, SkeletonRig rig, IReadOnlyList<(CharacterPart Part, OgreMesh? Mesh)> equipment,
        CancellationToken cancellation, out CutVolume? punchVolume) =>
        BuildCore(folder, rigs, look, view, body, rig, equipment, null, cancellation, out punchVolume);

    static Geometry? BuildCore(string folder, CharacterRigs rigs, CharacterAppearance look, StockWoundRules.View view,
        OgreMesh body, SkeletonRig rig, IReadOnlyList<(CharacterPart Part, OgreMesh? Mesh)> sourceEquipment,
        CharacterAppearance? looseHead, CancellationToken cancellation, out CutVolume? punchVolume)
    {
        cancellation.ThrowIfCancellationRequested();
        punchVolume = null;

        // The stock renderer cannot project a bruise into skin and cloth materials. Its standalone
        // opaque crush cards can cover clothes or hover over a missing head. Keep the actual cuts.
        bool headRetained = !look.Headless && view.AttachedHead is null;
        var recent = new BodyWoundState(view.Body.Entries.Where(entry => entry.Mark.Shape.Kind is WoundKind.Slash or WoundKind.Thrust
                && (headRetained || entry.Mark.Segment != 8))
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
        Vector4? split = view.AttachedHead is { } cut ? HeadPlane(body, rig, look, cut.Plane) : null;
        if (view.AttachedHead is not null && split is null) return null;
        if (split is { } retain) rules.Add(new(retain, -1, 7, MorphedDistance: true));
        OgreMesh? skin = null;
        bool shaved = look.Character?.GetBool("shaved") == true;
        var flesh = robot ? new Vector4(.14f, .15f, .16f, 1) : StockTissuePalette.For(look).Flesh;
        if (volumes.Count > 0 || rules.Count > 0)
        {
            var clipped = MeshCut.Clip(body, rig, rules, look.PoseWeights, shaved, removeVolumes: volumes, buildCaps: false);
            if (!clipped.HasTriangles) return null;
            skin = clipped.Skin;
        }
        if (skin is null && tissue.Count == 0) return null;

        var equipment = new List<(CharacterPart Part, OgreMesh? Mesh)>(sourceEquipment.Count);
        bool retainLimbEquipment = StockHeadMeshes.IsGeneratedBody(look);
        var clothing = retainLimbEquipment ? [] : ordinary.Clothing;
        var punchedClothing = punch is null ? clothing : clothing.Append(punch.Clothing).ToArray();
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
                : part.Slot is AttachSlot.Body or AttachSlot.Shirt or AttachSlot.Legs or AttachSlot.Neck ? punchedClothing : clothing;
            if (pockets.Count == 0)
            {
                equipment.Add((part, previous));
                continue;
            }
            OgreMesh? source = previous;
            if (source is null && rigs.FindMesh(part.Mesh) is { } partPath)
            {
                try { source = OgreMeshReader.ReadFile(partPath); }
                catch (Exception error) when (error is IOException or InvalidDataException or OgreFormatException or UnauthorizedAccessException) { }
            }
            if (source is null || source.Bounds is { } bounds && !pockets.Any(pocket => Intersects(bounds.Min, bounds.Max, pocket.Bounds)))
            {
                equipment.Add((part, previous));
                continue;
            }
            var clipped = MeshCut.Clip(source, rig, [], removeVolumes: pockets, buildCaps: false);
            if (!clipped.HasTriangles) continue;
            equipment.Add((part, clipped.RemovedSurface ? clipped.Skin : previous));
        }
        if (split is not { } plane) return new(skin, tissue, equipment, Spill: spill, PunchExit: punch?.Exit, PunchEntry: punch?.Entry);

        var detachedTissue = new List<(OgreMesh Mesh, string Label)>();
        // Back the cavity slightly inside both exposed faces so the outer edge and the fitted
        // brain have depth instead of being covered by one flat plane.
        var headOnly = MeshCut.Clip(body, rig, [new(new(0, 1, 0, 1_000_000), 1, 7, true)], look.PoseWeights, shaved, buildCaps: false);
        if (Backing(-1) is { } cover) tissue.Insert(0, (cover, "fracture surface"));
        AddSplitInterior(folder, body, look, rig, plane, tissue, detachedTissue, cancellation);
        if (looseHead is null) return new(skin, tissue, equipment, Spill: spill, PunchExit: punch?.Exit, PunchEntry: punch?.Entry);
        var detached = MeshCut.Clip(body, rig, [new(plane, 1, 7, true, MorphedDistance: true)],
            look.PoseWeights, shaved, buildCaps: false);
        if (!detached.HasTriangles) return null;
        if (Backing(1) is { } face) detachedTissue.Insert(0, (face, "fracture surface"));
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
        return new(skin, tissue, equipment, new(detached.Skin, detachedTissue, accessories, Bounds: detachedBounds), spill, punch?.Exit, PunchEntry: punch?.Entry);

        OgreMesh? Backing(int side)
        {
            if (!headOnly.HasTriangles) return null;
            if (StockHeadAnatomy.SkinBacking(headOnly.Skin, rig, look, plane, side, flesh) is { } lining) return lining;
            var recessed = plane with { W = plane.W - side * .16f };
            return MeshCut.Clip(headOnly.Skin, rig, [new(recessed, side, MorphedDistance: true)],
                look.PoseWeights, shaved, capColour: flesh).Wound;
        }
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
