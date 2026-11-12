using Meitou.Game.Host;
using Meitou.Game.Rendering.Frame;
using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using Meitou.Brutality.Rendering;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;
using Meitou.Data.Textures;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.Brutality;

/// <summary>Builds a stock character look with only the actually stained weapon mesh replaced.</summary>
internal sealed class StockWeaponPresentation
{
    // The native renderer retains its uploaded assets. Leave most of the shared mesh budget for severed bodies.
    const int MaximumVariants = 24;
    static StockWeaponPresentation? installed;
    readonly ConditionalWeakTable<object, Display> displays = new();
    readonly ConditionalWeakTable<WeaponStainCharacter, Request> requests = new();
    readonly FieldInfo session, contentAssets, graphicsOptions, blood, loadedAssets, assetParts;
    readonly MethodInfo renderer, rigs, drawKey;
    readonly bool trace = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_TRACE_HITS") == "1";

    readonly record struct Key(CharacterAppearance Original, string Pigments);
    sealed record Request(Key Key, IReadOnlyList<WeaponStainAttachment> Stains);
    sealed record Variant(CharacterAppearance Look, long StagingKey)
    {
        public bool Ready;
    }
    sealed record Prior(Request Request, Variant Variant);
    sealed record Pending(Request Request, Task<IReadOnlyList<(CharacterPart Part, OgreMesh Mesh)>> Work, CancellationTokenSource Cancel);
    sealed class Display(World world, object renderer, AssetLocator assets)
    {
        public readonly World World = world;
        public readonly object Renderer = renderer;
        public readonly Dictionary<Key, Variant?> Variants = [];
        public readonly Dictionary<long, Prior> Shown = [];
        public readonly Dictionary<long, WeaponStainCharacter> ByKey = [];
        public readonly Lazy<WeaponBloodGeometry> Geometry = new(() => new(LoadSplatter(assets), StockWeaponWetness.Enabled));
        public WeaponBloodFrame? Frame;
        public Pending? Pending;
        public long Serial;
    }

    StockWeaponPresentation(Type host, Type characters)
    {
        session = Field(host, "Session");
        contentAssets = AccessTools.GetDeclaredFields(host).Single(field => field.FieldType == typeof(AssetLocator));
        graphicsOptions = AccessTools.GetDeclaredFields(host).Single(f => f.FieldType.Name == "WorldOptions");
        blood = Field(graphicsOptions.FieldType, "MeitouBlood");
        renderer = AccessTools.PropertyGetter(host, "ActiveCharacters") ?? throw new MissingMethodException("GameHost.ActiveCharacters");
        rigs = AccessTools.PropertyGetter(characters, "Rigs") ?? throw new MissingMethodException("CharacterRenderer.Rigs");
        drawKey = Method(host, "DrawKey", typeof(CharacterId));
        loadedAssets = Field(characters, "_assets");
        assetParts = Field(loadedAssets.FieldType.GetGenericArguments()[1], "Parts");
    }

    internal static bool Install(ModContext context)
    {
        if (!StockHeadMeshes.Install(context)) return false;
        var host = AccessTools.TypeByName("Meitou.Game.GameHost") ?? throw new TypeLoadException("GameHost");
        var characters = AccessTools.TypeByName("Meitou.Game.Rendering.Characters.CharacterRenderer") ?? throw new TypeLoadException("CharacterRenderer");
        var hooks = new StockWeaponPresentation(host, characters);
        context.Harmony.Patch(Method(host, "FillDrawListCore", typeof(CharacterDrawList)),
            postfix: new HarmonyMethod(typeof(StockWeaponPresentation), nameof(DrawListed)));
        context.Harmony.Patch(Method(host, "Run"),
            finalizer: new HarmonyMethod(typeof(StockWeaponPresentation), nameof(HostFinished)));
        installed = hooks;
        return true;
    }

