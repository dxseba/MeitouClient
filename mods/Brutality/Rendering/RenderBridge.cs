using System.Diagnostics;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
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
        public readonly Dictionary<(CharacterId, int), FragmentPoseBlend> Mix = [];
        public readonly Dictionary<long, RuntimeCharacterGeometry> WeaponVariants = [];
        public readonly Dictionary<CharacterId, (CharacterAppearance Look, CharacterGeometryResult Geometry, CharacterGeometryAdapter.BuildKey Key)> Cuts = [];
        public readonly CutGeometryQueue<CharacterId, CharacterGeometryAdapter.BuildKey> CutQueue = new();
        public readonly Dictionary<CharacterId, CharacterGeometryAdapter.BuildKey> Pending = [];
        public readonly Dictionary<CharacterId, CutPresentationFamily> Families = [];
        public readonly Dictionary<CharacterId, (CharacterGeometryAdapter.BuildKey Key, CharacterGeometryResult Geometry, HashSet<RuntimeCharacterGeometry> Wrappers)> Staged = [];
        public readonly HashSet<long> HeldKeys = [];
        public readonly ConcurrentDictionary<RuntimeCharacterGeometry, byte> Generated = new(ReferenceEqualityComparer.Instance);
    }

    internal static bool HasPublishedHead(World world, GoreCharacterFrame current) =>
        states.TryGetValue(world, out var render) && render.Cuts.TryGetValue(current.Character, out var shown)
            && HasPublishedHead(shown.Key, shown.Geometry, current);

    internal static bool HasPublishedHead(CharacterGeometryAdapter.BuildKey published, CharacterGeometryResult geometry, GoreCharacterFrame current) =>
        current.State.Head is not null && AcceptedGeometry(published, geometry, current)
            && geometry.Detached.Any(piece => piece.StableKey is 2 or 3);

    internal static bool TryAcceptedGeometry(World world, GoreCharacterFrame current, [NotNullWhen(true)] out CharacterGeometryResult? geometry)
    {
        geometry = null;
        if (!states.TryGetValue(world, out var render) || !render.Cuts.TryGetValue(current.Character, out var shown)
            || !AcceptedGeometry(shown.Key, shown.Geometry, current)) return false;
        geometry = shown.Geometry;
        return true;
    }

    internal static bool AcceptedGeometry(CharacterGeometryAdapter.BuildKey published, CharacterGeometryResult geometry, GoreCharacterFrame current) =>
        !geometry.Retained.IsRetired && geometry.Detached.All(piece => !piece.Geometry.IsRetired)
            && published == CharacterGeometryAdapter.RequestKey(current.Appearance, published.State.Rig, published.State.Body, current.State, ComponentsOf(current));

    internal static bool IsCurrentCutPending(World world, CharacterId owner, CharacterAppearance look, BrutalityActorView state) =>
        states.TryGetValue(world, out var render) && (render.Pending.TryGetValue(owner, out var pending)
            || render.CutQueue.TryPendingKey(owner, out pending)) && MatchesPendingCut(pending, look, state);

    internal static bool IsPresentationHeld(World world, long key) => states.TryGetValue(world, out var render) && render.HeldKeys.Contains(key);

    internal static bool MatchesPendingCut(CharacterGeometryAdapter.BuildKey pending, CharacterAppearance look, BrutalityActorView state) =>
        pending == CharacterGeometryAdapter.RequestKey(look, pending.State.Rig, pending.State.Body, state, pending.State.Components);

    static bool Shown(RenderState render, RuntimeCharacterGeometry geometry) => render.WeaponVariants.Values.Any(value => ReferenceEquals(value, geometry))
        || render.Families.Values.Any(family => family.Pieces.Any(piece => ReferenceEquals(piece.Instance.Geometry, geometry)));

    static void DiscardStage(RenderState render, CharacterId owner, CharacterGeometryResult? keep = null)
    {
        if (!render.Staged.Remove(owner, out var stage)) return;
        if (!ReferenceEquals(keep, stage.Geometry) && !render.Cuts.Values.Any(value => ReferenceEquals(value.Geometry, stage.Geometry)))
            CharacterGeometryAdapter.Retire(stage.Geometry);
        foreach (var wrapper in stage.Wrappers)
            if (!Shown(render, wrapper)) wrapper.Retire();
    }

    static void Stage(RenderState render, CharacterId owner, CharacterGeometryAdapter.BuildKey key, CharacterGeometryResult geometry,
        HashSet<RuntimeCharacterGeometry> wrappers)
    {
        if (render.Staged.TryGetValue(owner, out var previous))
        {
            if (!ReferenceEquals(previous.Geometry, geometry)) DiscardStage(render, owner, geometry);
            else CutPresentationFamily.RetireUnused(previous.Wrappers, wrappers, old => Shown(render, old));
        }
        render.Staged[owner] = (key, geometry, wrappers);
    }

    internal static void Track(World world, RuntimeCharacterGeometry geometry) => states.GetValue(world, static _ => new()).Generated.TryAdd(geometry, 0);
    static void ReleaseWorld(World __instance)
    {
        if (states.TryGetValue(__instance, out var state))
        {
            state.CutQueue.Dispose();
            if (resources is not null)
                foreach (var look in state.Cuts.Values.Select(value => value.Look).Distinct()) resources.Geometry.Forget(look);
            foreach (var geometry in state.Generated.Keys) geometry.Retire();
            states.Remove(__instance);
        }
        GeometryPreparations.ReleaseWorld(__instance);
        BodyWoundRenderBridge.ReleaseWorld(__instance);
    }

    public static void Install(ModContext context, RenderResources source)
    {
        resources = source;
        context.Harmony.Patch(AccessTools.Method(typeof(World), nameof(World.Dispose)), postfix: new HarmonyMethod(typeof(RenderBridge), nameof(ReleaseWorld)));
        var host = AccessTools.TypeByName("Meitou.Game.GameHost");
        if (host is null) return; // Headless tools have no character draw list.
        sessionField = AccessTools.Field(host, "session") ?? throw new MissingFieldException(host.FullName, "session");
        var draw = AccessTools.Method(host, "DrawKey") ?? throw new MissingMethodException(host.FullName, "DrawKey");
        var target = Expression.Parameter(typeof(object)); var id = Expression.Parameter(typeof(CharacterId));
        drawKey = Expression.Lambda<Func<object, CharacterId, long>>(Expression.Call(Expression.Convert(target, host), draw, id), target, id).Compile();
        foreach (string name in new[] { "FillDrawListCore", "FillWorldDrawList" })
            context.Harmony.Patch(AccessTools.Method(host, name), postfix: new HarmonyMethod(AccessTools.Method(typeof(RenderBridge), nameof(FillPostfix))));
    }
    static void FillPostfix(object __instance, CharacterDrawList list)
    {
        if (resources is null || drawKey is null || sessionField?.GetValue(__instance) is not WorldSession session) return;
        var sourceResources = resources;
        var render = states.GetValue(session.World, static _ => new());
        render.HeldKeys.Clear();
        GeometryPreparations.Capture(__instance, list, session, resources);
        var physics = session.World.Systems.OfType<FragmentPhysicsSystem>().FirstOrDefault();
        var proxyFrame = BodyFragmentProxySystem.ForWorld(session.World)?.FrameAt(session.ShownTick);
        var frame = physics?.FrameAt(session.ShownTick);
        if (frame is null || frame.Characters.Count == 0)
        {
            render.CutQueue.Retain(new HashSet<CharacterId>()); render.Pending.Clear();
            foreach (var owner in render.Staged.Keys.ToArray()) DiscardStage(render, owner);
            if (proxyFrame is not null)
            {
                var hidden = proxyFrame.Proxies.Keys.Select(proxy => drawKey(__instance, proxy)).ToHashSet();
                list.Items.RemoveAll(instance => hidden.Contains(instance.Key));
            }
            FinishPresentation(__instance, session, list, render);
            return;
        }
        var previous = physics!.FrameAt(session.PreviousSnapshot.Tick);
        var proxyKeys = new Dictionary<long, BodyFragmentProxyView>();
        if (proxyFrame is not null)
            foreach (var value in proxyFrame.Proxies.Values) proxyKeys[drawKey(__instance, value.Proxy)] = value;
        var byKey = new Dictionary<long, GoreCharacterFrame>();
        foreach (var value in frame.Characters.Values) byKey[drawKey(__instance, value.Character)] = value;
        foreach (var (key, proxy) in proxyKeys) render.Keys[(proxy.Owner, proxy.StableKey)] = key;
        var neededOwners = frame.Characters.Keys.ToHashSet();
        render.CutQueue.Retain(neededOwners);
        foreach (var owner in render.Staged.Keys.Where(owner => !neededOwners.Contains(owner)).ToArray()) DiscardStage(render, owner);
        foreach (var owner in render.Pending.Keys.Where(owner => !neededOwners.Contains(owner)).ToArray()) render.Pending.Remove(owner);
        foreach (var owner in render.Cuts.Keys.Where(owner => !neededOwners.Contains(owner)).ToArray())
        { CharacterGeometryAdapter.Retire(render.Cuts[owner].Geometry); render.Cuts.Remove(owner); }
        var original = list.Items.ToArray();
        var originalByKey = original.ToDictionary(instance => instance.Key);
        var removed = new HashSet<long>(); var replacedHeads = new HashSet<long>();
        var processed = new HashSet<CharacterId>(); var output = new List<CharacterInstance>();
        var stains = session.World.Systems.OfType<WeaponBloodState>().FirstOrDefault()?.FrameAt(session.ShownTick);
        double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        foreach (var source in original)
        {
            var current = proxyKeys.TryGetValue(source.Key, out var proxy) ? frame.Characters.GetValueOrDefault(proxy.Owner) : byKey.GetValueOrDefault(source.Key);
            if (current is null || !processed.Add(current.Character)) continue;
            long rootKey = drawKey(__instance, current.Character);
            bool hasRoot = originalByKey.TryGetValue(rootKey, out var rootSource);
            if (!hasRoot) rootSource = source with { Key = rootKey, Hidden = true };
            if (hasRoot) removed.Add(rootKey);
            foreach (var pair in proxyKeys) if (pair.Value.Owner == current.Character) removed.Add(pair.Key);
            if (!render.Families.TryGetValue(current.Character, out var family)) render.Families[current.Character] = family = new();
            if (sourceResources.Rigs.For(current.Appearance) is not { } setup || sourceResources.Mesh(current.Appearance.BodyMesh) is not { } body)
            { Hold(); continue; }
            var physicalComponents = ComponentsOf(current);
            var request = CharacterGeometryAdapter.RequestKey(current.Appearance, setup.Rig, body, current.State, physicalComponents);
            if (render.Staged.TryGetValue(current.Character, out var staged) && staged.Key != request) DiscardStage(render, current.Character);
            if (!sourceResources.Geometry.TryCached(current.Appearance, setup.Rig, body, current.State, out var geometry, physicalComponents))
            {
                if (render.CutQueue.TryTake(current.Character, request, out var completed)) geometry = sourceResources.Geometry.Publish(request, completed);
                else
                {
                    if (render.CutQueue.NeedsRequest(current.Character, request))
                    {
                        var input = CharacterGeometryAdapter.Capture(request, current.State, sourceResources.Mesh);
                        var adapter = sourceResources.Geometry;
                        render.CutQueue.Request(current.Character, request, cancellation => adapter.BuildUnpublished(input, cancellation));
                    }
                    if (render.CutQueue.TryPendingKey(current.Character, out _)) render.Pending[current.Character] = request;
                    else render.Pending.Remove(current.Character);
                    Hold(); continue;
                }
            }
            else render.CutQueue.Remove(current.Character);
            Track(session.World, geometry.Retained);
            foreach (var part in geometry.Detached) Track(session.World, part.Geometry);
            var effective = new Dictionary<int, RuntimeCharacterGeometry> { [0] = Paint(geometry.Retained) };
            foreach (var fragment in geometry.Detached) effective[fragment.StableKey] = Paint(fragment.Geometry);
            Stage(render, current.Character, request, geometry,
                effective.Values.Where(sourceResources.WeaponBlood.IsOwned).ToHashSet<RuntimeCharacterGeometry>(ReferenceEqualityComparer.Instance));
            bool ready = true;
            foreach (var shape in effective.Values)
            {
                Track(session.World, shape);
                list.Preparations.Add(rootSource with { Appearance = current.Appearance, Geometry = shape, Bones = null, RuntimeShaderData = null });
                ready &= list.IsPrepared(current.Appearance, shape);
            }
            if (!ready)
            { render.Pending[current.Character] = request; Hold(); continue; }
            render.Pending.Remove(current.Character);
            render.Staged.Remove(current.Character); // This complete family now owns its displayed base/wrapper variants.
            if (render.Cuts.TryGetValue(current.Character, out var replaced) && !ReferenceEquals(replaced.Geometry, geometry))
                CharacterGeometryAdapter.Retire(replaced.Geometry);
            render.Cuts[current.Character] = (current.Appearance, geometry, request);
            family.Accept(now);
            if (current.State.Head is not null) replacedHeads.Add(rootKey);
            GoreCharacterFrame? before = previous?.Characters.GetValueOrDefault(current.Character);
            var retained = current.Retained is null ? rootSource.Bones : Mixed(render, (current.Character, 0), before?.Retained, current.Retained, session.SimulationAlpha);
            var retainedInstance = WithPhysicalPlacement(rootSource, before, current, session.SimulationAlpha);
            var retainedWorld = retainedInstance.World ?? (rootSource.Rotation is { } retainedRotation
                ? CharacterRigs.Placement(setup, rootSource.Position, retainedRotation, rootSource.Scale)
                : CharacterRigs.Placement(setup, rootSource.Position, rootSource.Yaw, rootSource.Scale));
            retained = family.Blend(0, retained, retainedWorld, now);
            output.Add(retainedInstance with
            {
                Appearance = current.Appearance, Geometry = effective[0], Bones = retained,
                WorldBounds = FragmentWorldBounds.For(geometry.Retained, current.Appearance, setup.Rig, body, retained, retainedWorld,
                    sourceResources.Mesh, setup.Shape, sourceResources.Rigs, rootSource.Build),
            });
            foreach (var fragment in geometry.Detached)
            {
                var pose = current.Detached.FirstOrDefault(piece => piece.StableKey == fragment.StableKey);
                if (pose is null) continue;
                var old = before?.Detached.FirstOrDefault(piece => piece.StableKey == fragment.StableKey);
                var key = (current.Character, fragment.StableKey);
                if (!render.Keys.TryGetValue(key, out long shown)) render.Keys[key] = shown = Interlocked.Decrement(ref serial);
                var pieceSource = originalByKey.GetValueOrDefault(shown, rootSource);
                var framed = WithPhysicalPlacement(pieceSource, before, current, session.SimulationAlpha, detached: true);
                var world = framed.World!.Value;
                var position = old is null ? pose.Position : Vector3.Lerp(old.Position, pose.Position, session.SimulationAlpha);
                var mixed = Mixed(render, key, old?.Bones, pose.Bones, session.SimulationAlpha);
                var shownPose = family.Blend(fragment.StableKey, mixed, world, now);
                output.Add(framed with
                {
                    Key = shown, Appearance = current.Appearance, Geometry = effective[fragment.StableKey], Position = position, World = world,
                    Bones = shownPose, FreezeFrom = null, BlendFrom = null, Blend = 0,
                    WorldBounds = FragmentWorldBounds.For(fragment.Geometry, current.Appearance, setup.Rig, body, shownPose, world,
                        sourceResources.Mesh, setup.Shape, sourceResources.Rigs, pieceSource.Build),
                });
            }

            RuntimeCharacterGeometry Paint(RuntimeCharacterGeometry shape) => stains?.Characters.GetValueOrDefault(current.Character) is { } blood
                ? sourceResources.WeaponBlood.Apply(current.Appearance, shape, blood.Parts, sourceResources.Mesh) ?? shape : shape;

            void Hold()
            {
                if (!family.HasPresentation && current.BirthPresentation is { } birth)
                {
                    var born = rootSource with { Appearance = birth.Appearance, Geometry = null, Bones = birth.Bones,
                        Position = birth.Placement.Translation, World = birth.Placement, Rotation = null, FreezeFrom = null,
                        BlendFrom = null, Blend = 0, RuntimeShaderData = null, WorldBounds = CutPresentationFamily.Bounds(birth.Bones, birth.Placement) };
                    family.Capture(new[] { new PresentedPiece(0, born) });
                }
                if (!family.Hold(current.Appearance, item => sourceResources.Rigs.For(item.Appearance) is { } heldSetup
                    ? CutPresentationFamily.WorldBones(item, heldSetup, sourceResources.Rigs) : new BonePose(0))) return;
                foreach (var row in family.Pieces)
                {
                    var shown = row.Instance;
                    if (row.StableKey == 0 && !hasRoot) shown = shown with { Hidden = true };
                    output.Add(shown); render.HeldKeys.Add(shown.Key);
                }
                if (current.State.Head is not null) replacedHeads.Add(rootKey);
            }
        }
        render.CutQueue.StartNext();
        list.Items.RemoveAll(instance => removed.Contains(instance.Key) || instance.FreezeFrom is { } from && replacedHeads.Contains(from));
        list.Items.AddRange(output);
        FinishPresentation(__instance, session, list, render);
    }

    static void FinishPresentation(object host, WorldSession session, CharacterDrawList list, RenderState render)
    {
        BeforeEffects?.Invoke(host, session, list); TintWeapons(host, list, session); AfterFill?.Invoke(host, list, session);
        if (drawKey is null) return;
        var keys = new Dictionary<long, (CharacterId Owner, int Piece)>();
        foreach (var actor in session.CurrentSnapshot.Characters) keys[drawKey(host, actor.Id)] = (actor.Id, 0);
        foreach (var pair in render.Keys) keys[pair.Value] = pair.Key;
        var groups = new Dictionary<CharacterId, List<PresentedPiece>>();
        foreach (var instance in list.Items)
        {
            if (!keys.TryGetValue(instance.Key, out var key)) continue;
            if (!groups.TryGetValue(key.Owner, out var group)) groups[key.Owner] = group = [];
            group.Add(new(key.Piece, instance));
        }
        foreach (var pair in groups)
        {
            if (pair.Value.Any(piece => render.HeldKeys.Contains(piece.Instance.Key)) || !CutPresentationFamily.Ready(list, pair.Value)) continue;
            if (!render.Families.TryGetValue(pair.Key, out var family)) render.Families[pair.Key] = family = new();
            family.Capture(pair.Value);
        }
        foreach (var owner in render.Families.Keys.Where(owner => !groups.ContainsKey(owner)).ToArray()) render.Families.Remove(owner);
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
            if (render.HeldKeys.Contains(list.Items[i].Key))
            {
                if (list.Items[i].Geometry is { } held && resources.WeaponBlood.IsOwned(held)) used[list.Items[i].Key] = held;
            }
            else if (byKey.TryGetValue(list.Items[i].Key, out var stains))
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

    /// <summary>Published physical bones are relative to this frame, even if the native hot root moved or returned to animation.</summary>
    internal static CharacterInstance WithPhysicalPlacement(in CharacterInstance instance, GoreCharacterFrame? before,
        GoreCharacterFrame current, float alpha, bool detached = false) => current.Retained is not null || detached
            ? instance with { World = Placement(before, current, alpha) } : instance;

    static Matrix4x4 Placement(GoreCharacterFrame? before, GoreCharacterFrame current, float alpha)
    {
        if (before is null || !Matrix4x4.Decompose(before.Placement, out var size0, out var turn0, out var at0)
            || !Matrix4x4.Decompose(current.Placement, out var size1, out var turn1, out var at1)) return current.Placement;
        return Matrix4x4.CreateScale(Vector3.Lerp(size0, size1, alpha))
            * Matrix4x4.CreateFromQuaternion(Quaternion.Slerp(turn0, turn1, alpha))
            * Matrix4x4.CreateTranslation(Vector3.Lerp(at0, at1, alpha));
    }

    static CharacterGeometryComponents ComponentsOf(GoreCharacterFrame frame)
    {
        int[]? torso = null, headPositive = null, headNegative = null, leftArm = null, rightArm = null, leftLeg = null, rightLeg = null;
        foreach (var piece in frame.Detached)
            switch (piece.StableKey)
            {
                case 1: torso = piece.Bones.Components; break;
                case 2: headPositive = piece.Bones.Components; break;
                case 3: headNegative = piece.Bones.Components; break;
                case 10: leftArm = piece.Bones.Components; break;
                case 11: rightArm = piece.Bones.Components; break;
                case 12: leftLeg = piece.Bones.Components; break;
                case 13: rightLeg = piece.Bones.Components; break;
            }
        return new(frame.Retained?.Components, torso, headPositive, headNegative, leftArm, rightArm, leftLeg, rightLeg);
    }

    static BonePose Mixed(RenderState state, (CharacterId, int) key, BonePose? before, BonePose after, float alpha)
    {
        if (!state.Mix.TryGetValue(key, out var blend)) state.Mix[key] = blend = new();
        return blend.Between(before, after, alpha);
    }
}
