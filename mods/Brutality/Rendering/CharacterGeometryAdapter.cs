using System.Numerics;
using System.Runtime.CompilerServices;
using Meitou.Brutality.Anatomy;
using Meitou.Brutality.Effects;
using Meitou.Brutality.Rendering.Surface;
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
    internal NativeGeometryInfo? Surface { get; init; }
    internal CutAnchorGeometry? CutAnchors { get; init; }
}

/// <summary>
/// Mod-owned CPU geometry. The bridge supplies the uncollapsed pre-cut appearance, original rig/meshes, and permanent gameplay state.
/// Results are cached by that immutable look, mesh identity and cut values, so rendering a frame never allocates another geometry variant.
/// </summary>
public sealed class CharacterGeometryAdapter(AnatomyLibrary anatomy)
{
    internal AnatomyLibrary Anatomy => anatomy;
    static readonly ConditionalWeakTable<RuntimeCharacterGeometry, NativeGeometryInfo> surfaceInfos = new();
    internal static NativeGeometryInfo? SurfaceFor(RuntimeCharacterGeometry geometry) => surfaceInfos.TryGetValue(geometry, out var info) ? info : null;
    internal static void AliasSurface(RuntimeCharacterGeometry source, RuntimeCharacterGeometry target)
    {
        if (!ReferenceEquals(source, target) && surfaceInfos.TryGetValue(source, out var info)) surfaceInfos.GetValue(target, _ => info);
    }
    readonly ConditionalWeakTable<CharacterAppearance, LookCache> looks = new();
    sealed class LookCache
    {
        public readonly Dictionary<StateKey, CharacterGeometryResult> Results = [];
    }
    internal readonly record struct StateKey(OgreMesh Body, SkeletonRig Rig, TorsoBisectionState? Torso, HeadCutState? Head,
        LimbCutState? LeftArm, LimbCutState? RightArm, LimbCutState? LeftLeg, LimbCutState? RightLeg, SevereHeadWoundState Severe, ulong Wounds, CharacterGeometryComponents Components);

    internal readonly record struct BuildKey(CharacterAppearance Appearance, StateKey State);
    internal sealed record BuildInput(BuildKey Key, CharacterAppearance Appearance, BrutalityActorView State,
        Func<string, OgreMesh?> LoadPartMesh);

    internal static BuildKey RequestKey(CharacterAppearance appearance, SkeletonRig rig, OgreMesh body,
        BrutalityActorView state, CharacterGeometryComponents components) => new(appearance, Key(body, rig, state, components));

