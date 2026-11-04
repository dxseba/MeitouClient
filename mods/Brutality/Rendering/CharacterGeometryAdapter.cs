using System.Numerics;
using System.Runtime.CompilerServices;
using Meitou.Brutality.Anatomy;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;
using Meitou.Rendering;

namespace Meitou.Brutality.Rendering;

public enum GeometryFragmentKind { TorsoPositive, TorsoNegative, HeadPositive, HeadNegative, Limb }

[Flags]
public enum GeometryFidelityGap
{
    None = 0,
    /// <summary>The requested local head wound could not be projected onto this body's supported head surface.</summary>
    SevereHeadRelief = 1,
    MissingAnatomy = 2,
    MissingPartMesh = 4,
    /// <summary>Sections use the host's ordinary lit vertex-colour material, rather than the fork's layered tissue shader.</summary>
    SectionMaterial = 8,
}

/// <summary>All coordinates remain in the original body's bind frame. StableKey identifies the independent physics/presentation fragment.</summary>
public sealed record GeometryFragment(GeometryFragmentKind Kind, int StableKey, int LimbSlot, RuntimeCharacterGeometry Geometry,
    Vector3 BindMin, Vector3 BindMax, Vector4 CutPlane);

public sealed record CharacterGeometryResult(RuntimeCharacterGeometry Retained, IReadOnlyList<GeometryFragment> Detached,
    Vector3 RetainedMin, Vector3 RetainedMax, GeometryFidelityGap FidelityGaps)
{
    /// <summary>Original bind-space surface samples, including bone weights, at the opened abdomen wound.</summary>
    public IReadOnlyList<Vertex> AbdomenAnchor { get; init; } = [];
}

/// <summary>
/// Mod-owned CPU geometry. The bridge supplies the uncollapsed pre-cut appearance, original rig/meshes, and permanent gameplay state.
/// Results are cached by that immutable look, mesh identity and cut values, so rendering a frame never allocates another geometry variant.
/// </summary>
public sealed class CharacterGeometryAdapter(AnatomyLibrary anatomy)
{
    internal AnatomyLibrary Anatomy => anatomy;
    readonly ConditionalWeakTable<CharacterAppearance, LookCache> looks = new();
    sealed class LookCache
    {
        public readonly Dictionary<StateKey, CharacterGeometryResult> Results = [];
    }
    readonly record struct StateKey(OgreMesh Body, SkeletonRig Rig, TorsoBisectionState? Torso, HeadCutState? Head,
        LimbCutState? LeftArm, LimbCutState? RightArm, LimbCutState? LeftLeg, LimbCutState? RightLeg, SevereHeadWoundState Severe, ulong Wounds);

    /// <summary>Looks up a published current-state mesh without importing anatomy or performing CPU cuts. Visual consumers must not bypass the producer's build budget.</summary>
    public bool TryCached(CharacterAppearance appearance, SkeletonRig rig, OgreMesh body, BrutalityActorView state,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CharacterGeometryResult? result)
    {
        result = null;
        if (!looks.TryGetValue(appearance, out var look)) return false;
        var key = Key(body, rig, state);
        lock (look)
        {
            if (!look.Results.TryGetValue(key, out var existing) || existing.Retained.IsRetired) return false;
            result = existing; return true;
        }
    }

    /// <summary>Releases a world's cached CPU presentation variants even when the host still caches its native appearance.</summary>
    public void Forget(CharacterAppearance appearance)
    {
        if (!looks.TryGetValue(appearance, out var look) || !looks.Remove(appearance)) return;
        lock (look)
        {
            foreach (var result in look.Results.Values)
            {
                result.Retained.Retire();
                foreach (var fragment in result.Detached) fragment.Geometry.Retire();
            }
            look.Results.Clear();
        }
    }

