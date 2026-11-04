using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Brutality.Anatomy;
using Meitou.Brutality.Physics;
using Meitou.Content;
using Meitou.Data;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;
using Meitou.Data.Textures;
using Meitou.Engine;
using Meitou.Modding;
using Meitou.Rendering;
using Meitou.Simulation;
using Meitou.Simulation.Ragdolls;

namespace Meitou.Brutality.Rendering;

internal sealed class RenderResources
{
    readonly AssetLocator assets;
    readonly Dictionary<string, OgreMesh?> meshes = new(StringComparer.OrdinalIgnoreCase);
    public AssetLocator Assets => assets;
    public CharacterRigs Rigs { get; }
    public CharacterGeometryAdapter Geometry { get; }
    public WeaponBloodGeometry WeaponBlood { get; }
    public RenderResources(GameInstall install, GameDatabase data, AnatomyLibrary anatomy)
    {
        assets = new(install);
        Rigs = new(data, name => assets.Find(name), name => assets.Find(name));
        Geometry = new(anatomy);
        RgbaImage? splatter = null;
        if (assets.Find("bloody.png") is { } path)
        {
            try { splatter = TextureLoader.LoadFile(path, allMips: false).Levels[0]; }
            catch (Exception error) when (error is IOException or InvalidDataException) { Console.Error.WriteLine("Brutality splatter: " + error.Message); }
        }
        WeaponBlood = new(splatter);
    }
    public OgreMesh? Mesh(string name)
    {
        if (assets.Find(name) is not { } path) return null;
        if (meshes.TryGetValue(path, out var known)) return known;
        try { return meshes[path] = OgreMeshReader.ReadFile(path); }
        catch (Exception error) when (error is IOException or InvalidDataException or OgreFormatException)
        { Console.Error.WriteLine($"Brutality mesh {name}: {error.Message}"); return meshes[path] = null; }
    }
}

/// <summary>Version-specific host adapter; geometry and physics stay in the mod rather than replacement engine assemblies.</summary>
internal static class RenderBridge
{
    static RenderResources? resources;
    internal static Action<object, CharacterDrawList, WorldSession>? AfterFill;
    internal static Action<object, WorldSession, CharacterDrawList>? BeforeEffects;
    internal static long? FragmentDrawKey(World world, CharacterId owner, int stableKey) =>
        states.TryGetValue(world, out var state) && state.Keys.TryGetValue((owner, stableKey), out long key) ? key : null;
    internal static long? NativeDrawKey(object host, CharacterId owner) => drawKey?.Invoke(host, owner);
    static FieldInfo? sessionField;
    static Func<object, CharacterId, long>? drawKey;
    static readonly ConditionalWeakTable<World, RenderState> states = new();
    static long serial = -0x4000000000000000L;
    sealed class RenderState
    {
        public readonly Dictionary<(CharacterId, int), long> Keys = [];
        public readonly Dictionary<(CharacterId, int), BonePose> Mix = [];
        public readonly Dictionary<long, RuntimeCharacterGeometry> WeaponVariants = [];
        public readonly Dictionary<CharacterId, (CharacterAppearance Look, CharacterGeometryResult Geometry)> Cuts = [];
        public readonly ConcurrentDictionary<RuntimeCharacterGeometry, byte> Generated = new(ReferenceEqualityComparer.Instance);
    }

    internal static void Track(World world, RuntimeCharacterGeometry geometry) => states.GetValue(world, static _ => new()).Generated.TryAdd(geometry, 0);
    static void ReleaseWorld(World __instance)
    {
        if (states.TryGetValue(__instance, out var state))
        {
            if (resources is not null)
                foreach (var look in state.Cuts.Values.Select(value => value.Look).Distinct()) resources.Geometry.Forget(look);
            foreach (var geometry in state.Generated.Keys) geometry.Retire();
            states.Remove(__instance);
        }
        BodyWoundRenderBridge.ReleaseWorld(__instance);
    }

