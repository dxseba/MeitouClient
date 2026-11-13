using Meitou.Game.Rendering.Foliage;
using Meitou.Game.Rendering.Frame;
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;
using Meitou.Modding;

namespace Meitou.Brutality;

/// <summary>Gives the stock character renderer generated meshes while retaining its original appearance and rig.</summary>
internal static class StockHeadMeshes
{
    const string PathPrefix = "__meitou_brutality_generated__/";
    const int MeshLimit = 144;
    const long ByteLimit = 64L * 1024 * 1024;
    // Fresh cuts get a small live reserve while older bodies are still moving. Native cache retirement keeps its hard ceiling.
    const int ForegroundMeshLimit = 168;
    const long ForegroundByteLimit = 80L * 1024 * 1024;
    const int ProcessMeshLimit = 256;
    const long ProcessByteLimit = 96L * 1024 * 1024;

    enum Surface { Skin, Wound, Anatomy, Section, Bruise, Equipment, WeaponBlood, GroundWeapon }

    sealed record Entry(OgreMesh Mesh, string? SkeletonName, Surface Surface, string? OriginalMesh = null);
    sealed record Variant(string[] Paths);

    static readonly ConcurrentDictionary<string, Entry> meshes = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<CharacterAppearance, Variant> variants = new(ReferenceEqualityComparer.Instance);
    static readonly Dictionary<string, int> pathReferences = new(StringComparer.OrdinalIgnoreCase);
    static readonly ConditionalWeakTable<OgreMesh, Entry> generatedMeshes = new();
    static readonly ConditionalWeakTable<Model, Entry> generatedModels = new();
    static readonly ConditionalWeakTable<CharacterAppearance, CharacterAppearance> woundPoseSources = new();
    static readonly object admission = new();
    static readonly MethodInfo? setBodyMesh = AccessTools.PropertySetter(typeof(CharacterAppearance), nameof(CharacterAppearance.BodyMesh));
    static readonly MethodInfo? setHeadOnly = AccessTools.PropertySetter(typeof(CharacterAppearance), nameof(CharacterAppearance.HeadOnly));
    static readonly MethodInfo? setHeadless = AccessTools.PropertySetter(typeof(CharacterAppearance), nameof(CharacterAppearance.Headless));
    static readonly MethodInfo? setHiddenLimbs = AccessTools.PropertySetter(typeof(CharacterAppearance), nameof(CharacterAppearance.HiddenLimbs));
    static readonly MethodInfo? setHiddenParts = AccessTools.PropertySetter(typeof(CharacterAppearance), nameof(CharacterAppearance.HiddenParts));
    static readonly MethodInfo readFile = AccessTools.Method(typeof(OgreMeshReader), nameof(OgreMeshReader.ReadFile), [typeof(string)]);
    static MethodInfo? woundMaterial;
    static ConstructorInfo? anatomyMaterialConstructor;
    static FieldInfo? vertexColoured, doubleSided, description;
    static volatile bool installed;
    static volatile bool drawsTissue;
    static volatile bool drawsVertexColours;
    static long nextKey;
    static int retainedMeshes, committedMeshes, groundCommittedMeshes;
    static long retainedBytes;
    static long totalBytes, groundCommittedBytes;