    /// <summary>Capture the look and state. A concurrent asset reader can resolve part meshes on the worker.</summary>
    internal static BuildInput Capture(BuildKey key, BrutalityActorView state, Func<string, OgreMesh?> loadPartMesh, bool deferMeshReads = false)
    {
        var appearance = key.Appearance.Snapshot();
        if (!deferMeshReads)
        {
            var meshes = new Dictionary<string, OgreMesh?>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in appearance.Parts)
                if (!meshes.ContainsKey(part.Mesh)) meshes.Add(part.Mesh, loadPartMesh(part.Mesh));
            loadPartMesh = name => meshes.GetValueOrDefault(name);
        }
        var saved = state with { Limbs = Array.AsReadOnly(state.Limbs.ToArray()),
            Wounds = state.Wounds with { Entries = Array.AsReadOnly(state.Wounds.Entries.ToArray()) } };
        return new(key, appearance, saved, loadPartMesh);
    }

    /// <summary>CPU-only and unpublished: cancellation or a newer cut can discard the result without retiring a live variant.</summary>
    internal CharacterGeometryResult BuildUnpublished(BuildInput input, CancellationToken cancellation, bool prepareFragments = true)
    {
        cancellation.ThrowIfCancellationRequested();
        var key = input.Key.State;
        var native = NativeGeometry.Build(anatomy, input.Appearance, key.Rig, key.Body, key.Severe);
        cancellation.ThrowIfCancellationRequested();
        var result = BuildCore(input.Appearance, key.Rig, key.Body, input.LoadPartMesh,
            key, native.Anatomy, native.Relief, native.Info, input.State.Wounds, cancellation);
        surfaceInfos.Add(result.Retained, native.Info);
        foreach (var fragment in result.Detached) surfaceInfos.Add(fragment.Geometry, native.Info);
        if (!prepareFragments) return result;
        FragmentWorldBounds.Prepare(result.Retained, input.Appearance, key.Rig, key.Body, input.LoadPartMesh);
        foreach (var fragment in result.Detached)
        {
            cancellation.ThrowIfCancellationRequested();
            FragmentWorldBounds.Prepare(fragment.Geometry, input.Appearance, key.Rig, key.Body, input.LoadPartMesh);
        }
        cancellation.ThrowIfCancellationRequested();
        // Prepare bind-space rim samples with the cut meshes, before their first visible frame.
        var anchors = new CutAnchorGeometry();
        foreach (var fragment in result.Detached)
        {
            cancellation.ThrowIfCancellationRequested();
            int detachedSide = fragment.Kind == GeometryFragmentKind.HeadPositive ? 1 : -1;
            anchors.Samples(fragment.Geometry, fragment.CutPlane, detachedSide, key.Rig, input.Appearance);
            if (fragment.Kind != GeometryFragmentKind.HeadNegative)
                anchors.Samples(result.Retained, fragment.CutPlane, -detachedSide, key.Rig, input.Appearance);
        }
        return result with { CutAnchors = anchors };
    }

    /// <summary>Publish only after the producer has checked the world's current request; the original look remains the cache identity.</summary>
    internal CharacterGeometryResult Publish(BuildKey key, CharacterGeometryResult result)
    {
        var look = looks.GetOrCreateValue(key.Appearance);
        lock (look)
        {
            if (look.Results.TryGetValue(key.State, out var existing) && !existing.Retained.IsRetired)
            { if (!ReferenceEquals(existing, result)) Retire(result); return existing; }
            Store(look, key.State, result);
            return result;
        }
    }

    internal static void Retire(CharacterGeometryResult result)
    {
        result.Retained.Retire();
        foreach (var fragment in result.Detached) fragment.Geometry.Retire();
    }

    static void Store(LookCache look, StateKey key, CharacterGeometryResult result)
    {
        look.Results.Remove(key);
        while (look.Results.Count >= 8)
        {
            var oldest = look.Results.First(); look.Results.Remove(oldest.Key); Retire(oldest.Value);
        }
        look.Results[key] = result;
    }

    /// <summary>Looks up a published current-state mesh without importing anatomy or performing CPU cuts. Visual consumers must not bypass the producer's build budget.</summary>
    public bool TryCached(CharacterAppearance appearance, SkeletonRig rig, OgreMesh body, BrutalityActorView state,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CharacterGeometryResult? result, CharacterGeometryComponents? components = null)
    {
        result = null;
        if (!looks.TryGetValue(appearance, out var look)) return false;
        var key = Key(body, rig, state, components.GetValueOrDefault());
        lock (look)
        {
            if (look.Results.TryGetValue(key, out var existing) && !existing.Retained.IsRetired)
            { result = existing; return true; }
            if (components is not null) return false;
            // Effects only consume an already published result; they need not reconstruct the producer's physical-map key.
            foreach (var pair in look.Results)
                if ((pair.Key with { Components = default }) == key && !pair.Value.Retained.IsRetired) result = pair.Value;
            return result is not null;
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

    static StateKey Key(OgreMesh body, SkeletonRig rig, BrutalityActorView state, CharacterGeometryComponents components)
    {
        var limbs = new LimbCutState?[4];
        foreach (var cut in state.Limbs) if (cut.IsValid) limbs[cut.LimbSlot] ??= cut;
        return new(body, rig, state.Torso is { IsValid: true } torso ? torso : null,
            state.Head is { IsValid: true } head ? head : null, limbs[0], limbs[1], limbs[2], limbs[3], state.SevereHead with { HeavyHits = 0 }, (state.Wounds.BellyHits >= 10 ? 1UL : 0UL), components);
    }

    public CharacterGeometryResult Build(CharacterAppearance appearance, SkeletonRig rig, OgreMesh body,
        Func<string, OgreMesh?> loadPartMesh, BrutalityActorView state, CharacterGeometryComponents components = default)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        ArgumentNullException.ThrowIfNull(rig);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(loadPartMesh);
        var key = Key(body, rig, state, components);
        var look = looks.GetOrCreateValue(appearance);
        lock (look)
        {
            if (look.Results.TryGetValue(key, out var existing) && !existing.Retained.IsRetired) return existing;
            look.Results.Remove(key);
            var native = NativeGeometry.Build(anatomy, appearance, rig, body, key.Severe);
            var result = BuildCore(appearance, rig, body, loadPartMesh, key, native.Anatomy, native.Relief, native.Info, state.Wounds);
            surfaceInfos.Add(result.Retained, native.Info);
            foreach (var fragment in result.Detached) surfaceInfos.Add(fragment.Geometry, native.Info);
            Store(look, key, result);
            return result;
        }
    }

    static CharacterGeometryResult BuildCore(CharacterAppearance appearance, SkeletonRig rig, OgreMesh body,
        Func<string, OgreMesh?> loadPartMesh, in StateKey state, OgreMesh? inside, OgreMesh? nativeRelief, NativeGeometryInfo surface, BodyWoundState wounds, CancellationToken cancellation = default)
    {
        var general = new List<CutHalfspace>();
        var fragments = new List<(GeometryFragmentKind Kind, int Key, int Limb, Vector4 Plane, List<CutHalfspace> Rules)>();
        var gaps = GeometryFidelityGap.None;
        // Wounds remain shader fields; CPU construction is used only for the first physical bowel anchor.
        var bodyWounds = BodyWoundGeometry.Build(body, rig, appearance, wounds, anchorsOnly: true);
        if (!appearance.Headless && state.Severe.Kind is SevereHeadWoundKind.Scalp or SevereHeadWoundKind.BrokenSkull && !surface.HeadReliefReady)
            gaps |= GeometryFidelityGap.SevereHeadRelief;
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
        var componentMaps = state.Components;
        bool detachedHead = state.Head is { Mode: HeadCutMode.NeckDetach or HeadCutMode.DetachedSplit };
        float originalRadius = MeshCut.SourceModel(body, rig.BoneCount).Radius;
        (RuntimeCharacterGeometry Geometry, CutMeshResult Skin) Piece(IReadOnlyList<CutHalfspace> rules, int stableKey)
        {
            cancellation.ThrowIfCancellationRequested();
            var physicalComponents = componentMaps.For(stableKey);
            var cutBody = GeneratedExteriorLod.Clip(body, rig, rules, appearance.PoseWeights, shaved, flesh, components: physicalComponents);
            var parts = new List<RuntimeCharacterPart>();
            bool headOnly = stableKey is 2 or 3;
            var pieceInside = inside is null ? null : headOnly ? NativeAnatomyRegions.Select(inside, rig, true)
                : detachedHead ? NativeAnatomyRegions.Select(inside, rig, false) : inside;
            // The pin's 180-pixel budget is measured on the whole actor. A head's actual culling sphere is smaller.
            float detailScale = Math.Clamp(Vector3.Distance(cutBody.Min, cutBody.Max) * .5f / originalRadius, .01f, 1);
            float anatomyLodScale = pieceInside is null || inside is null || ReferenceEquals(pieceInside, inside) ? 1
                : Math.Clamp((pieceInside.Bounds?.Radius ?? 1) / MathF.Max(.001f, inside.Bounds?.Radius ?? 1), .001f, 1);
            var detailThresholds = Array.AsReadOnly(AnatomyDetail.ScreenThresholds.Select(value => value * anatomyLodScale).ToArray());
            void AddInside(OgreMesh? mesh, string label)
            {
                if (mesh is null || mesh.SubMeshes.All(s => s.Indices.Indices.Length == 0)) return;
                parts.Add(new(new CharacterPart { Record = appearance.Race, Mesh = "brutality-" + label, Field = label,
                    Mode = AttachMode.SharedSkeleton }, mesh, true)
                {
                    CastsShadows = mechanical || label is "wound-section" or "mechanical-section",
                    DrawFlags = NativeSurfaceShaders.WoundFlag | NativeSurfaceShaders.AnatomyFlag,
                    MaterialFlags = mechanical ? NativeSurfaceShaders.Mechanical : 0, PreserveRawVertexChannels = true, ShowInPortrait = false, MinimumScreenPixels = 180 * detailScale, DoubleSided = true,
                    ScreenLodThresholds = mesh.Lod is { Levels.Count: > 0 } ? detailThresholds : null,
                });
            }
            foreach (var part in appearance.Parts)
            {
                cancellation.ThrowIfCancellationRequested();
                if (part.Mode == AttachMode.Bone)
                {
                    var (point, bone) = Attachment(appearance, rig, part);
                    int group = bone >= 0 && bone < groups.Length ? groups[bone] : 0;
                    if (rules.All(rule => rule.Band(point, group) >= 0)) parts.Add(new(part));
                    continue;
                }
                if (Source(part.Mesh) is not { } source) { gaps |= GeometryFidelityGap.MissingPartMesh; continue; }
                bool hair = part.Slot is AttachSlot.Hair or AttachSlot.Beard;
                float partLodScale = part.Record.Type == Meitou.Data.Fcs.FcsRecordType.ATTACHMENT && appearance.Animal is null ? 4 : 1;
                var cut = GeneratedExteriorLod.Clip(source, rig, rules, capColour: flesh, components: physicalComponents, lodScale: partLodScale);
                if (cut.HasTriangles) parts.Add(new(part, rules.Count == 0 ? null : cut.Skin)
                { DrawFlags = (part.Record.GetInt("material type") == 3 ? NativeSurfaceShaders.PlateFlag : NativeSurfaceShaders.TearFlag)
                    | (hair ? NativeSurfaceShaders.HairWoundFlag : 0) });
                // The character skin supplies the section; capping each cloth shell would conceal its anatomy.
            }
            if (rules.Count > 0)
                AddInside(NativeGeometry.SectionCaps(appearance, rig, body, rules, physicalComponents, blood, mechanical, pieceInside), mechanical ? "mechanical-section" : "wound-section");
            // Keep every fitted layer. The original shader masks exposure per pixel and shares the same planes in all passes.
            if (pieceInside is not null && !AnatomyExposure.Hidden) AddInside(pieceInside, "anatomy");
            if (nativeRelief is not null && (!detachedHead || headOnly)) AddInside(nativeRelief, "head-relief");
            uint bodyFlags = NativeSurfaceShaders.WoundFlag | (surface.HeadReliefReady ? NativeSurfaceShaders.HeadReliefFlag : 0);
            return (new(rules.Count == 0 ? null : cutBody.Skin, parts, NativeSurfaceShaders.CreateSet(), bodyFlags: bodyFlags, bodyMaterialFlags: mechanical ? NativeSurfaceShaders.Mechanical : 0, bodyDoubleSided: true), cutBody);
        }
        var retained = Piece(retainedRules, 0);
        var detached = new List<GeometryFragment>();
        foreach (var piece in fragments)
        {
            var built = Piece(piece.Rules, piece.Key);
            if (!built.Skin.HasTriangles) continue;
            detached.Add(new(piece.Kind, piece.Key, piece.Limb, built.Geometry, built.Skin.Min, built.Skin.Max, piece.Plane));
        }
        return new(retained.Geometry, detached.AsReadOnly(), retained.Skin.Min, retained.Skin.Max, gaps) { AbdomenAnchor = bodyWounds.AbdomenAnchor, Surface = surface };
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