    public static void Install(ModContext context, RenderResources source)
    {
        resources = source;
        context.Harmony.Patch(AccessTools.Method(typeof(World), nameof(World.Dispose)), postfix: new HarmonyMethod(typeof(RenderBridge), nameof(ReleaseWorld)));
        context.Harmony.Patch(AccessTools.Method(typeof(RagdollSystem), "Wanted"), prefix: new HarmonyMethod(AccessTools.Method(typeof(RenderBridge), nameof(WantedPrefix))));
        var host = AccessTools.TypeByName("Meitou.Game.GameHost");
        if (host is null) return; // Headless tools have no character draw list.
        sessionField = AccessTools.Field(host, "session") ?? throw new MissingFieldException(host.FullName, "session");
        var draw = AccessTools.Method(host, "DrawKey") ?? throw new MissingMethodException(host.FullName, "DrawKey");
        var target = Expression.Parameter(typeof(object)); var id = Expression.Parameter(typeof(CharacterId));
        drawKey = Expression.Lambda<Func<object, CharacterId, long>>(Expression.Call(Expression.Convert(target, host), draw, id), target, id).Compile();
        foreach (string name in new[] { "FillDrawListCore", "FillWorldDrawList" })
            context.Harmony.Patch(AccessTools.Method(host, name), postfix: new HarmonyMethod(AccessTools.Method(typeof(RenderBridge), nameof(FillPostfix))));
    }
    static bool WantedPrefix(CharacterCold cold, ref RagdollPart __result)
    {
        if (!BrutalityRuntime.ControlsPhysics(cold)) return true;
        __result = RagdollPart.None; return false;
    }