    public static bool Install(ModContext context)
    {
        if (installed) return true;
        var content = typeof(Model).Assembly.GetType("Meitou.Game.Rendering.Characters.CharacterContent");
        if (content is null || setBodyMesh is null || setHeadOnly is null) return false;
        var findMesh = AccessTools.Method(content, "FindMesh", [typeof(string)]);
        if (findMesh is null) return false;

        try
        {
            context.Harmony.Patch(findMesh, prefix: Patch(nameof(FindMeshPrefix)));
            context.Harmony.Patch(AccessTools.Method(typeof(CharacterRigs), nameof(CharacterRigs.BodySkeletonName)), prefix: Patch(nameof(BodySkeletonPrefix)));
            context.Harmony.Patch(AccessTools.Method(typeof(CharacterRigs), nameof(CharacterRigs.For), [typeof(CharacterAppearance)]),
                prefix: Patch(nameof(WoundPoseSourcePrefix)));
            // The host defers JIT warm-up until code mods initialize. Patch the reader directly to leave PrepareMesh's exception filter intact.
            context.Harmony.Patch(readFile, prefix: Patch(nameof(ReadMeshPrefix)));
        }
        catch (Exception e)
        {
            context.LogError($"stock generated head meshes unavailable: {e.GetBaseException().Message}");
            return false;
        }

        try
        {
            context.Harmony.Patch(AccessTools.Method(content, "GeneratedLevels"), prefix: Patch(nameof(GeneratedLevelsPrefix)));
            context.Harmony.Patch(AccessTools.Method(typeof(Model), nameof(Model.Build)), postfix: Patch(nameof(ModelBuilt)));
            context.Harmony.Patch(AccessTools.Method(content, "CapMesh"), prefix: Patch(nameof(CapMeshPrefix)));
            context.Harmony.Patch(AccessTools.Method(content, "MarkCurvature"), prefix: Patch(nameof(MarkCurvaturePrefix)));
            // Stock writes a worn mesh's hide and blood-part bits into its colour's Y, which would overwrite tissue green.
            if (AccessTools.Method(content, "MarkWorn") is { } markWorn) context.Harmony.Patch(markWorn, prefix: Patch(nameof(MarkCurvaturePrefix)));
            drawsVertexColours = true;
        }
        catch (Exception e)
        {
            context.LogError($"stock generated mesh preparation optimizations unavailable: {e.GetBaseException().Message}");
        }

        woundMaterial = AccessTools.Method(content, "WoundMaterial", [typeof(CharacterAppearance)]);
        var itemMaterial = AccessTools.Method(content, "ItemMaterial");
        if (woundMaterial is not null && itemMaterial is not null && woundMaterial.ReturnType == itemMaterial.ReturnType)
        {
            try
            {
                var materialType = woundMaterial.ReturnType;
                anatomyMaterialConstructor = AccessTools.Constructor(materialType, Type.EmptyTypes);
                vertexColoured = AccessTools.Field(materialType, "VertexColoured");
                doubleSided = AccessTools.Field(materialType, "DoubleSided");
                description = AccessTools.Field(materialType, "Description");
                context.Harmony.Patch(itemMaterial, postfix: Patch(nameof(WeaponMaterialPostfix)), transpiler: Patch(nameof(TissueMaterialTranspiler)));
                drawsTissue = true;
                StockSectionDetail.Install(context);
                StockBruiseGeometry.Install(context);
                StockVestTears.Install(context);
            }
            catch (Exception e)
            {
                context.LogError($"stock cut surfaces unavailable: {e.GetBaseException().Message}");
            }
        }

        StockGeneratedMeshCache.Install(context, content);
        installed = true;
        return true;
    }

    static HarmonyMethod Patch(string method) => new(typeof(StockHeadMeshes), method);

    public static bool CanCreate
    {
        get
        {
            lock (admission)
                return installed && retainedMeshes < MeshLimit && retainedBytes < ByteLimit &&
                    committedMeshes < ProcessMeshLimit && totalBytes < ProcessByteLimit;
        }
    }

    internal readonly record struct Cost(int Meshes, long Bytes);
    internal enum Capacity { Available, LiveLimit, PendingRetirement, TooLarge }

    internal static Cost TorsoCost(IReadOnlyList<StockTorsoBuild.Piece> pieces)
    {
        int count = 0;
        long bytes = 0;
        foreach (var piece in pieces)
        {
            count++;
            bytes += EstimateBytes(piece.Body);
            if (drawsTissue)
                foreach (var (mesh, _) in piece.Tissue) Add(mesh);
            foreach (var (_, mesh) in piece.Equipment)
                if (mesh is not null) Add(mesh);
        }
        return new(count, bytes);

        void Add(OgreMesh mesh)
        {
            if (!HasTriangles(mesh)) return;
            count++;
            bytes += EstimateBytes(mesh);
        }
    }