    static StateKey Key(OgreMesh body, SkeletonRig rig, BrutalityActorView state)
    {
        var limbs = new LimbCutState?[4];
        foreach (var cut in state.Limbs) if (cut.IsValid) limbs[cut.LimbSlot] ??= cut;
        return new(body, rig, state.Torso is { IsValid: true } torso ? torso : null,
            state.Head is { IsValid: true } head ? head : null, limbs[0], limbs[1], limbs[2], limbs[3], state.SevereHead with { HeavyHits = 0 }, state.Wounds.StateKey());
    }

    public CharacterGeometryResult Build(CharacterAppearance appearance, SkeletonRig rig, OgreMesh body,
        Func<string, OgreMesh?> loadPartMesh, BrutalityActorView state)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        ArgumentNullException.ThrowIfNull(rig);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(loadPartMesh);
        var key = Key(body, rig, state);
        var look = looks.GetOrCreateValue(appearance);
        lock (look)
        {
            if (look.Results.TryGetValue(key, out var existing) && !existing.Retained.IsRetired) return existing;
            look.Results.Remove(key);
            bool includeBowel = key.Torso is null && state.Wounds.BellyHits < 10;
            OgreMesh? inside = null;
            bool exposed = key.Torso is not null || key.Head is not null || key.LeftArm is not null || key.RightArm is not null || key.LeftLeg is not null || key.RightLeg is not null
                || key.Severe.Kind != SevereHeadWoundKind.None;
            if (exposed) inside = AnatomyGeometry.Build(anatomy, appearance, rig, body, includeBowel);
            var result = BuildCore(appearance, rig, body, loadPartMesh, key, inside, state.Wounds);
            while (look.Results.Count >= 8)
            {
                var oldest = look.Results.First(); look.Results.Remove(oldest.Key);
                oldest.Value.Retained.Retire(); foreach (var fragment in oldest.Value.Detached) fragment.Geometry.Retire();
            }
            return look.Results[key] = result;
        }
    }

    static CharacterGeometryResult BuildCore(CharacterAppearance appearance, SkeletonRig rig, OgreMesh body,
        Func<string, OgreMesh?> loadPartMesh, in StateKey state, OgreMesh? inside, BodyWoundState wounds)
    {
        var general = new List<CutHalfspace>();
        var fragments = new List<(GeometryFragmentKind Kind, int Key, int Limb, Vector4 Plane, List<CutHalfspace> Rules)>();
        var gaps = GeometryFidelityGap.SectionMaterial;
        var headRelief = HeadReliefGeometry.Build(body, rig, appearance, state.Severe);
        var bodyWounds = BodyWoundGeometry.Build(body, rig, appearance, wounds);
        var skinPockets = (headRelief?.Skin ?? []).Concat(bodyWounds.Skin).ToArray();
        var clothPockets = (headRelief?.Skin ?? []).Concat(bodyWounds.Clothing).ToArray();
        float anatomyScale = rig.Handle("Bip01 Pelvis") is { } hip && rig.Handle("Bip01 Neck") is { } neck
            ? Vector3.Distance(rig.BindPosition[hip], rig.BindPosition[neck]) / 6.1649f : 1;
        if (state.Severe.Kind != SevereHeadWoundKind.None && headRelief is null) gaps |= GeometryFidelityGap.SevereHeadRelief;
        if (headRelief is not null) general.AddRange(headRelief.Rules);
        if (inside is null && (state.Torso is not null || state.Head is not null || state.LeftArm is not null || state.RightArm is not null
            || state.LeftLeg is not null || state.RightLeg is not null || state.Severe.Kind != SevereHeadWoundKind.None)) gaps |= GeometryFidelityGap.MissingAnatomy;
        var groups = MeshCut.BoneGroups(rig);
        var headPlane = state.Head is { } hc ? ResolveHeadPlane(body, rig, appearance, hc.Plane) : default;
        if (state.Head is { } head)
        {
            if (head.Mode == HeadCutMode.DetachedSplit)
            {
                // A head region is either on a saved split plane or excluded as a whole. Non-head triangles remain on the owner.
                general.Add(new(new(0, 1, 0, -1_000_000), 1, 7));
                fragments.Add((GeometryFragmentKind.HeadPositive, 2, -1, headPlane, [new(headPlane, 1, 7, true, MorphedDistance: true)]));
                fragments.Add((GeometryFragmentKind.HeadNegative, 3, -1, headPlane, [new(headPlane, -1, 7, true, MorphedDistance: true)]));
            }
            else
            {
                general.Add(new(headPlane, -1, 7, MorphedDistance: true));
                fragments.Add((GeometryFragmentKind.HeadPositive, 2, -1, headPlane, [new(headPlane, 1, 7, true, MorphedDistance: true)]));
            }
        }
        foreach (var cut in new[] { state.LeftArm, state.RightArm, state.LeftLeg, state.RightLeg })
        {
            if (cut is not { } limb || !LimbCutGeometry.TryPlane(rig, limb, out var plane)) continue;
            general.Add(new(plane, 1, limb.LimbSlot + 1));
            fragments.Add((GeometryFragmentKind.Limb, 10 + limb.LimbSlot, limb.LimbSlot, plane, [new(plane, -1, limb.LimbSlot + 1, true)]));
        }
        var retainedRules = new List<CutHalfspace>(general);
        if (state.Torso is { } torso)
        {
            retainedRules.Add(new(torso.Plane, 1, LegacyTorso: !torso.FullBody));
            var droppedRules = new List<CutHalfspace>(general) { new(torso.Plane, -1, LegacyTorso: !torso.FullBody) };
            fragments.Insert(0, (GeometryFragmentKind.TorsoNegative, 1, -1, torso.Plane, droppedRules));
        }
        uint blood = unchecked((uint)appearance.Race.GetInt("blood colour", 0x800000)) & 0xFFFFFF;
        bool mechanical = appearance.Race.GetBool("is robot");
        var flesh = mechanical ? new Vector4(.28f, .31f, .32f, 1) : AnatomyGeometry.Recolour(new(.37f, .045f, .045f, 1), blood);
        bool shaved = appearance.Character?.GetBool("shaved") == true;
        var partSources = new Dictionary<string, OgreMesh?>(StringComparer.OrdinalIgnoreCase);
        OgreMesh? Source(string path)
        {
            if (!partSources.TryGetValue(path, out var mesh)) partSources[path] = mesh = loadPartMesh(path);
            return mesh;
        }
        (RuntimeCharacterGeometry Geometry, CutMeshResult Skin) Piece(IReadOnlyList<CutHalfspace> rules)
        {
            var cutBody = MeshCut.Clip(body, rig, rules, appearance.PoseWeights, shaved, flesh, skinPockets);
            var parts = new List<RuntimeCharacterPart>();
            void AddInside(OgreMesh? mesh, string label)
            {
                if (mesh is null || mesh.SubMeshes.All(s => s.Indices.Indices.Length == 0)) return;
                parts.Add(new(new CharacterPart { Record = appearance.Race, Mesh = "brutality-" + label, Field = label,
                    Mode = AttachMode.SharedSkeleton }, mesh, true));
            }
            foreach (var part in appearance.Parts)
            {
                if (part.Mode == AttachMode.Bone)
                {
                    var (point, bone) = Attachment(appearance, rig, part);
                    int group = bone >= 0 && bone < groups.Length ? groups[bone] : 0;
                    if (rules.All(rule => rule.Band(point, group) >= 0)) parts.Add(new(part));
                    continue;
                }
                if (Source(part.Mesh) is not { } source) { gaps |= GeometryFidelityGap.MissingPartMesh; continue; }
                bool hair = part.Slot is AttachSlot.Hair or AttachSlot.Beard;
                var cut = MeshCut.Clip(source, rig, rules, capColour: flesh, removeVolumes: hair ? headRelief?.Hair : clothPockets);
                if (cut.HasTriangles) parts.Add(new(part, cut.Skin));
                // The character skin supplies the section; capping each cloth shell would conceal its anatomy.
            }
            AddInside(cutBody.Wound, mechanical ? "mechanical-section" : "wound-section");
            if (inside is not null && !AnatomyExposure.Hidden)
            {
                // The reference shader exposes only opened tissue. Uninjured anatomical regions must never protrude through a retargeted body's intact skin.
                var exposed = AnatomyExposure.Filter(inside, rig, rules, skinPockets, anatomyScale);
                var internalCut = MeshCut.Clip(exposed, rig, rules, removeVolumes: headRelief?.Inside);
                if (internalCut.HasTriangles)
                {
                    var detailed = AnatomyDetail.WithLevels(internalCut.Skin, rig, rules);
                    parts.Add(new(new CharacterPart { Record = appearance.Race, Mesh = "brutality-anatomy", Field = "anatomy", Mode = AttachMode.SharedSkeleton }, detailed, true)
                        { ScreenLodThresholds = detailed.Lod is null ? null : AnatomyDetail.ScreenThresholds });
                }
                AddInside(internalCut.Wound, "anatomy-section");
            }
            if (bodyWounds.Relief is { } bodyRelief)
            {
                var placed = MeshCut.Clip(bodyRelief, rig, rules);
                if (placed.HasTriangles) AddInside(placed.Skin, "body-wound-relief");
            }
            if (headRelief?.Relief is { } relief)
            {
                var placed = MeshCut.Clip(relief, rig, rules);
                if (placed.HasTriangles) AddInside(placed.Skin, "head-relief");
            }
            return (new(cutBody.Skin, parts), cutBody);
        }
        var retained = Piece(retainedRules);
        var detached = new List<GeometryFragment>();
        foreach (var piece in fragments)
        {
            var built = Piece(piece.Rules);
            if (!built.Skin.HasTriangles) continue;
            detached.Add(new(piece.Kind, piece.Key, piece.Limb, built.Geometry, built.Skin.Min, built.Skin.Max, piece.Plane));
        }
        return new(retained.Geometry, detached.AsReadOnly(), retained.Skin.Min, retained.Skin.Max, gaps) { AbdomenAnchor = bodyWounds.AbdomenAnchor };
    }

    /// <summary>The saved normalized head plane is resolved against the current morphed head, as in the pinned renderer.</summary>
    internal static Vector4 ResolveHeadPlane(OgreMesh body, SkeletonRig rig, CharacterAppearance appearance, Vector4 normalized)
    {
        var points = HeadReliefGeometry.HeadPoints(body, rig, appearance);
        var (min, max) = AnatomyGeometry.Bounds(points);
        if (points.Length == 0)
        {
            var centre = rig.Handle("Bip01 Head") is { } head ? rig.BindPosition[head] : new Vector3(0, 18, 0);
            min = centre - new Vector3(1, 1.5f, 1); max = centre + new Vector3(1, 1.5f, 1);
        }
        var half = Vector3.Max((max - min) * .5f, new Vector3(.01f));
        var normal = new Vector3(normalized.X, normalized.Y, normalized.Z) / half;
        float length = normal.Length();
        return new Vector4(normal, normalized.W - Vector3.Dot(normal, (min + max) * .5f)) / length;
    }

    internal static (Vector3 Point, int Bone) Attachment(CharacterAppearance appearance, SkeletonRig rig, CharacterPart part)
    {
        var point = part.Point is { } name && appearance.AttachmentPoints.TryGetValue(name, out var found) ? found : null;
        int bone = point is null ? 0 : rig.Handle(point.Bone) ?? 0;
        if (rig.BoneCount == 0) return (Vector3.Zero, -1);
        bone = Math.Clamp(bone, 0, rig.BoneCount - 1);
        var offset = point is null ? Matrix4x4.Identity : Matrix4x4.CreateFromQuaternion(point.Rotation) * Matrix4x4.CreateTranslation(point.Position + part.PointOffset);
        if (part.Local is { } local) offset = local * offset;
        var bind = Matrix4x4.CreateScale(rig.BindScale[bone]) * Matrix4x4.CreateFromQuaternion(Quaternion.Inverse(rig.BindRotationInverse[bone])) * Matrix4x4.CreateTranslation(rig.BindPosition[bone]);
        return (Vector3.Transform(Vector3.Zero, offset * bind), bone);
    }
}
