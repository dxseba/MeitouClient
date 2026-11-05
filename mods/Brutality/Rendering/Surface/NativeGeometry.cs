using System.Numerics;
using System.Runtime.CompilerServices;
using Meitou.Brutality.Anatomy;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;
using Meitou.Rendering;

namespace Meitou.Brutality.Rendering.Surface;

/// <summary>The same original-skin samples and fitted frame are used for both geometry and the shader header.</summary>
internal sealed record NativeGeometryInfo(WoundPoint[] Skin, Vertex[] HeadSource, HeadWoundFrame? HeadFrame, bool HeadReliefReady)
{
    // Compact head/body views copy their triangles. Keep their immutable full-fit identity alive while an actual geometry uses them.
    public OgreMesh? FittedAnatomy { get; init; }
}

internal static class NativeGeometry
{
    sealed record Source(WoundPoint[] Skin, Vertex[] Head, Vertex[] Body, NativeMeshMorphs? Morphs)
    {
        public Vertex[]? HeadSkin, MechanicalHeadSkin;
    }
    sealed class Sources { public readonly Dictionary<SkeletonRig, Source> ByRig = []; }
    readonly record struct FitKey(OgreMesh Body, SkeletonRig Rig, bool Female, uint Blood, bool Robot, bool Headless, bool HeadOnly);
    static readonly ConditionalWeakTable<OgreMesh, Sources> sources = new();
    static readonly ConditionalWeakTable<AnatomyLibrary, AnatomyFitCache<FitKey>> fits = new();
    static readonly ConditionalWeakTable<OgreMesh, Vertex[]> brainSources = new();

    /// <summary>Configure a library's bounded fit storage before any fit is requested.</summary>
    internal static void ConfigureFitBudget(AnatomyLibrary library, long byteBudget) => fits.Add(library, new(byteBudget));
    internal static long CachedFitBytes(AnatomyLibrary library) => fits.TryGetValue(library, out var cache) ? cache.Bytes : 0;

    static Source Original(OgreMesh body, SkeletonRig rig)
    {
        var cache = sources.GetValue(body, static _ => new());
        lock (cache)
        {
            if (cache.ByRig.TryGetValue(rig, out var ready)) return ready;
            var model = Model.Build(body, rig.BoneCount, gameBinormals: true);
            var morphs = NativeMeshMorphs.From(body, model, [], "Brutality body");
            var skin = model.Parts.SelectMany(part => part.Vertices).Where((_, i) => i % 2 == 0)
                .Select(v => new WoundPoint(v.Position, v.Normal, v.Uv, (int)(v.Bones & 255), v.Bones, v.Weights)).ToArray();
            var source = new Source(skin, NativeHeadSurface.Source(model),
                model.Parts.SelectMany(part => part.Indices.Select(index => part.Vertices[index])).ToArray(), morphs);
            cache.ByRig.Add(rig, source);
            return source;
        }
    }

    public static (OgreMesh? Anatomy, OgreMesh? Relief, NativeGeometryInfo Info) Build(AnatomyLibrary library,
        CharacterAppearance appearance, SkeletonRig rig, OgreMesh body, SevereHeadWoundState severe)
    {
        var source = Original(body, rig);
        uint blood = unchecked((uint)appearance.Race.GetInt("blood colour", 0x800000)) & 0xFFFFFF;
        bool robot = appearance.Race.GetBool("is robot");
        var key = new FitKey(body, rig, appearance.Female, blood, robot, appearance.Headless, appearance.HeadOnly);
        var anatomy = Fit(library, rig, body, key, source);
        fits.GetValue(library, static _ => new()).SharePublished(key, anatomy);
        OgreMesh? relief = null;
        HeadWoundFrame? fitted = null;
        bool reliefReady = false;
        if (!appearance.Headless && !robot && severe.Kind is SevereHeadWoundKind.Scalp or SevereHeadWoundKind.BrokenSkull
            && rig.Handle("Bip01 Head") is { } headBone)
        {
            var delta = source.Morphs?.Deltas(appearance.PoseWeights, appearance.Character?.GetBool("shaved") == true);
            Vertex[]? placed = null;
            int profile = Math.Clamp(severe.Profile, 0, 5);
            if (severe.Placement is { IsValid: true } estimate)
            {
                var placement = new NativeHeadPlacement(estimate.Point, estimate.Normal, estimate.Tangent, estimate.LengthScale, estimate.WidthScale);
                if (NativeHeadSurface.TryProjectPlacement(source.Head, placement, profile, out var frame, (int)severe.Mechanism))
                    placed = NativeHeadSurface.FitPlacementVertices(source.Head, frame, headBone, delta, profile,
                        severe.Kind == SevereHeadWoundKind.BrokenSkull, out var projected, blood) is { } mesh
                        ? SetFrame(mesh, projected) : null;
            }
            if (placed is null)
            {
                var fallback = new List<Vertex>();
                uint supported = NativeHeadSurface.Append(fallback, source.Head, headBone, delta, blood);
                reliefReady = (supported & 1u << profile) != 0;
                if (reliefReady) placed = fallback.ToArray();
            }
            if (placed is { Length: > 0 }) relief = MeshCut.FromTriangles(placed, body.SkeletonName, preserveRawVertexChannels: true);
        }
        return (anatomy, relief, new(source.Skin, source.Head, fitted, reliefReady) { FittedAnatomy = anatomy });

        Vertex[] SetFrame(Vertex[] vertices, HeadWoundFrame frame)
        { fitted = frame; reliefReady = true; return vertices; }
    }