    internal static Cost WoundCost(StockWoundBuild.Geometry geometry)
    {
        int count = 0;
        long bytes = 0;
        for (StockWoundBuild.Geometry? piece = geometry; piece is not null; piece = piece.Detached)
        {
            if (piece.Body is { } body) Add(body);
            foreach (var (mesh, _) in piece.Tissue) Add(mesh);
            foreach (var (_, mesh) in piece.Equipment)
                if (mesh is not null) Add(mesh);
        }
        return new(count, bytes);

        void Add(OgreMesh mesh)
        {
            if (!HasTriangles(mesh)) return;
            count++;
            bytes += EstimateBytes(mesh);
        }
    }

    internal static Cost HeadCost(IReadOnlyList<Meitou.Brutality.Rendering.PreparedHeadBurstShard> pieces,
        IReadOnlyDictionary<int, IReadOnlyList<(CharacterPart Part, OgreMesh? Mesh)>>? equipment = null)
    {
        int count = pieces.Count;
        long bytes = 0;
        foreach (var piece in pieces)
        {
            bytes += EstimateBytes(piece.Body);
            if (equipment?.GetValueOrDefault(piece.Key) is { } parts)
                foreach (var (_, mesh) in parts)
                {
                    if (mesh is null || !HasTriangles(mesh)) continue;
                    count++;
                    bytes += EstimateBytes(mesh);
                }
            if (!drawsTissue) continue;
            foreach (var (mesh, _) in piece.Parts)
            {
                if (!HasTriangles(mesh)) continue;
                count++;
                bytes += EstimateBytes(mesh);
            }
        }
        return new(count, bytes);
    }

    internal static Capacity TorsoCapacity(Cost cost, bool foreground = false)
    {
        lock (admission)
        {
            if (cost.Meshes <= 0 || cost.Meshes > MeshLimit || cost.Bytes > ByteLimit) return Capacity.TooLarge;
            if (cost.Meshes > (foreground ? ForegroundMeshLimit : MeshLimit) - retainedMeshes
                || cost.Bytes > (foreground ? ForegroundByteLimit : ByteLimit) - retainedBytes) return Capacity.LiveLimit;
            if (cost.Meshes > ProcessMeshLimit - committedMeshes || cost.Bytes > ProcessByteLimit - totalBytes) return Capacity.PendingRetirement;
            return Capacity.Available;
        }
    }

    public static CharacterAppearance? Create(CharacterAppearance original, OgreMesh skin, IReadOnlyList<(OgreMesh Mesh, string Label)> tissue,
        IReadOnlyList<(CharacterPart Part, OgreMesh? Mesh)>? equipment = null)
    {
        if (!installed) return null;
        var surfaces = drawsTissue ? tissue.Where(surface => HasTriangles(surface.Mesh)).ToArray() : [];
        bool hasSkin = HasTriangles(skin);
        if (!hasSkin && surfaces.Length == 0) return null;
        string? skeleton = skin.SkeletonName;
        if (string.IsNullOrEmpty(skeleton)) skeleton = surfaces.Select(surface => surface.Mesh.SkeletonName).FirstOrDefault(name => !string.IsNullOrEmpty(name));
        if (string.IsNullOrEmpty(skeleton)) return null;
        if (!hasSkin) skin = new OgreMesh { SkeletonName = skeleton, SkeletallyAnimated = true };
        var look = original.SeveredHead();
        look.Parts.Clear();
        setHeadOnly!.Invoke(look, [false]);
        return Register(look, skin, skeleton, surfaces, equipment ?? [], allVertexColours: false);
    }