    static FieldInfo Field(Type type, string name) =>
        AccessTools.Field(type, name) ?? throw new MissingFieldException(type.Name, name);
    static MethodInfo Method(Type type, string name, params Type[] parameters) =>
        AccessTools.Method(type, name, parameters) ?? throw new MissingMethodException(type.Name, name);
    static void DrawListed(object __instance, CharacterDrawList list) => installed?.Draw(__instance, list);
    static void HostFinished(object __instance) => installed?.Reset(__instance);

    void Draw(object host, CharacterDrawList list)
    {
        if (session.GetValue(host) is not WorldSession shown) return;
        displays.TryGetValue(host, out var display);
        if (display is not null && !ReferenceEquals(display.World, shown.World))
        {
            Reset(host);
            display = null;
        }
        if (graphicsOptions.GetValue(host) is not { } options || blood.GetValue(options) is not true
            || WeaponBloodState.For(shown.World)?.FrameAt(shown.ShownTick) is not { Characters.Count: > 0 } frame
            || renderer.Invoke(host, null) is not { } chars || contentAssets.GetValue(host) is not AssetLocator assets)
            return;

        if (display is not null && !ReferenceEquals(display.Renderer, chars))
        {
            Reset(host);
            display = null;
        }
        display ??= displays.GetValue(host, _ => new(shown.World, chars, assets));
        FinishPaint(display);
        if (!ReferenceEquals(display.Frame, frame))
        {
            display.Frame = frame;
            display.ByKey.Clear();
            foreach (var (owner, stain) in frame.Characters)
                if (drawKey.Invoke(host, [owner]) is long key) display.ByKey.Add(key, stain);
            foreach (long key in display.Shown.Keys.Where(key => !display.ByKey.ContainsKey(key)).ToArray())
                display.Shown.Remove(key);
        }
        if (loadedAssets.GetValue(chars) is not IDictionary loaded) return;

        int count = list.Items.Count;
        for (int i = 0; i < count; i++)
        {
            var item = list.Items[i];
            if (item.Hidden || !display.ByKey.TryGetValue(item.Key, out var stain)
                || !ReferenceEquals(item.Appearance, stain.Appearance)) continue;
            var request = requests.GetValue(stain, BuildRequest);
            if (request.Stains.Count == 0) continue;
            if (display.Variants.TryGetValue(request.Key, out var variant))
            {
                if (variant is not null && IsReady(display, request.Key, variant, loaded))
                {
                    display.Shown[item.Key] = new(request, variant);
                    list.Items[i] = item with { Appearance = variant.Look };
                    continue;
                }
                if (variant is not null && display.Variants[request.Key] is not null)
                    list.Add(item with { Key = variant.StagingKey, Appearance = variant.Look, Hidden = true });
            }
            else if (display.Pending is null && display.Variants.Count < MaximumVariants
                && StockHeadMeshes.CanCreate && rigs.Invoke(chars, null) is CharacterRigs stockRigs)
            {
                var cancel = new CancellationTokenSource();
                var geometry = display.Geometry;
                var token = cancel.Token;
                display.Pending = new(request, Task.Run(() => Paint(request, stockRigs, geometry.Value, token), token), cancel);
            }
            if (display.Shown.TryGetValue(item.Key, out var prior) && SameItems(prior.Request, request))
                list.Items[i] = item with { Appearance = prior.Variant.Look };
        }
    }

    bool IsReady(Display display, Key key, Variant variant, IDictionary loaded)
    {
        if (variant.Ready) return true;
        if (!loaded.Contains(variant.Look)) return false;
        if (loaded[variant.Look] is { } asset && assetParts.GetValue(asset) is ICollection parts
            && parts.Count == variant.Look.Parts.Count + 1)
        {
            variant.Ready = true;
            if (trace) Console.WriteLine($"Brutality stock weapon stain mesh ready body={key.Original.BodyMesh}");
            return true;
        }
        StockHeadMeshes.Retire(variant.Look);
        display.Variants[key] = null;
        return false;
    }

