using Meitou.Game.Host;
using Meitou.Game.Rendering.Objects;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Brutality.Rendering;
using Meitou.Data;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;
using Meitou.Data.Ogre;
using Meitou.Data.World;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Items;

namespace Meitou.Brutality;

/// <summary>Swaps a dropped weapon's presentation mesh once its stain has streamed through the stock world renderer.</summary>
internal sealed class StockWeaponGround
{
    const int MaximumPaints = 16;
    static StockWeaponGround? installed;
    static readonly ConditionalWeakTable<World, GroundPublication> publications = new();
    readonly ConditionalWeakTable<object, Display> displays = new();
    readonly GameDatabase data;
    readonly FieldInfo session, groundPoses, graphics, blood, assets, cache, status, placements;
    readonly MethodInfo objects, getMesh, requestMesh, showGround, hasRuntime;
    readonly object resident, failed;
    readonly bool trace = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_TRACE_HITS") == "1";

    sealed record Snapshot(long Tick, long Revision, IReadOnlyDictionary<int, WeaponStainView> Stains,
        IReadOnlyDictionary<int, string> Materials);
    sealed class GroundPublication
    {
        public volatile Snapshot? Latest;
    }
    readonly record struct PaintKey(string Record, int Pigment);
    internal sealed record Paint(string Original, OgreMesh Mesh);
    sealed record Pending(PaintKey Key, Task<Paint?> Work);
    sealed record Entry(string Path, object Mesh);
    sealed class Display(World world, WorldObjectRenderer renderer, AssetLocator assets)
    {
        public readonly World World = world;
        public readonly WorldObjectRenderer Renderer = renderer;
        public readonly Lazy<WeaponBloodGeometry> Geometry = new(() => new(StockWeaponPresentation.LoadSplatter(assets)));
        public readonly Dictionary<PaintKey, Entry?> Variants = [];
        public readonly HashSet<int> Shown = [];
        public Pending? Pending;
    }

    StockWeaponGround(Type host, ModContext context)
    {
        data = context.Data;
        session = Field(host, "Session");
        groundPoses = Field(StockHostParts.GroundItems.FieldType, "_groundPoses");
        graphics = AccessTools.GetDeclaredFields(host).Single(f => f.FieldType.Name == "WorldOptions");
        blood = Field(graphics.FieldType, "MeitouBlood");
        assets = AccessTools.GetDeclaredFields(host).Single(f => f.FieldType == typeof(AssetLocator));
        objects = AccessTools.PropertyGetter(host, "ActiveObjects") ?? throw new MissingMethodException("GameHost.ActiveObjects");
        showGround = Method(StockHostParts.GroundItems.FieldType, "ShowGround", typeof(GroundItemSnapshot), typeof(Quaternion), typeof(float));
        cache = Field(typeof(WorldObjectRenderer), "_meshes");
        placements = Field(typeof(WorldObjectRenderer), "_objects");
        hasRuntime = Method(placements.FieldType, "HasRuntime", typeof(string));
        getMesh = Method(cache.FieldType, "Get", typeof(string), typeof(bool));
        requestMesh = Method(cache.FieldType, "Request", getMesh.ReturnType, typeof(float));
        status = Field(getMesh.ReturnType, "Status");
        resident = Enum.Parse(status.FieldType, "Resident");
        failed = Enum.Parse(status.FieldType, "Failed");
    }

    internal static void Install(ModContext context)
    {
        try
        {
            var host = AccessTools.TypeByName("Meitou.Game.GameHost") ?? throw new TypeLoadException("GameHost");
            var hooks = new StockWeaponGround(host, context);
            context.Harmony.Patch(Method(typeof(AssetLocator), nameof(AssetLocator.Find), typeof(string), typeof(string).MakeByRefType()),
                prefix: Patch(nameof(FindMesh)));
            context.Harmony.Patch(Method(typeof(MaterialResolver), nameof(MaterialResolver.Candidates), typeof(string), typeof(OgreSubMesh)),
                prefix: Patch(nameof(MaterialPath)), postfix: Patch(nameof(TintMaterial)));
            context.Harmony.Patch(Method(StockHostParts.GroundItems.FieldType, "UpdateGroundItems", typeof(WorldSnapshot)), postfix: Patch(nameof(GroundUpdated)));
            context.Harmony.Patch(Method(host, "Run"), finalizer: Patch(nameof(HostFinished)));
            installed = hooks;
            context.Log("Brutality stock ground weapon stains initialized");
        }
        catch (Exception error)
        {
            context.LogError($"Brutality stock ground weapon stains unavailable: {error.GetBaseException().Message}");
        }
    }