    static void FillPostfix(object __instance, CharacterDrawList list)
    {
        if (resources is null || drawKey is null || sessionField?.GetValue(__instance) is not WorldSession session) return;
        var physics = session.World.Systems.OfType<FragmentPhysicsSystem>().FirstOrDefault();
        var proxyFrame = BodyFragmentProxySystem.ForWorld(session.World)?.FrameAt(session.ShownTick);
        if (physics?.FrameAt(session.ShownTick) is not { } frame || frame.Characters.Count == 0)
        {
            if (proxyFrame is not null)
            {
                var hidden = proxyFrame.Proxies.Keys.Select(proxy => drawKey(__instance, proxy)).ToHashSet();
                list.Items.RemoveAll(instance => hidden.Contains(instance.Key));
            }
            BeforeEffects?.Invoke(__instance, session, list); TintWeapons(__instance, list, session); AfterFill?.Invoke(__instance, list, session); return;
        }
        var previous = physics.FrameAt(session.PreviousSnapshot.Tick);
        var proxyKeys = new Dictionary<long, BodyFragmentProxyView>();
        if (proxyFrame is not null)
            foreach (var value in proxyFrame.Proxies.Values) proxyKeys[drawKey(__instance, value.Proxy)] = value;
        var byKey = new Dictionary<long, GoreCharacterFrame>();
        foreach (var value in frame.Characters.Values) byKey[drawKey(__instance, value.Character)] = value;
        // Native falling-head visuals are replaced by the corresponding articulated fragment, not drawn twice.
        list.Items.RemoveAll(item => item.FreezeFrom is { } from && byKey.TryGetValue(from, out var source) && source.State.Head is not null);
        var render = states.GetValue(session.World, static _ => new());
        foreach (var (key, proxy) in proxyKeys) render.Keys[(proxy.Owner, proxy.StableKey)] = key;
        var neededOwners = frame.Characters.Keys.ToHashSet();
        foreach (var owner in render.Cuts.Keys.Where(owner => !neededOwners.Contains(owner)).ToArray())
        {
            var old = render.Cuts[owner].Geometry; old.Retained.Retire();
            foreach (var piece in old.Detached) piece.Geometry.Retire();
            render.Cuts.Remove(owner);
        }
        long started = Stopwatch.GetTimestamp(); int rebuilt = 0;
        var pendingProxies = new HashSet<long>();
        int count = list.Items.Count;
        for (int i = 0; i < count; i++)
        {
            var instance = list.Items[i];
            int stableKey = 0;
            GoreCharacterFrame? current;
            if (proxyKeys.TryGetValue(instance.Key, out var proxy))
            { current = frame.Characters.GetValueOrDefault(proxy.Owner); stableKey = proxy.StableKey; }
            else current = byKey.GetValueOrDefault(instance.Key);
            if (current is null || resources.Rigs.For(current.Appearance) is not { } setup
                || resources.Mesh(current.Appearance.BodyMesh) is not { } body)
            { if (stableKey != 0) pendingProxies.Add(instance.Key); continue; }
            if (!resources.Geometry.TryCached(current.Appearance, setup.Rig, body, current.State, out var geometry))
            {
                if (rebuilt >= 4 || rebuilt > 0 && Stopwatch.GetElapsedTime(started).TotalMilliseconds >= 5)
                {
                    if (!render.Cuts.TryGetValue(current.Character, out var prior) || !ReferenceEquals(prior.Look, current.Appearance))
                    { if (stableKey != 0) pendingProxies.Add(instance.Key); continue; }
                    geometry = prior.Geometry;
                }
                else
                {
                    geometry = resources.Geometry.Build(current.Appearance, setup.Rig, body, resources.Mesh, current.State);
                    rebuilt++;
                }
            }
            if (render.Cuts.TryGetValue(current.Character, out var replaced) && !ReferenceEquals(replaced.Geometry, geometry))
            {
                replaced.Geometry.Retained.Retire(); foreach (var piece in replaced.Geometry.Detached) piece.Geometry.Retire();
            }
            render.Cuts[current.Character] = (current.Appearance, geometry);
            var retainedGeometry = geometry.Retained;
            Track(session.World, retainedGeometry);
            foreach (var part in geometry.Detached) Track(session.World, part.Geometry);
            GoreCharacterFrame? before = previous?.Characters.GetValueOrDefault(current.Character);
            if (stableKey != 0)
            {
                var fragment = geometry.Detached.FirstOrDefault(piece => piece.StableKey == stableKey);
                var pose = current.Detached.FirstOrDefault(piece => piece.StableKey == stableKey);
                if (fragment is null || pose is null) { pendingProxies.Add(instance.Key); continue; }
                var old = before?.Detached.FirstOrDefault(piece => piece.StableKey == stableKey);
                list.Items[i] = instance with
                {
                    Appearance = current.Appearance, Geometry = fragment.Geometry,
                    Bones = Mixed(render, (current.Character, stableKey), old?.Bones, pose.Bones, session.SimulationAlpha),
                    Position = old is null ? pose.Position : Vector3.Lerp(old.Position, pose.Position, session.SimulationAlpha),
                    World = Placement(before, current, session.SimulationAlpha), FreezeFrom = null, BlendFrom = null, Blend = 0,
                };
                continue;
            }
            var retained = current.Retained is null ? instance.Bones : Mixed(render, (current.Character, 0), before?.Retained, current.Retained, session.SimulationAlpha);
            list.Items[i] = instance with { Appearance = current.Appearance, Geometry = retainedGeometry, Bones = retained };
            foreach (var fragment in geometry.Detached)
            {
                if (fragment.StableKey == 1 && proxyKeys.Values.Any(proxy => proxy.Owner == current.Character && proxy.StableKey == 1)) continue;
                var pose = current.Detached.FirstOrDefault(p => p.StableKey == fragment.StableKey);
                if (pose is null) continue;
                var old = before?.Detached.FirstOrDefault(p => p.StableKey == fragment.StableKey);
                var key = (current.Character, fragment.StableKey);
                if (!render.Keys.TryGetValue(key, out long shown)) render.Keys.Add(key, shown = Interlocked.Decrement(ref serial));
                var world = instance.World ?? (instance.Rotation is { } rotation
                    ? CharacterRigs.Placement(setup, instance.Position, rotation, instance.Scale)
                    : CharacterRigs.Placement(setup, instance.Position, instance.Yaw, instance.Scale));
                var position = old is null ? pose.Position : Vector3.Lerp(old.Position, pose.Position, session.SimulationAlpha);
                list.Add(instance with
                {
                    Key = shown, Appearance = current.Appearance, Geometry = fragment.Geometry, Position = position, World = world,
                    Bones = Mixed(render, key, old?.Bones, pose.Bones, session.SimulationAlpha), FreezeFrom = null,
                    BlendFrom = null, Blend = 0,
                });
            }
        }
        list.Items.RemoveAll(instance => pendingProxies.Contains(instance.Key));
        BeforeEffects?.Invoke(__instance, session, list);
        TintWeapons(__instance, list, session);
        AfterFill?.Invoke(__instance, list, session);
    }