    void FinishPaint(Display display)
    {
        if (display.Pending is not { Work.IsCompleted: true } pending) return;
        display.Pending = null;
        pending.Cancel.Dispose();
        CharacterAppearance? variant = pending.Work.IsCompletedSuccessfully && pending.Work.Result is { Count: > 0 } parts
            ? StockHeadMeshes.CreateWeapon(pending.Request.Key.Original, parts) : null;
        if (trace && pending.Work.Exception is { } fault)
            Console.WriteLine($"Brutality stock weapon stain paint unavailable: {fault.GetBaseException().Message}");
        else _ = pending.Work.Exception;
        display.Variants.Add(pending.Request.Key, variant is null ? null : new(variant, long.MinValue | (1L << 51) | ++display.Serial));
    }

    void Reset(object host)
    {
        if (!displays.TryGetValue(host, out var display)) return;
        displays.Remove(host);
        foreach (var variant in display.Variants.Values)
            if (variant is not null) StockHeadMeshes.Retire(variant.Look);
        if (display.Pending is not { } pending) return;
        pending.Cancel.Cancel();
        _ = pending.Work.ContinueWith(task =>
        {
            _ = task.Exception;
            pending.Cancel.Dispose();
        }, TaskScheduler.Default);
    }

    static Request BuildRequest(WeaponStainCharacter stain)
    {
        var parts = stain.Appearance.Parts;
        var selected = new List<WeaponStainAttachment>();
        var pigment = new StringBuilder();
        for (int i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            if (part.Field == "sheath" || part.Point == "back2"
                || part.Field != "bare sword" && part.Record.GetPath("bare sword").Length > 0) continue;
            foreach (var source in stain.Parts)
            {
                if (!ReferenceEquals(source.SourcePart, part)) continue;
                var value = Quantize(source.Blood, out int bucket);
                pigment.Append(i).Append('/').Append(bucket).Append(';');
                selected.Add(source with { Blood = value, Revision = bucket });
                break;
            }
        }
        return new(new(stain.Appearance, pigment.ToString()), selected);
    }

    internal static Vector4 Quantize(Vector4 colour, out int bucket)
    {
        int red = (int)MathF.Round(colour.X * 15), green = (int)MathF.Round(colour.Y * 15), blue = (int)MathF.Round(colour.Z * 15);
        int amount = Math.Clamp((int)MathF.Ceiling(colour.W * 6), 1, 6);
        bucket = (red << 12) | (green << 8) | (blue << 4) | amount;
        return new(red / 15f, green / 15f, blue / 15f, amount / 6f);
    }

    static bool SameItems(Request old, Request current)
    {
        if (!ReferenceEquals(old.Key.Original, current.Key.Original) || old.Stains.Count != current.Stains.Count) return false;
        for (int i = 0; i < old.Stains.Count; i++)
            if (!ReferenceEquals(old.Stains[i].Item, current.Stains[i].Item)
                || !ReferenceEquals(old.Stains[i].SourcePart, current.Stains[i].SourcePart)) return false;
        return true;
    }

    static IReadOnlyList<(CharacterPart Part, OgreMesh Mesh)> Paint(Request request, CharacterRigs rigs, WeaponBloodGeometry geometry, CancellationToken cancellation)
    {
        var result = new List<(CharacterPart Part, OgreMesh Mesh)>(request.Stains.Count);
        foreach (var stain in request.Stains)
        {
            cancellation.ThrowIfCancellationRequested();
            if (rigs.FindMesh(stain.SourcePart.Mesh) is not { } path) continue;
            try
            {
                var source = OgreMeshReader.ReadFile(path);
                var painted = geometry.Paint(source, stain);
                if (!ReferenceEquals(painted, source)) result.Add((stain.SourcePart, painted));
            }
            catch (Exception error) when (error is IOException or InvalidDataException or OgreFormatException or UnauthorizedAccessException) { }
        }
        return result;
    }

    internal static RgbaImage? LoadSplatter(AssetLocator assets)
    {
        if (assets.Find("bloody.png") is not { } path) return null;
        try { return TextureLoader.LoadFile(path, allMips: false).Levels[0]; }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException) { return null; }
    }
}
