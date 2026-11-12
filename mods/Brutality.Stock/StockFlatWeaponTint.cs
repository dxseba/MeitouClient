using Meitou.Game.Rendering.Gpu;
using Meitou.Game.Host;
using Meitou.Game.Rendering.Objects;
using Meitou.Game.Rendering.Terrain;
using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HarmonyLib;
using Meitou.Brutality.Rendering;
using Meitou.Data.Ogre;
using Meitou.Modding;

namespace Meitou.Brutality;

/// <summary>Reuses stock debris buffers and lighting with an albedo tint for landed arena weapons.</summary>
internal sealed class StockFlatWeaponTint(ModContext context, Type host, Type characters)
{
    const int MaximumVariants = 16;
    static StockFlatWeaponTint? installed;
    [ThreadStatic] static ShaderEdits? shaderEdits;
    readonly ConditionalWeakTable<DebrisRenderer, Display> displays = new();
    readonly FieldInfo gpu = RequireField(typeof(DebrisRenderer), "_gpu");
    readonly FieldInfo meshes = RequireField(typeof(DebrisRenderer), "_meshes");
    readonly FieldInfo content = RequireField(characters, "_content");
    readonly FieldInfo resolver = RequireField(RequireField(characters, "_content").FieldType, "_resolver");
    readonly FieldInfo art = AccessTools.GetDeclaredFields(host).Single(field => field.FieldType == typeof(AssetLocator));
    readonly FieldInfo options = AccessTools.GetDeclaredFields(host).Single(field => field.FieldType.Name == "WorldOptions");
    readonly FieldInfo blood = RequireField(AccessTools.GetDeclaredFields(host).Single(field => field.FieldType.Name == "WorldOptions").FieldType, "MeitouBlood");
    readonly MethodInfo activeCharacters = AccessTools.PropertyGetter(host, "ActiveCharacters") ?? throw new MissingMethodException("ActiveCharacters");
    readonly MethodInfo build = RequireMethod(typeof(DebrisRenderer), "Build", typeof(string), typeof(string), typeof(OgreMesh), typeof(LimbLook), typeof(string));
    readonly bool trace = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_TRACE_HITS") == "1";

    sealed class ShaderEdits { public bool Vertex, Fragment; }
    readonly record struct Key(string Record, string Material, int Pigment);
    sealed record Pending(Key Key, Task<StockWeaponGround.Paint?> Work);
    sealed class Display(AssetLocator assets)
    {
        public readonly AssetLocator Assets = assets;
        public readonly Lazy<WeaponBloodGeometry> Geometry = new(() => new(StockWeaponPresentation.LoadSplatter(assets)));
        public readonly Dictionary<Key, string?> Variants = [];
        public readonly List<DebrisPiece> Pieces = [];
        public List<DebrisPiece>? NativePieces;
        public DebrisRenderer? Renderer;
        public Pending? Pending;
        public bool Faulted;
    }

    internal static void Install(ModContext context)
    {
        try
        {
            var host = AccessTools.TypeByName("Meitou.Game.GameHost") ?? throw new TypeLoadException("GameHost");
            var characters = AccessTools.TypeByName("Meitou.Game.Rendering.Characters.CharacterRenderer") ?? throw new TypeLoadException("CharacterRenderer");
            var shaders = typeof(DebrisRenderer).Assembly.GetType("Meitou.Game.Rendering.Frame.NativeShaders", throwOnError: true)!;
            var hooks = new StockFlatWeaponTint(context, host, characters);
            context.Harmony.Patch(RequireMethod(shaders, "Port", typeof(string), typeof(IReadOnlyDictionary<string, string>), typeof(string)),
                prefix: Patch(nameof(Shader)));
            context.Harmony.Patch(RequireMethod(typeof(DebrisRenderer), nameof(DebrisRenderer.Draw), typeof(ReadOnlySpan<DebrisPiece>),
                typeof(Matrix4x4), typeof(Vector3), typeof(Vector4[]), typeof(WorldLighting).MakeByRefType(), typeof(Func<LimbLook, uint>)),
                postfix: Patch(nameof(Draw)));
            context.Harmony.Patch(RequireMethod(typeof(DebrisRenderer), nameof(DebrisRenderer.Dispose)), postfix: Patch(nameof(Disposed)));
            installed = hooks;
        }
        catch (Exception error)
        {
            context.LogError($"Brutality stock arena weapon tint unavailable: {error.GetBaseException().Message}");
        }
    }

    static FieldInfo RequireField(Type owner, string name) => AccessTools.Field(owner, name) ?? throw new MissingFieldException(owner.FullName, name);
    static MethodInfo RequireMethod(Type owner, string name, params Type[] signature) =>
        AccessTools.Method(owner, name, signature) ?? throw new MissingMethodException(owner.FullName, name);
    static HarmonyMethod Patch(string name) => new(typeof(StockFlatWeaponTint), name);

    static void Shader(ref string legacy)
    {
        if (shaderEdits is not { } edits) return;
        const string vertex = "vCurvature = aColour.z;", fragment = "if (cut > 0.0 && base.a < cut) discard;";
        if (legacy.Contains(vertex, StringComparison.Ordinal))
        {
            legacy = legacy.Replace("out float vCurvature;", "out float vCurvature;\nout vec3 vWeaponTint;")
                .Replace(vertex, vertex + "\n    vWeaponTint = aColour.rgb;");
            edits.Vertex = true;
        }
        else if (legacy.Contains(fragment, StringComparison.Ordinal))
        {
            legacy = legacy.Replace("in float vCurvature;", "in float vCurvature;\nin vec3 vWeaponTint;")
                .Replace(fragment, fragment + "\n    base.rgb *= clamp(vWeaponTint, 0.0, 1.0);");
            edits.Fragment = true;
        }
    }