    public static CharacterAppearance? CreateTorso(CharacterAppearance original, OgreMesh body,
        IReadOnlyList<(OgreMesh Mesh, string Label)> tissue, IReadOnlyList<(CharacterPart Part, OgreMesh? Mesh)> equipment,
        int? hiddenLimbs = null, bool showHead = false, bool foreground = false)
    {
        if (!installed || setHeadless is null || setHiddenLimbs is null || setHiddenParts is null
            || !HasTriangles(body) || string.IsNullOrEmpty(body.SkeletonName)) return null;
        var surfaces = drawsTissue ? tissue.Where(surface => HasTriangles(surface.Mesh)).ToArray() : [];

        // The native copy already keeps body textures, overlays, morphs and shape. Restore the full body's
        // visibility and attachment information, then insert only the equipment belonging to this side.
        var look = CopyFull(original, hiddenLimbs);
        if (showHead) setHeadless.Invoke(look, [false]);
        return Register(look, body, body.SkeletonName, surfaces, equipment, allVertexColours: true, foreground: foreground);
    }

    public static CharacterAppearance? CreateWeapon(CharacterAppearance original, IReadOnlyList<(CharacterPart Part, OgreMesh Mesh)> painted)
    {
        if (!installed || !drawsTissue || !drawsVertexColours || vertexColoured is null
            || setHeadless is null || setHiddenLimbs is null || setHiddenParts is null || painted.Count == 0) return null;
        var byPart = new Dictionary<CharacterPart, OgreMesh>(ReferenceEqualityComparer.Instance);
        foreach (var (part, mesh) in painted) byPart[part] = mesh;
        var equipment = original.Parts.Select(part => (part, byPart.GetValueOrDefault(part))).ToArray();
        var look = CopyFull(original);
        look.Lights.AddRange(original.Lights);
        return Register(look, null, null, [], equipment, allVertexColours: false, equipmentSurface: Surface.WeaponBlood);
    }

    public static bool IsGeneratedBody(CharacterAppearance look) => IsSynthetic(look.BodyMesh);

    public static CharacterAppearance? CreateWounds(CharacterAppearance original, OgreMesh? body,
        IReadOnlyList<(OgreMesh Mesh, string Label)> tissue, IReadOnlyList<(CharacterPart Part, OgreMesh? Mesh)> equipment,
        bool showHead = false, bool foreground = false, bool materialOnly = false)
    {
        if (!installed || !drawsTissue || !drawsVertexColours || vertexColoured is null || setHeadless is null
            || setHiddenLimbs is null || setHiddenParts is null
            || body is not null && (!HasTriangles(body) || string.IsNullOrEmpty(body.SkeletonName))) return null;
        var surfaces = tissue.Where(surface => HasTriangles(surface.Mesh)).ToArray();
        var look = CopyFull(original);
        if (showHead) setHeadless.Invoke(look, [false]);
        look.Lights.AddRange(original.Lights);
        var result = Register(look, body, body?.SkeletonName, surfaces, equipment, allVertexColours: true, foreground: foreground,
            materialOnly: materialOnly);
        // The renderer reads the edited mesh, while the rig keeps the intact body's sliders and grounding.
        if (result is not null && body is not null)
        {
            if (woundPoseSources.TryGetValue(original, out var source)) woundPoseSources.Add(result, source);
            else if (!IsGeneratedBody(original)) woundPoseSources.Add(result, original);
        }
        return result;
    }

    static CharacterAppearance CopyFull(CharacterAppearance original, int? hiddenLimbs = null)
    {
        var look = original.SeveredHead();
        look.Parts.Clear();
        setHeadOnly!.Invoke(look, [original.HeadOnly]);
        setHeadless!.Invoke(look, [original.Headless]);
        setHiddenLimbs!.Invoke(look, [hiddenLimbs ?? original.HiddenLimbs]);
        setHiddenParts!.Invoke(look, [original.HiddenParts]);
        foreach (var (name, point) in original.AttachmentPoints) look.AttachmentPoints.Add(name, point);
        return look;
    }