    static void TintWeapons(object host, CharacterDrawList list, WorldSession session)
    {
        if (resources is null || drawKey is null) return;
        var render = states.GetValue(session.World, static _ => new());
        var byKey = new Dictionary<long, IReadOnlyList<WeaponStainAttachment>>();
        var blood = session.World.Systems.OfType<WeaponBloodState>().FirstOrDefault()?.FrameAt(session.ShownTick);
        if (blood is not null)
            foreach (var (owner, state) in blood.Characters)
            {
                byKey[drawKey(host, owner)] = state.Parts;
                foreach (var (key, value) in render.Keys) if (key.Item1 == owner) byKey[value] = state.Parts;
            }
        var used = new Dictionary<long, RuntimeCharacterGeometry>();
        for (int i = 0; i < list.Items.Count; i++)
            if (byKey.TryGetValue(list.Items[i].Key, out var stains))
            {
                var item = list.Items[i];
                var painted = resources.WeaponBlood.Apply(item.Appearance, item.Geometry, stains, resources.Mesh);
                if (painted is not null && resources.WeaponBlood.IsOwned(painted))
                { used[item.Key] = painted; Track(session.World, painted); }
                list.Items[i] = item with { Geometry = painted };
            }
        var live = used.Values.ToHashSet(ReferenceEqualityComparer.Instance);
        foreach (var old in render.WeaponVariants.Values) if (!live.Contains(old)) old.Retire();
        render.WeaponVariants.Clear();
        foreach (var (key, value) in used) render.WeaponVariants[key] = value;
        // Retired variants are already owned by the host until its last in-flight draw retires.
        foreach (var geometry in render.Generated.Keys) if (geometry.IsRetired) render.Generated.TryRemove(geometry, out _);
    }

    static Matrix4x4 Placement(GoreCharacterFrame? before, GoreCharacterFrame current, float alpha)
    {
        if (before is null || !Matrix4x4.Decompose(before.Placement, out var size0, out var turn0, out var at0)
            || !Matrix4x4.Decompose(current.Placement, out var size1, out var turn1, out var at1)) return current.Placement;
        return Matrix4x4.CreateScale(Vector3.Lerp(size0, size1, alpha))
            * Matrix4x4.CreateFromQuaternion(Quaternion.Slerp(turn0, turn1, alpha))
            * Matrix4x4.CreateTranslation(Vector3.Lerp(at0, at1, alpha));
    }

    static BonePose Mixed(RenderState state, (CharacterId, int) key, BonePose? before, BonePose after, float alpha)
    {
        if (before is null || ReferenceEquals(before, after) || before.Set.Length != after.Set.Length) return after;
        if (!state.Mix.TryGetValue(key, out var result) || result.Set.Length != after.Set.Length)
            state.Mix[key] = result = new(after.Set.Length);
        result.Weight = before.Weight + (after.Weight - before.Weight) * alpha; result.AbsolutePositions = after.AbsolutePositions;
        for (int h = 0; h < result.Set.Length; h++)
        {
            result.Set[h] = after.Set[h];
            result.Position[h] = before.Set[h] && after.Set[h] ? Vector3.Lerp(before.Position[h], after.Position[h], alpha) : after.Position[h];
            result.Rotation[h] = before.Set[h] && after.Set[h] ? Quaternion.Slerp(before.Rotation[h], after.Rotation[h], alpha) : after.Rotation[h];
        }
        return result;
    }
}