    static HarmonyMethod Patch(string method) => new(typeof(StockWeaponGround), method);
    static FieldInfo Field(Type type, string name) => AccessTools.Field(type, name) ?? throw new MissingFieldException(type.Name, name);
    static MethodInfo Method(Type type, string name, params Type[] args) => AccessTools.Method(type, name, args) ?? throw new MissingMethodException(type.Name, name);

    internal static void Publish(World world, WeaponBloodState state)
    {
        if (installed is null) return;
        var publication = publications.GetOrCreateValue(world);
        long revision = world.GroundItems.Revision;
        if (publication.Latest?.Revision == revision) return;
        var stains = new Dictionary<int, WeaponStainView>();
        var materials = new Dictionary<int, string>();
        foreach (var item in world.GroundItems.All)
        {
            if (item.Item.Type is not (FcsRecordType.Weapon or FcsRecordType.Crossbow)) continue;
            if (item.Item.MaterialId.Length > 0) materials.Add(item.Id, item.Item.MaterialId);
            if (item.Item.Type == FcsRecordType.Weapon && state.TryGet(item.Item, out var stain)) stains.Add(item.Id, stain);
        }
        publication.Latest = new(world.Tick + 1, revision, stains, materials);
    }

    internal static string MaterialId(World world, int item) =>
        publications.TryGetValue(world, out var source) ? source.Latest?.Materials.GetValueOrDefault(item) ?? "" : "";

    internal static string? DiffuseOf(GameDatabase data, AssetLocator art, GameRecord item, string materialId)
    {
        var texture = ItemIcons.MaterialOf(data, item, materialId)?.GetPath("texture map");
        if (!string.IsNullOrEmpty(texture) && art.Find(texture) is not null) return texture;
        if (materialId.Length == 0) return null;
        texture = ItemIcons.MaterialOf(data, item, "")?.GetPath("texture map");
        return !string.IsNullOrEmpty(texture) && art.Find(texture) is not null ? texture : null;
    }

    internal static IReadOnlyDictionary<int, WeaponStainView>? StainsFor(WorldSession shown, WorldSnapshot snapshot) =>
        publications.TryGetValue(shown.World, out var source) && source.Latest is { } state
            && state.Tick <= shown.ShownTick && state.Revision == snapshot.GroundItemsRevision ? state.Stains : null;

    static bool FindMesh(string name, ref string how, ref string? __result)
    {
        if (StockHeadMeshes.GroundOriginal(name) is null) return true;
        __result = name;
        how = "Brutality painted ground mesh";
        return false;
    }

    static void MaterialPath(ref string meshFile, out bool __state)
    {
        string? original = StockHeadMeshes.GroundOriginal(meshFile);
        __state = original is not null;
        if (original is not null) meshFile = original;
    }

    static void TintMaterial(bool __state, List<SurfaceMaterial> __result)
    {
        if (!__state) return;
        if (__result.Count == 0) __result.Add(new SurfaceMaterial { Description = "Brutality ground weapon", VertexColours = true });
        else
            for (int i = 0; i < __result.Count; i++)
                __result[i] = __result[i] with { VertexColours = true };
    }

    static void GroundUpdated(object __instance, WorldSnapshot snapshot)
    {
        if (installed is { } hooks && StockHostParts.HostOf(__instance) is { } host) hooks.Update(host, __instance, snapshot);
    }
    static void HostFinished(object __instance) => installed?.Reset(__instance);