    static CharacterAppearance? Register(CharacterAppearance look, OgreMesh? body, string? skeleton,
        IReadOnlyList<(OgreMesh Mesh, string Label)> surfaces, IReadOnlyList<(CharacterPart Part, OgreMesh? Mesh)> equipment,
        bool allVertexColours, Surface equipmentSurface = Surface.Equipment, bool foreground = false, bool materialOnly = false)
    {
        long bytes = (body is null ? 0 : EstimateBytes(body)) + surfaces.Sum(surface => EstimateBytes(surface.Mesh));
        int count = (body is null ? 0 : 1) + surfaces.Count;
        foreach (var (_, mesh) in equipment)
        {
            if (mesh is null || !HasTriangles(mesh)) continue;
            count++;
            bytes += EstimateBytes(mesh);
        }

        // Keep the process allowance reserved while retired geometry still occupies native caches.
        lock (admission)
        {
            if (count == 0 && !materialOnly || retainedMeshes + count > (foreground ? ForegroundMeshLimit : MeshLimit)
                || bytes > (foreground ? ForegroundByteLimit : ByteLimit) - retainedBytes ||
                committedMeshes + count > ProcessMeshLimit || bytes > ProcessByteLimit - totalBytes) return null;
            string[] paths = new string[count];
            int index = 0;
            if (body is not null)
            {
                string bodyPath = Add(new(body, skeleton, Surface.Skin));
                setBodyMesh!.Invoke(look, [bodyPath]);
            }
            foreach (var (part, mesh) in equipment)
            {
                if (mesh is null) look.Parts.Add(part);
                else if (HasTriangles(mesh))
                {
                    string path = Add(new(mesh, null, equipmentSurface, OriginalMaterialPath(part.Mesh)));
                    look.Parts.Add(part with { Mesh = path });
                }
            }
            foreach (var (mesh, label) in surfaces)
            {
                string path = Add(new(mesh, null, TissueSurface(label, allVertexColours)));
                look.Parts.Add(new CharacterPart
                {
                    Record = look.Race,
                    Mesh = path,
                    Field = label,
                    Mode = AttachMode.SharedSkeleton,
                });
            }

            var dependencies = GeneratedPaths(look).Where(pathReferences.ContainsKey).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (string path in dependencies) pathReferences[path]++;
            variants.Add(look, new(dependencies));
            retainedMeshes += paths.Length;
            committedMeshes += paths.Length;
            retainedBytes += bytes;
            totalBytes += bytes;
            return look;

            string Add(Entry entry)
            {
                string path = paths[index++] = MeshPath(++nextKey);
                meshes[path] = entry;
                pathReferences.Add(path, 0);
                return path;
            }
        }
    }

    public static string? CreateGroundWeapon(OgreMesh mesh, string original)
    {
        if (!installed || !HasTriangles(mesh)) return null;
        // The world renderer treats this as static (it passes no bone count). A synthetic name
        // skips generated/disk LODs for this in-memory mesh.
        mesh.SkeletonName ??= "__brutality_static_ground__";
        long bytes = EstimateBytes(mesh);
        lock (admission)
        {
            if (retainedMeshes >= MeshLimit || bytes > ByteLimit - retainedBytes
                || groundCommittedMeshes >= ProcessMeshLimit || bytes > ProcessByteLimit - groundCommittedBytes) return null;
            string path = MeshPath(++nextKey);
            meshes[path] = new(mesh, null, Surface.GroundWeapon, original);
            retainedMeshes++;
            groundCommittedMeshes++;
            retainedBytes += bytes;
            groundCommittedBytes += bytes;
            return path;
        }
    }

    public static string? GroundOriginal(string path) => IsSynthetic(path) && meshes.TryGetValue(path, out var entry)
        && entry.Surface == Surface.GroundWeapon ? entry.OriginalMesh : null;

    public static void RetireGroundWeapon(string path)
    {
        lock (admission)
            if (meshes.TryGetValue(path, out var entry) && entry.Surface == Surface.GroundWeapon && meshes.TryRemove(path, out _))
            {
                retainedMeshes--;
                retainedBytes -= EstimateBytes(entry.Mesh);
            }
    }