    internal static void Queue(object host, WorldSession shown, DebrisRenderer native,
        Dictionary<int, DebrisPiece> landed, List<DebrisPiece> plain)
    {
        if (installed is not { } hooks || !hooks.Fill(host, shown, native, landed, plain)) plain.AddRange(landed.Values);
    }

    internal static void Clear(DebrisRenderer native)
    {
        if (installed?.displays.TryGetValue(native, out var display) == true) display.Pieces.Clear();
    }

    bool Fill(object host, WorldSession shown, DebrisRenderer native, Dictionary<int, DebrisPiece> landed, List<DebrisPiece> plain)
    {
        displays.TryGetValue(native, out var display);
        display?.Pieces.Clear();
        if (landed.Count == 0 || display?.Faulted == true || options.GetValue(host) is not { } settings
            || blood.GetValue(settings) is not true
            || StockWeaponGround.StainsFor(shown, shown.CurrentSnapshot) is not { Count: > 0 } stains
            || art.GetValue(host) is not AssetLocator assets) return false;
        display ??= displays.GetValue(native, _ => new(assets));
        display.NativePieces = plain;
        if (display.Pending is { Work.IsCompleted: true } pending) Complete(host, native, display, pending);
        if (display.Faulted) return false;
        foreach (var (id, piece) in landed)
        {
            if (stains.TryGetValue(id, out var stain))
            {
                var tint = StockWeaponPresentation.Quantize(stain.Blood, out int pigment);
                var key = new Key(stain.Item.Record, stain.Item.MaterialId, pigment);
                if (display.Variants.TryGetValue(key, out var mesh))
                {
                    if (mesh is not null) { display.Pieces.Add(piece with { Mesh = mesh }); continue; }
                }
                else if (display.Pending is null && display.Variants.Count < MaximumVariants
                    && context.Data.Find(key.Record) is { } record)
                {
                    var geometry = display.Geometry;
                    display.Pending = new(key, Task.Run(() => StockWeaponGround.Build(record,
                        stain with { Blood = tint, Revision = pigment }, assets, geometry.Value, preserveGroundMesh: true)));
                }
            }
            plain.Add(piece);
        }
        return true;
    }

    void Complete(object host, DebrisRenderer native, Display display, Pending pending)
    {
        display.Pending = null;
        string? key = null;
        try
        {
            if (pending.Work.GetAwaiter().GetResult() is { } paint)
            {
                display.Renderer ??= MakeRenderer(host, native, display.Assets);
                key = $"brutality-arena-weapon-{display.Variants.Count}";
                string? diffuse = context.Data.Find(pending.Key.Record) is { } record
                    ? StockWeaponGround.DiffuseOf(context.Data, display.Assets, record, pending.Key.Material) : null;
                object? built = build.Invoke(display.Renderer, [key, paint.Original, paint.Mesh, null, diffuse]);
                if (built is not null && meshes.GetValue(display.Renderer) is IDictionary cache) cache.Add(key, built);
                else key = null;
                if (trace && key is not null) Console.WriteLine($"Brutality stock arena weapon tint ready weapon={pending.Key.Record}");
            }
        }
        catch (Exception error)
        {
            key = null;
            if (display.Renderer is null) display.Faulted = true;
            context.LogError($"Brutality stock arena weapon tint skipped: {error.GetBaseException().Message}");
        }
        display.Variants.Add(pending.Key, key);
    }

    DebrisRenderer MakeRenderer(object host, DebrisRenderer native, AssetLocator assets)
    {
        var active = activeCharacters.Invoke(host, null);
        var resources = active is null ? null : content.GetValue(active);
        var materials = resources is null ? null : resolver.GetValue(resources) as MaterialResolver;
        var edits = new ShaderEdits();
        shaderEdits = edits;
        try
        {
            var renderer = new DebrisRenderer((IGpuContext)gpu.GetValue(native)!, materials, assets.Find) { SharedTextures = native.SharedTextures };
            if (edits.Vertex && edits.Fragment) return renderer;
            renderer.Dispose();
            throw new InvalidOperationException("The stock debris shader source changed.");
        }
        finally { shaderEdits = null; }
    }

    static void Draw(DebrisRenderer __instance, ReadOnlySpan<DebrisPiece> pieces, Matrix4x4 sliceViewProjection, Vector3 eye,
        Vector4[] frustum, in WorldLighting light)
    {
        if (installed is not { } hooks || !hooks.displays.TryGetValue(__instance, out var display)
            || display.Renderer is not { } renderer || display.Pieces.Count == 0 || display.NativePieces is not { } native) return;
        // The stock renderer also draws birds, stuck bolts and held limbs in separate passes.
        // Identify the main debris pass by its actual list, including when it is empty.
        var span = CollectionsMarshal.AsSpan(native);
        if (span.Length != pieces.Length || !span.IsEmpty
            && !Unsafe.AreSame(ref MemoryMarshal.GetReference(span), ref MemoryMarshal.GetReference(pieces))) return;
        renderer.Draw(CollectionsMarshal.AsSpan(display.Pieces), sliceViewProjection, eye, frustum, light);
    }

    static void Disposed(DebrisRenderer __instance)
    {
        if (installed is not { } hooks || !hooks.displays.TryGetValue(__instance, out var display)) return;
        hooks.displays.Remove(__instance);
        display.Renderer?.Dispose();
        if (display.Pending is { Work: var job }) _ = job.ContinueWith(task => _ = task.Exception, TaskScheduler.Default);
    }
}