    void Update(object host, object ground, WorldSnapshot snapshot)
    {
        if (session.GetValue(host) is not WorldSession shown) return;
        displays.TryGetValue(host, out var display);
        if (display is not null && !ReferenceEquals(display.World, shown.World))
        {
            Reset(host);
            display = null;
        }
        if (!publications.TryGetValue(shown.World, out var source) || source.Latest is not { } state
            || state.Tick > shown.ShownTick || state.Revision != snapshot.GroundItemsRevision
            || display is null && state.Stains.Count == 0) return;
        bool enabled = graphics.GetValue(host) is { } options && blood.GetValue(options) is true;
        if (display is null && (!enabled || state.Stains.Count == 0)) return;
        if (objects.Invoke(host, null) is not WorldObjectRenderer renderer || assets.GetValue(host) is not AssetLocator art
            || groundPoses.GetValue(ground) is not Dictionary<int, (Vector3 At, Quaternion Rotation, string Key)> poses) return;
        if (display is not null && !ReferenceEquals(display.Renderer, renderer))
        {
            Reset(host);
            display = null;
        }
        display ??= displays.GetValue(host, _ => new(shown.World, renderer, art));
        if (cache.GetValue(renderer) is not { } meshCache || placements.GetValue(renderer) is not { } placed) return;
        // The host removes the placement by id before this postfix when the item is picked up.
        // The cached mesh is reusable on a later drop, whose id is newly allocated by the simulation.
        display.Shown.RemoveWhere(id => !poses.ContainsKey(id));
        if (display.Pending is { Work.IsCompleted: true } pending)
        {
            var job = pending.Work;
            Entry? variant = null;
            if (job.IsCompletedSuccessfully && job.Result is { } paint
                && StockHeadMeshes.CreateGroundWeapon(paint.Mesh, paint.Original) is { } path
                && getMesh.Invoke(meshCache, [path, false]) is { } mesh)
                variant = new(path, mesh);
            else if (trace && job.Exception is { } fault)
                Console.WriteLine($"Brutality stock ground weapon paint unavailable: {fault.GetBaseException().Message}");
            else _ = job.Exception;
            display.Variants.Add(pending.Key, variant);
            display.Pending = null;
        }
        if (state.Stains.Count == 0 && display.Shown.Count == 0) return;
        foreach (var item in snapshot.GroundItems)
        {
            if (!poses.TryGetValue(item.Id, out var pose)) continue;
            if (display.Shown.Contains(item.Id))
            {
                if (enabled) continue;
                renderer.RemoveRuntime(pose.Key);
                showGround.Invoke(ground, [item, pose.Rotation, pose.At.Y]);
                display.Shown.Remove(item.Id);
                continue;
            }
            if (!enabled || !state.Stains.TryGetValue(item.Id, out var stain)) continue;
            var tint = StockWeaponPresentation.Quantize(stain.Blood, out int pigment);
            var key = new PaintKey(item.Record, pigment);
            if (display.Variants.TryGetValue(key, out var entry))
            {
                if (entry is null) continue;
                object? current = status.GetValue(entry.Mesh);
                if (Equals(current, resident))
                {
                    // For cut-triggered drops, the native debris renderer owns the flight.
                    // Switch only after the host has installed the weapon's ground placement.
                    if (item.Fall is not null && hasRuntime.Invoke(placed, [pose.Key]) is not true) continue;
                    renderer.RemoveRuntime(pose.Key);
                    renderer.AddRuntime(new BuildingPlacement(WorldLayout.ZoneOf(item.Position.X, item.Position.Z),
                        pose.Key, item.Record, item.Position, pose.Rotation, null, pose.At.Y) { Key = pose.Key, ItemMesh = entry.Path });
                    display.Shown.Add(item.Id);
                    if (trace) Console.WriteLine($"Brutality stock ground weapon stain mesh ready id={item.Id} weapon={item.Record}");
                }
                else if (!Equals(current, failed)) requestMesh.Invoke(meshCache, [entry.Mesh, 0f]);
                continue;
            }
            if (display.Pending is not null || display.Variants.Count >= MaximumPaints || !StockHeadMeshes.CanCreate
                || data.Find(item.Record) is not { } record) continue;
            var resources = display;
            display.Pending = new(key, Task.Run(() => Build(record, stain with { Blood = tint, Revision = pigment }, art, resources.Geometry.Value)));
        }
    }

    internal static Paint? Build(GameRecord record, WeaponStainView stain, AssetLocator art, WeaponBloodGeometry geometry, bool preserveGroundMesh = false)
    {
        string original = !preserveGroundMesh && record.GetPath("ground mesh").Length == 0
            && record.GetPath("bare sword") is { Length: > 0 } blade ? blade : WorldObjectLayout.ItemMesh(record);
        if (art.Find(original) is not { } path) return null;
        var source = OgreMeshReader.ReadFile(path);
        var part = new CharacterPart { Record = record, Mesh = original, Field = "ground mesh", Mode = AttachMode.Bone };
        var mesh = geometry.Paint(source, new(stain.Item, part, stain.Blood, stain.Revision));
        return ReferenceEquals(mesh, source) ? null : new(path, mesh);
    }

    void Reset(object host)
    {
        if (!displays.TryGetValue(host, out var display)) return;
        displays.Remove(host);
        foreach (var entry in display.Variants.Values)
            if (entry is not null) StockHeadMeshes.RetireGroundWeapon(entry.Path);
        if (display.Pending is { Work: var job })
            _ = job.ContinueWith(task => _ = task.Exception, TaskScheduler.Default);
    }
}