    public static void Retire(CharacterAppearance look)
    {
        var retired = new List<StockGeneratedMeshCache.Reclaimed>();
        lock (admission)
        {
            if (!variants.Remove(look, out var variant)) return;
            foreach (string path in variant.Paths)
            {
                if (--pathReferences[path] > 0) continue;
                pathReferences.Remove(path);
                if (!meshes.TryRemove(path, out var entry)) continue;
                long bytes = EstimateBytes(entry.Mesh);
                retired.Add(new(path, bytes));
                retainedMeshes--;
                retainedBytes -= bytes;
            }
        }
        StockGeneratedMeshCache.Retire(look, retired);
    }

    internal static void Recycled(StockGeneratedMeshCache.Reclaimed mesh)
    {
        lock (admission)
        {
            committedMeshes--;
            totalBytes -= mesh.Bytes;
        }
    }

    internal static bool HasGeneratedPath(CharacterAppearance look) => IsSynthetic(look.BodyMesh) || look.Parts.Exists(part => IsSynthetic(part.Mesh));

    internal static IEnumerable<string> GeneratedPaths(CharacterAppearance look)
    {
        if (IsSynthetic(look.BodyMesh)) yield return look.BodyMesh;
        foreach (var part in look.Parts)
            if (IsSynthetic(part.Mesh)) yield return part.Mesh;
    }

    internal static string? MorphPath(string key)
    {
        int at = key.LastIndexOf('@') + 1;
        if (!key.AsSpan(at).StartsWith(PathPrefix, StringComparison.OrdinalIgnoreCase)) return null;
        int end = key.IndexOf(".mesh", at, StringComparison.OrdinalIgnoreCase);
        return end < 0 ? null : key[at..(end + 5)];
    }

    static bool HasTriangles(OgreMesh mesh) => mesh.SubMeshes.Any(part => part.Indices.Indices.Length >= 3 &&
        (part.UseSharedVertices ? mesh.SharedVertexData : part.VertexData) is { VertexCount: >= 3 });

    static long EstimateBytes(OgreMesh mesh)
    {
        static long Buffers(OgreVertexData? data) => data is null ? 0 : data.Buffers.Values.Sum(buffer => buffer.Data.LongLength);
        long bytes = 1024 + Buffers(mesh.SharedVertexData) + mesh.BoneAssignments.Capacity * 12L;
        foreach (var submesh in mesh.SubMeshes)
            bytes += 256 + Buffers(submesh.VertexData) + submesh.Indices.Indices.LongLength * sizeof(uint) * 19 / 10
                + submesh.BoneAssignments.Capacity * 12L;
        foreach (var pose in mesh.Poses) bytes += 128 + pose.Vertices.Capacity * 28L;
        return bytes;
    }

    static string MeshPath(long id) => FormattableString.Invariant($"{PathPrefix}{id}.mesh");
    static bool IsSynthetic(string path) => path.StartsWith(PathPrefix, StringComparison.OrdinalIgnoreCase);
    internal static bool IsTissue(string path) => IsSynthetic(path) && meshes.TryGetValue(path, out var entry)
        && entry.Surface is Surface.Wound or Surface.Anatomy or Surface.Section or Surface.Bruise;

    static Surface TissueSurface(string label, bool allVertexColours)
    {
        if (!drawsVertexColours || anatomyMaterialConstructor is null || vertexColoured is null) return Surface.Wound;
        if (label.EndsWith("muscle and bone section", StringComparison.Ordinal)) return Surface.Section;
        if (label == "skin bruise") return Surface.Bruise;
        return allVertexColours || label.StartsWith("brain", StringComparison.OrdinalIgnoreCase) ||
            label.StartsWith("skull", StringComparison.OrdinalIgnoreCase) || label.StartsWith("fracture", StringComparison.OrdinalIgnoreCase)
            ? Surface.Anatomy : Surface.Wound;
    }

    // The fallback material resolver indexes the FCS record by the original mesh basename.
    static string OriginalMaterialPath(string path) => IsSynthetic(path) && meshes.TryGetValue(path, out var entry)
        ? entry.OriginalMesh ?? path : path;