    /// <summary>Immutable fit inputs only: worker preparation never reads a mutable actor or appearance.</summary>
    internal static (OgreMesh? Anatomy, NativeGeometryInfo Info) Prepare(AnatomyLibrary library, SkeletonRig rig,
        OgreMesh body, bool female, uint blood, bool robot)
    {
        var source = Original(body, rig);
        var key = new FitKey(body, rig, female, blood & 0xFFFFFF, robot, false, false);
        var anatomy = Fit(library, rig, body, key, source);
        fits.GetValue(library, static _ => new()).SharePublished(key, anatomy);
        return (anatomy, new(source.Skin, source.Head, null, false) { FittedAnatomy = anatomy });
    }

    static OgreMesh? Fit(AnatomyLibrary library, SkeletonRig rig, OgreMesh body, FitKey key, Source source)
    {
        return fits.GetValue(library, static _ => new()).GetOrCreate(key, () =>
        {
            List<Vertex>? vertices;
            if (key.Robot)
            {
                vertices = MechanicalAnatomy.Build(rig, source.Body, source.Head, key.Headless, key.HeadOnly);
                if (vertices is not null)
                    for (int i = 0; i < vertices.Count; i++)
                    {
                        var v = vertices[i];
                        int bone = (int)(v.Bones & 255);
                        int group = NativeWoundMasks.BodyGroup(rig.ByHandle[bone]?.Name ?? "");
                        vertices[i] = v with { Tangent = new(v.Position, 34), Colour = v.Colour with { W = group } };
                    }
            }
            else if (library.PathFor(AnatomyKind.Body) is { } path)
                vertices = NativeAnatomy.Build(rig, source.Skin, source.Head, key.Female, source.Morphs,
                    key.Headless, key.HeadOnly, path, key.Blood, source.Body);
            else vertices = null;
            if (vertices is not { Count: > 0 }) return null;
            var mesh = MeshCut.FromTriangles(vertices, body.SkeletonName, preserveRawVertexChannels: true);
            // As in the reference, anatomy levels are shared independently of active wounds and never mutate after publication.
            var fitted = AnatomyDetail.WithLevels(mesh, rig, [], preserveRawVertexChannels: true);
            if (Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_PREVIEW") is not null)
                Console.WriteLine($"Brutality fit: body={RuntimeHelpers.GetHashCode(body)}, rig={RuntimeHelpers.GetHashCode(rig)}, female={key.Female}, robot={key.Robot}, bytes={AnatomyFitCache<FitKey>.EstimateBytes(fitted)}");
            return fitted;
        });
    }

    /// <summary>The punched tunnel's wall for this look, or null when too few of its rays cross the torso skin.</summary>
    public static OgreMesh? ChestHoleLining(CharacterAppearance appearance, SkeletonRig rig, OgreMesh body, in ChestHoleFrame frame, uint blood)
    {
        var source = Original(body, rig);
        var delta = source.Morphs?.Deltas(appearance.PoseWeights, appearance.Character?.GetBool("shaved") == true);
        return ChestHoleGeometry.Lining(source.Body, delta, rig, frame, blood, body.SkeletonName);
    }

    public static OgreMesh? SectionCaps(CharacterAppearance appearance, SkeletonRig rig, OgreMesh body,
        IReadOnlyList<CutHalfspace> rules, int[]? components, uint blood, bool mechanical, OgreMesh? anatomy = null)
    {
        var source = Original(body, rig);
        Vertex[]? headSkin = null;
        if (rules.Any(rule => rule.Group == 7))
            lock (source)
                headSkin = mechanical ? source.MechanicalHeadSkin ??= NativeCapGeometry.SelectHeadSkin(source.Body, rig)
                    : source.HeadSkin ??= NativeCapGeometry.SelectHeadSkin(source.Head, rig);
        Vertex[]? brain = null;
        if (!mechanical && anatomy is not null && rules.Any(rule => !rule.LegacyTorso && rule.Group is not (>= 1 and <= 4)))
            brain = brainSources.GetValue(anatomy, mesh => Model.Build(mesh, rig.BoneCount, preserveRawVertexChannels: true).Parts
                .SelectMany(part => part.Vertices).Where(vertex => vertex.Tangent.W == 6).ToArray());
        return NativeCapGeometry.Build(source.Body, rig, source.Morphs, appearance, rules, components, blood, mechanical, body.SkeletonName, brain, headSkin);
    }
}