    static object? AnatomyMaterial(string path)
    {
        if (!IsSynthetic(path) || !meshes.TryGetValue(path, out var entry)
            || entry.Surface is not (Surface.Anatomy or Surface.Section or Surface.Bruise)) return null;
        // Anatomy keeps the stock lighting and the mesh's species-specific colour.
        var material = anatomyMaterialConstructor!.Invoke(null);
        vertexColoured!.SetValue(material, true);
        doubleSided?.SetValue(material, true);
        description?.SetValue(material, entry.Surface == Surface.Section ? "Brutality section" : "Brutality anatomy");
        if (entry.Surface == Surface.Section) StockSectionDetail.Mark(material);
        if (entry.Surface == Surface.Bruise) StockBruiseGeometry.Mark(material);
        return material;
    }

    static bool FindMeshPrefix(string __0, ref string? __result)
    {
        if (!IsSynthetic(__0)) return true;
        __result = meshes.ContainsKey(__0) ? __0 : null;
        return false;
    }

    static bool BodySkeletonPrefix(string __0, ref string? __result)
    {
        if (!IsSynthetic(__0)) return true;
        __result = meshes.TryGetValue(__0, out var entry) ? entry.SkeletonName : null;
        return false;
    }

    static void WoundPoseSourcePrefix(ref CharacterAppearance __0)
    {
        if (woundPoseSources.TryGetValue(__0, out var source)) __0 = source;
    }

    static bool GeneratedLevelsPrefix(string __0, OgreMesh __1, Model __2, float __3, float __4,
        FoliageLodBuilder.Settings __5, ref List<MeshLodLevel> __result)
    {
        if (!IsSynthetic(__0)) return true;
        __result = [new(0, [.. __1.SubMeshes.Select(part => part.Indices.Indices)])];
        if (!__5.UvAware || !meshes.TryGetValue(__0, out var entry) || entry.Surface is not (Surface.Skin or Surface.Equipment or Surface.Anatomy or Surface.Section or Surface.Bruise))
            return false;
        var parts = __2.Parts.Where(part => part.Indices.Length > 0).ToArray();
        int triangles = parts.Sum(part => part.Indices.Length / 3);
        if (triangles is < 900 or > 16000 || !float.IsFinite(__3) || __3 <= 0) return false;
        var indices = new uint[]?[__1.SubMeshes.Count];
        float deviation = 0;
        int remaining = 0;
        foreach (var part in parts)
        {
            var reduced = StockFragmentLod.Reduce(part, Math.Clamp(__3 / 18, .15f, .85f), out float error);
            if (!ReferenceEquals(reduced, part.Indices)) deviation = MathF.Max(deviation, error);
            remaining += reduced.Length / 3;
            indices[part.SubMeshIndex] = reduced;
        }
        if (remaining < triangles * .88f)
        {
            float distance = MathF.Max(110, deviation * __4 / 2.5f);
            __result.Add(new(distance, indices));
#if BRUTALITY_STOCK_SAVE_PROBE
            if (Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_TRACE_HITS") == "1")
                Console.WriteLine(FormattableString.Invariant($"Brutality stock fragment LOD {triangles}->{remaining} triangles at {distance:F0} units"));
#endif
        }
        return false;
    }

    static void ModelBuilt(OgreMesh __0, Model __result)
    {
        if (generatedMeshes.TryGetValue(__0, out var entry)) generatedModels.Add(__result, entry);
    }

    static bool CapMeshPrefix(Model __0) => !generatedModels.TryGetValue(__0, out _);

    // Anatomy uses the stock item shader's vertex colour. Its blue channel is the original pigment.
    static bool MarkCurvaturePrefix(Model __0) => !generatedModels.TryGetValue(__0, out var entry)
        || entry.Surface is not (Surface.Anatomy or Surface.Section or Surface.Bruise or Surface.WeaponBlood);

    static void WeaponMaterialPostfix(CharacterPart part, object loaded, object __result)
    {
        if (!IsSynthetic(part.Mesh) || !meshes.TryGetValue(part.Mesh, out var entry)) return;
        if (entry.Surface == Surface.Bruise) StockBruiseGeometry.MarkLoadedMesh(loaded);
        if (entry.Surface == Surface.WeaponBlood)
        {
            vertexColoured?.SetValue(__result, true);
            StockWeaponWetness.Mark(__result);
        }
    }

    static IEnumerable<CodeInstruction> TissueMaterialTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        var body = instructions.ToList();
        var normal = generator.DefineLabel();
        var regularTissue = generator.DefineLabel();
        yield return new(OpCodes.Ldarg_3); // mesh path
        yield return new(OpCodes.Call, AccessTools.Method(typeof(StockHeadMeshes), nameof(AnatomyMaterial)));
        yield return new(OpCodes.Dup);
        yield return new(OpCodes.Brfalse, regularTissue);
        yield return new(OpCodes.Castclass, woundMaterial!.ReturnType);
        yield return new(OpCodes.Ret);
        var discardNull = new CodeInstruction(OpCodes.Pop);
        discardNull.labels.Add(regularTissue);
        yield return discardNull;
        yield return new(OpCodes.Ldarg_3); // mesh path
        yield return new(OpCodes.Call, AccessTools.Method(typeof(StockHeadMeshes), nameof(IsTissue)));
        yield return new(OpCodes.Brfalse, normal);
        yield return new(OpCodes.Ldarg_0); // CharacterContent instance
        yield return new(OpCodes.Ldarg_1); // CharacterAppearance
        yield return new(OpCodes.Call, woundMaterial!);
        yield return new(OpCodes.Ret);
        var meshPath = new CodeInstruction(OpCodes.Ldarg_3);
        meshPath.labels.Add(normal);
        yield return meshPath;
        yield return new(OpCodes.Call, AccessTools.Method(typeof(StockHeadMeshes), nameof(OriginalMaterialPath)));
        yield return new(OpCodes.Starg_S, (byte)3);
        foreach (var instruction in body) yield return instruction;
    }

    static bool ReadMeshPrefix(string __0, ref OgreMesh __result)
    {
        if (!IsSynthetic(__0)) return true;
        // A worker already holding an entry can finish safely. A queued load for a retired look has nothing left to prepare.
        if (meshes.TryGetValue(__0, out var entry))
        {
            __result = CopyForPreparation(entry.Mesh);
            generatedMeshes.Add(__result, entry);
        }
        else __result = new();
        return false;
    }

    static OgreMesh CopyForPreparation(OgreMesh source)
    {
        // Stock welding changes vertex descriptors, indices, bone assignments and poses. Its replacements leave raw buffers intact.
        static OgreVertexData? CopyVertices(OgreVertexData? data)
        {
            if (data is null) return null;
            var copy = new OgreVertexData { VertexCount = data.VertexCount };
            copy.Elements.AddRange(data.Elements);
            foreach (var (index, buffer) in data.Buffers) copy.Buffers[index] = buffer;
            return copy;
        }

        var result = new OgreMesh
        {
            Version = source.Version, SkeletonName = source.SkeletonName, SkeletallyAnimated = source.SkeletallyAnimated,
            SharedVertexData = CopyVertices(source.SharedVertexData), Bounds = source.Bounds,
        };
        result.BoneAssignments.AddRange(source.BoneAssignments);
        foreach (var submesh in source.SubMeshes)
        {
            var copy = new OgreSubMesh
            {
                MaterialName = submesh.MaterialName, UseSharedVertices = submesh.UseSharedVertices, Indices = submesh.Indices,
                VertexData = CopyVertices(submesh.VertexData), Operation = submesh.Operation,
            };
            copy.BoneAssignments.AddRange(submesh.BoneAssignments);
            foreach (var (alias, texture) in submesh.TextureAliases) copy.TextureAliases[alias] = texture;
            result.SubMeshes.Add(copy);
        }
        foreach (var (index, name) in source.SubMeshNames) result.SubMeshNames[index] = name;
        foreach (var pose in source.Poses)
        {
            var copy = new OgrePose { Name = pose.Name, Target = pose.Target, IncludesNormals = pose.IncludesNormals };
            copy.Vertices.AddRange(pose.Vertices);
            result.Poses.Add(copy);
        }
        return result;
    }
}
