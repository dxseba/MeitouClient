using Meitou.Game.Host;
using Meitou.Game.Rendering.Frame;
using System.Collections;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Brutality.Effects;
using Meitou.Brutality.Physics;
using Meitou.Brutality.Ui;
using Meitou.Data.Characters;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Items;
using Meitou.Simulation.Ragdolls;

namespace Meitou.Brutality;

/// <summary>Installs a local stump and articulated detached limb once both generated appearances are ready.</summary>
internal sealed class StockLimbFragments
{
    const int MaxOwners = 10, MaxDetached = 16;
    static StockLimbFragments? installed;
    readonly ModContext context;
    readonly ConditionalWeakTable<object, Display> displays = new();
    readonly FieldInfo session, states, assets, stamp, bones, previous, posed, seen, frozen, parts;
    readonly FieldInfo nativeFalls, fallingItem, hiddenGround, hiddenKeys, groundPoses;
    readonly MethodInfo renderer, rigs, drawKey, lastDrawn, groundKey;
    readonly Type stateType;
    readonly bool trace = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_TRACE_HITS") == "1";

    sealed class Display(World world, int epoch, object renderer)
    {
        public readonly World World = world;
        public readonly int Epoch = epoch;
        public readonly object Renderer = renderer;
        public readonly IRagdollEnvironment Ground = new WalkabilityEnvironment(world.Walkability);
        public readonly AdvancedFragmentScene Physics = new();
        public readonly Dictionary<CharacterId, Retained> Owners = [];
        public readonly Dictionary<CharacterId, (CharacterAppearance Appearance, StockLimbRules.Request[] Cuts)> Attempted = [];
        public readonly Dictionary<CharacterId, HashSet<(int Slot, long Tick)>> Spawned = [];
        public readonly List<Detached> Detached = [];
        public readonly HashSet<int> ShownItems = [];
        public readonly Dictionary<int, StockNativeLimbLabels.LimbLabel> NativeLabels = [];
        public readonly List<BrutalityPartLabel> LooseLabels = [];
        public Pending? Pending;
        public double? PreviousTime;
        public long Serial;
    }

    sealed record Retained(long Key, CharacterAppearance Source, CharacterAppearance Look, StockLimbRules.Request[] Cuts);
    sealed record Staged(long Key, CharacterAppearance Look, StockLimbBuild.Detached? Piece);
    sealed class Pending(CharacterId owner, long key, CharacterAppearance source, StockLimbRules.Request[] cuts,
        Matrix4x4 world, Matrix4x4[] birth, Task<StockLimbBuild.Prepared?> work, CancellationTokenSource cancellation, bool detachedOnly)
    {
        public readonly CharacterId Owner = owner;
        public readonly long Key = key;
        public readonly CharacterAppearance Source = source;
        public readonly StockLimbRules.Request[] Cuts = cuts;
        public readonly Matrix4x4 World = world;
        public readonly Matrix4x4[] Birth = birth;
        public readonly Task<StockLimbBuild.Prepared?> Work = work;
        public readonly CancellationTokenSource Cancellation = cancellation;
        public readonly bool DetachedOnly = detachedOnly;
        public readonly Stopwatch Timer = Stopwatch.StartNew();
        public readonly List<Staged> Looks = [];
        public bool Abandoned;
    }

    sealed record Detached(long Key, int Item, CharacterAppearance Look, SkeletonRig Rig, Matrix4x4 World, Matrix4x4 Inverse,
        Matrix4x4[] Birth, Matrix4x4[] Palette, Matrix4x4[] Motion, AdvancedFragmentScene.Piece Piece, ulong Blood, double Expires,
        (Vector3 At, Quaternion Rotation, string Key)? NativePose, StockLimbRules.Request Cut)
    {
        public double NextSave;
        public bool SavedFrozen;
    }

    sealed record DebrisFilter(IList Falls, List<(int Index, object Entry)> Removed, HashSet<int> Hidden, List<int> Added);

    StockLimbFragments(ModContext context, Type host, Type chars)
    {
        this.context = context;
        session = Field(host, "Session");
        renderer = AccessTools.PropertyGetter(host, "ActiveCharacters") ?? throw new MissingMemberException("ActiveCharacters");
        rigs = AccessTools.PropertyGetter(chars, "Rigs") ?? throw new MissingMemberException("CharacterRenderer.Rigs");
        drawKey = Method(host, "DrawKey", typeof(CharacterId));
        lastDrawn = Method(chars, "LastDrawn", typeof(long), typeof(string));
        groundKey = Method(StockHostParts.GroundItems.FieldType, "GroundId", typeof(int));
        states = Field(chars, "_states"); assets = Field(chars, "_assets"); stamp = Field(chars, "_updateStamp");
        stateType = states.FieldType.GetGenericArguments()[1];
        bones = Field(stateType, "Bones"); previous = Field(stateType, "Previous");
        posed = Field(stateType, "Posed"); seen = Field(stateType, "Seen"); frozen = Field(stateType, "Frozen");
        parts = Field(assets.FieldType.GetGenericArguments()[1], "Parts");
        nativeFalls = Field(StockHostParts.Debris.FieldType, "Falls");
        fallingItem = Field(nativeFalls.FieldType.GetGenericArguments()[0], "Ground");
        var ground = StockHostParts.GroundItems.FieldType;
        hiddenGround = Field(ground, "HiddenGround"); hiddenKeys = Field(ground, "HiddenGroundKeys");
        groundPoses = Field(ground, "_groundPoses");
    }

    internal static bool Install(ModContext context)
    {
        if (!StockHeadMeshes.Install(context)) return false;
        try
        {
            var host = AccessTools.TypeByName("Meitou.Game.GameHost") ?? throw new TypeLoadException("GameHost");
            var chars = AccessTools.TypeByName("Meitou.Game.Rendering.Characters.CharacterRenderer") ?? throw new TypeLoadException("CharacterRenderer");
            var hook = new StockLimbFragments(context, host, chars);
            context.Harmony.Patch(Method(host, "FillDrawListCore", typeof(CharacterDrawList)), postfix: Patch(nameof(Frame)));
            context.Harmony.Patch(Method(StockHostParts.Debris.FieldType, "UpdateDebris", typeof(World), typeof(float)), prefix: Patch(nameof(FilterDebris)),
                finalizer: Patch(nameof(RestoreDebris)));
            context.Harmony.Patch(Method(StockHostParts.GroundItems.FieldType, "UpdateGroundVisibility"), postfix: Patch(nameof(KeepNativeHidden)));
            context.Harmony.Patch(Method(host, "Run"), finalizer: Patch(nameof(Finished)));
            installed = hook;
            return true;
        }
        catch (Exception error)
        {
            context.Log($"Brutality stock limb fragments unavailable: {error.GetBaseException().Message}");
            return false;
        }
    }

    static HarmonyMethod Patch(string name) => new(typeof(StockLimbFragments), name);
    static FieldInfo Field(Type type, string name) =>
        AccessTools.Field(type, name) ?? throw new MissingFieldException(type.Name, name);
    static MethodInfo Method(Type type, string name, params Type[] parameters) =>
        AccessTools.Method(type, name, parameters) ?? throw new MissingMethodException(type.Name, name);

    static void Frame(object __instance, CharacterDrawList list) => installed?.Draw(__instance, list);
    static void Finished(object __instance) => installed?.Reset(__instance);
    static void FilterDebris(object __instance, out DebrisFilter? __state) =>
        __state = installed is { } hooks && StockHostParts.HostOf(__instance) is { } host ? hooks.SuppressNative(host) : null;

    static void KeepNativeHidden(object __instance)
    {
        if (installed is { } hooks && StockHostParts.HostOf(__instance) is { } host) hooks.HideWorldPlacements(host);
    }
    static Exception? RestoreDebris(Exception? __exception, DebrisFilter? __state)
    {
        if (__state is not { } filter) return __exception;
        foreach (int id in filter.Added) filter.Hidden.Remove(id);
        for (int i = filter.Removed.Count - 1; i >= 0; i--)
        {
            var (index, entry) = filter.Removed[i];
            filter.Falls.Insert(Math.Min(index, filter.Falls.Count), entry);
        }
        return __exception;
    }

    void Draw(object host, CharacterDrawList list)
    {
        if (!StockLimbRules.Enabled || session.GetValue(host) is not WorldSession shown
            || (!displays.TryGetValue(host, out _) && !StockLimbRules.HasCuts(shown.World))
            || renderer.Invoke(host, null) is not { } chars || states.GetValue(chars) is not IDictionary drawn) return;
        int generation = StockHostParts.WorldEpoch(host);
        if (!displays.TryGetValue(host, out var display) || display.Epoch != generation || !ReferenceEquals(display.Renderer, chars)
            || !ReferenceEquals(display.World, shown.World))
        {
            Reset(host);
            display = new(shown.World, generation, chars);
            displays.Add(host, display);
        }
        var actors = shown.CurrentSnapshot.Characters;
        var ground = shown.CurrentSnapshot.GroundItems;
        double time = (shown.ShownTick + (shown.TimeScale > 0 ? shown.SimulationAlpha : 0)) * shown.World.TickSeconds;
        if (display.PreviousTime is { } before && shown.TimeScale > 0 && time > before && display.Detached.Any(limb => !limb.Piece.Frozen))
        {
            float delta = (float)Math.Min(time - before, .10);
            display.Physics.SyncNativeContacts(shown.World, shown.ShownTick, StockNativeRagdollContacts.Scene.Limb,
                id => display.Detached.Any(limb => limb.Cut.Owner == id));
            display.Physics.Step(RagdollPhysics.StepOf(delta, (float)shown.TimeScale), display.Ground, shown.ShownTick, delta);
        }
        display.PreviousTime = time;
        display.ShownItems.Clear();
        display.NativeLabels.Clear();
        display.LooseLabels.Clear();
        for (int i = display.Detached.Count - 1; i >= 0; i--)
        {
            var detached = display.Detached[i];
            if (detached.Item >= 0 ? !ground.Any(item => item.Id == detached.Item) : time >= detached.Expires)
            {
                Release(host, display, detached, drawn);
                if (detached.Item >= 0) StockLimbSave.Remove(display.World, detached.Item);
                display.Detached.RemoveAt(i);
            }
            else
            {
                DrawDetached(host, display, detached, list, drawn);
                PublishPose(display, detached, time);
            }
        }
        foreach (var (owner, retained) in display.Owners.ToArray())
        {
            var actor = actors.FirstOrDefault(character => character.Id == owner);
            if (actor is null || !retained.Cuts.All(cut => HasStump(actor, cut.Shape.LimbSlot))
                || actor.Appearance?.Headless != retained.Source.Headless
                || StockTorsoRules.TryGet(shown.World, owner, shown.ShownTick, out _))
            {
                StockHeadMeshes.Retire(retained.Look);
                display.Owners.Remove(owner);
                if (actor is null) { display.Attempted.Remove(owner); display.Spawned.Remove(owner); }
                continue;
            }
            int index = list.Items.FindIndex(item => item.Key == retained.Key);
            if (index >= 0 && !list.Items[index].Hidden)
                list.Items[index] = list.Items[index] with { Appearance = retained.Look };
        }
        if (display.Attempted.Count > MaxOwners * 2)
            foreach (var owner in display.Attempted.Keys.Where(id => StockLimbRules.For(shown.World, id) is null).ToArray())
            {
                display.Attempted.Remove(owner);
                display.Spawned.Remove(owner);
            }
        if (display.Pending is not null) Prepare(host, display, list, shown, drawn, time);
        else Begin(host, display, list, shown, drawn);
        StockNativeLimbLabels.Publish(shown.World, display.NativeLabels);
        StockPartLabels.Publish(shown.World, "limbs", display.LooseLabels);
    }

    static bool HasStump(CharacterSnapshot actor, int slot) => (LimbState)((actor.LimbBits >> (slot * 2)) & 3) == LimbState.Stump;

    void Begin(object host, Display display, CharacterDrawList list, WorldSession shown, IDictionary drawn)
    {
        if (rigs.Invoke(display.Renderer, null) is not CharacterRigs setup) return;
        foreach (var actor in shown.CurrentSnapshot.Characters)
        {
            if (actor.Appearance is not { } original || actor.Carry is not null
                || StockLimbRules.For(shown.World, actor.Id) is not { } requests) continue;
            bool detachedOnly = StockTorsoRules.TryGet(shown.World, actor.Id, shown.ShownTick, out _);
            var cuts = requests.Where(cut => cut.Tick < shown.ShownTick && HasStump(actor, cut.Shape.LimbSlot)
                && (!detachedOnly || cut.Restoring && StockLimbSave.For(shown.World, cut) is not null)).ToArray();
            if (cuts.Length == 0 || display.Attempted.TryGetValue(actor.Id, out var attempted)
                && ReferenceEquals(attempted.Appearance, original) && cuts.SequenceEqual(attempted.Cuts)) continue;
            if (drawKey.Invoke(host, [actor.Id]) is not long key || !list.Items.Exists(item => item.Key == key && !item.Hidden)
                || lastDrawn.Invoke(display.Renderer, [key, "Bip01 Pelvis"]) is not ValueTuple<Matrix4x4, CharacterAppearance, Vector3> last
                || drawn[key] is not { } source || bones.GetValue(source) is not Matrix4x4[] { Length: > 0 } palette) continue;

            bool fresh = cuts.Any(cut => !cut.Restoring && shown.ShownTick - cut.Tick <= 90
                && (!display.Spawned.TryGetValue(actor.Id, out var finished) || !finished.Contains((cut.Shape.LimbSlot, cut.Tick))));
            if (!detachedOnly && !display.Owners.ContainsKey(actor.Id) && display.Owners.Count >= MaxOwners)
            {
                if (!fresh) continue;
                var oldest = display.Owners.MinBy(entry => entry.Value.Cuts.Max(cut => cut.Tick));
                int oldIndex = list.Items.FindIndex(item => item.Key == oldest.Value.Key && ReferenceEquals(item.Appearance, oldest.Value.Look));
                if (oldIndex >= 0) list.Items[oldIndex] = list.Items[oldIndex] with { Appearance = oldest.Value.Source };
                StockHeadMeshes.Retire(oldest.Value.Look);
                display.Owners.Remove(oldest.Key);
                if (trace) Console.WriteLine($"Brutality stock limb returned oldest stump to native look owner={oldest.Key}");
            }
            if (!StockHeadMeshes.CanCreate && !fresh) continue;
            display.Attempted[actor.Id] = (original, cuts);
            var old = display.Spawned.TryGetValue(actor.Id, out var existing) ? new HashSet<(int Slot, long Tick)>(existing) : [];
            // An old cut can rebuild the stump. Only a positively identified saved ground item can replace its old native limb.
            foreach (var cut in cuts)
                if (cut.Restoring ? StockLimbSave.For(shown.World, cut) is null : shown.ShownTick - cut.Tick > 90)
                    old.Add((cut.Shape.LimbSlot, cut.Tick));
            var cancellation = new CancellationTokenSource();
            var token = cancellation.Token;
            var work = Task.Run(() => StockLimbBuild.Build(setup, original, cuts, old, token), token);
            display.Pending = new(actor.Id, key, original, cuts, last.Item1, palette.ToArray(), work, cancellation, detachedOnly);
            return;
        }
    }

    void Prepare(object host, Display display, CharacterDrawList list, WorldSession shown, IDictionary drawn, double time)
    {
        var pending = display.Pending!;
        if (pending.Abandoned)
        {
            if (pending.Work.IsCompleted) Finish(display);
            return;
        }
        int index = list.Items.FindIndex(item => item.Key == pending.Key && !item.Hidden);
        var actor = shown.CurrentSnapshot.Characters.FirstOrDefault(character => character.Id == pending.Owner);
        if (index < 0 || actor is null || !ReferenceEquals(actor.Appearance, pending.Source)
            || !pending.Cuts.All(cut => HasStump(actor, cut.Shape.LimbSlot))
            || StockTorsoRules.TryGet(shown.World, pending.Owner, shown.ShownTick, out _) != pending.DetachedOnly
            || pending.Timer.Elapsed.TotalSeconds > 6)
        {
            Abandon(display);
            return;
        }
        if (!pending.Work.IsCompleted) return;
        if (!pending.Work.IsCompletedSuccessfully || pending.Work.Result is not { } build)
        {
            if (trace) Console.WriteLine($"Brutality stock limb geometry unavailable: {pending.Work.Exception?.GetBaseException().Message ?? "unsupported limb"}");
            Abandon(display);
            return;
        }
        var current = list.Items[index];
        int retainedCount = pending.DetachedOnly ? 0 : 1;
        int count = retainedCount + build.NewPieces.Count;
        if (pending.Looks.Count < count && (display.Detached.Count + build.NewPieces.Count > MaxDetached || !StockHeadMeshes.CanCreate))
        {
            ReturnSettledLimb();
            return;
        }
        if (pending.Looks.Count < count)
        {
            var piece = pending.Looks.Count < retainedCount ? null : build.NewPieces[pending.Looks.Count - retainedCount];
            var source = piece?.Look ?? build.Remaining;
            if (StockHeadMeshes.CreateTorso(pending.Source, source.Body, source.Tissue, source.Equipment, source.HiddenLimbs) is not { } look)
            {
                ReturnSettledLimb();
                return;
            }
            pending.Looks.Add(new(long.MinValue | (1L << 49) | ++display.Serial, look, piece));
        }
        var loaded = (IDictionary)assets.GetValue(display.Renderer)!;
        bool ready = pending.Looks.Count == count;
        foreach (var entry in pending.Looks)
        {
            if (loaded.Contains(entry.Look))
            {
                if (loaded[entry.Look] is not { } asset || parts.GetValue(asset) is not ICollection assetParts
                    || assetParts.Count != entry.Look.Parts.Count + 1)
                {
                    if (trace) Console.WriteLine("Brutality stock limb mesh upload incomplete");
                    Abandon(display);
                    return;
                }
            }
            else
            {
                list.Add(current with { Key = entry.Key, Appearance = entry.Look, Hidden = true, FreezeFrom = null });
                ready = false;
            }
        }
        if (!ready) return;
        if (!Matrix4x4.Invert(pending.World, out var inverse)
            || StockTorsoPhysics.BoneWorld(build.Rig, pending.Birth, pending.World) is not { } birth
            || display.Detached.Count + build.NewPieces.Count > MaxDetached)
        {
            Abandon(display);
            return;
        }
        var ground = shown.CurrentSnapshot.GroundItems;
        if (!display.Spawned.TryGetValue(pending.Owner, out var spawned))
            display.Spawned[pending.Owner] = spawned = [];
        foreach (var stage in pending.Looks)
        {
            if (stage.Piece is not { } piece) continue;
            var request = piece.Cut;
            var saved = request.Restoring ? StockLimbSave.For(shown.World, request) : null;
            int item = request.Restoring
                ? saved is { } known && ground.Any(g => g.Id == known.Id && request.Records.Contains(g.Record)) ? known.Id : -1
                : FindItem(ground, request);
            if (item < 0 && (request.Restoring || request.Records.Length > 0))
            {
                // No positively matched native item means it was picked up or its draw cannot safely be replaced.
                StockHeadMeshes.Retire(stage.Look);
                spawned.Add((request.Shape.LimbSlot, request.Tick));
                continue;
            }
            var normal = Vector3.TransformNormal(new(piece.Plane.X, piece.Plane.Y, piece.Plane.Z), pending.World);
            if (!float.IsFinite(normal.LengthSquared()) || normal.LengthSquared() <= .000001f) normal = Vector3.UnitY;
            else normal = Vector3.Normalize(normal);
            var travel = request.Travel;
            if (!float.IsFinite(travel.LengthSquared())) travel = default;
            else if (travel.LengthSquared() > 1) travel = Vector2.Normalize(travel);
            float response = RobotRagdollWeight.Response(request.Robot);
            var velocity = (new Vector3(travel.X, 0, travel.Y) * 2.4f - normal) * response;
            velocity.Y = MathF.Min(velocity.Y, 0);
            var world = pending.World;
            var limbInverse = inverse;
            var sourcePalette = pending.Birth;
            var limbBirth = birth;
            if (request.Restoring)
            {
                if (saved is not { Frame: { } frame } || frame.Fragment.Key != 10 + request.Shape.LimbSlot
                    || !Matrix4x4.Invert(frame.World, out limbInverse)
                    || StockTorsoPhysics.BoneWorld(build.Rig, frame.Birth, frame.World) is not { } savedBirth)
                {
                    StockHeadMeshes.Retire(stage.Look);
                    spawned.Add((request.Shape.LimbSlot, request.Tick));
                    continue;
                }
                (world, sourcePalette, limbBirth, velocity) = (frame.World, frame.Birth, savedBirth, Vector3.Zero);
            }
            var physics = display.Physics.Create(piece.Definition, bone => limbBirth[bone], velocity, velocity, request.Seed, limbBirth);
            if (saved is { Frame: { } stored } && !display.Physics.Restore(physics, stored.Fragment))
            {
                display.Physics.Remove(physics);
                StockHeadMeshes.Retire(stage.Look);
                spawned.Add((request.Shape.LimbSlot, request.Tick));
                continue;
            }
            var poses = (Dictionary<int, (Vector3 At, Quaternion Rotation, string Key)>)groundPoses.GetValue(StockHostParts.GroundItems.GetValue(host))!;
            var limb = new Detached(stage.Key, item, stage.Look, build.Rig, world, limbInverse, sourcePalette,
                new Matrix4x4[build.Rig.BoneCount], new Matrix4x4[piece.Definition.Bodies.Length], physics, current.Blood, time + 9,
                poses.TryGetValue(item, out var nativePose) ? nativePose : null, request);
            display.Detached.Add(limb);
            PublishPose(display, limb, time);
            if (!request.Restoring)
            {
                StockRobotDebris.BeginLimb(host, request, pending.Source, build.Rig, piece.Plane,
                    pending.World, pending.Birth, physics, () => display.Detached.Contains(limb), time);
                if (!request.Robot)
                    StockDetachedPools.Limb(host, shown, item, stage.Key, pending.Source.Race, request.Shape.LimbSlot >= 2,
                        () => display.Physics.PoolContact(physics, display.Ground), () => display.Detached.Contains(limb));
            }
            else if (trace) Console.WriteLine($"Brutality stock restored detached limb owner={request.Owner} slot={request.Shape.LimbSlot} item={item}");
            spawned.Add((request.Shape.LimbSlot, request.Tick));
            // The host has already assembled this frame's debris. Draw the replacement next frame after hiding that item.
            if (item >= 0) display.ShownItems.Add(item);
            else DrawDetached(host, display, limb, list, drawn);
            if (trace) Console.WriteLine(FormattableString.Invariant($"Brutality stock limb owner={request.Owner} slot={request.Shape.LimbSlot} cut={request.Shape.Fraction:F2} actors={piece.Definition.Bodies.Length} item={item} delayMs={pending.Timer.Elapsed.TotalMilliseconds:F0} launch={velocity.Length():F2}"));
        }
        if (!pending.DetachedOnly)
        {
            var retained = pending.Looks[0].Look;
            if (display.Owners.Remove(pending.Owner, out var prior)) StockHeadMeshes.Retire(prior.Look);
            display.Owners[pending.Owner] = new(pending.Key, pending.Source, retained, pending.Cuts);
            list.Items[index] = current with { Appearance = retained };
        }
        Finish(display);

        void ReturnSettledLimb()
        {
            int oldIndex = display.Detached.FindIndex(limb => limb.Piece.Frozen);
            if (oldIndex < 0)
            {
                if (StockHeadMeshes.TorsoCapacity(new(1, 1)) == StockHeadMeshes.Capacity.LiveLimit)
                    StockTorsoFragments.RequestRoom(host);
                return;
            }
            var old = display.Detached[oldIndex];
            if (old.Item >= 0)
                StockDetachedPools.ReturnLimbToNative(host, old.Item, display.Physics.PoolContact(old.Piece, display.Ground));
            PublishPose(display, old, time);
            Release(host, display, old, drawn, restorePose: false);
            display.ShownItems.Remove(old.Item);
            if (old.Item >= 0) display.NativeLabels.Remove(old.Item);
            else display.LooseLabels.RemoveAll(label => label.Owner == old.Cut.Owner && label.Piece == LimbLabelId(old));
            display.Detached.RemoveAt(oldIndex);
            list.Items.RemoveAll(item => item.Key == old.Key);
            if (trace) Console.WriteLine($"Brutality stock limb returned settled fragment to native item={old.Item}");
        }
    }

    static void PublishPose(Display display, Detached limb, double time)
    {
        if (limb.Item < 0 || limb.SavedFrozen || time < limb.NextSave && !limb.Piece.Frozen) return;
        StockLimbSave.Publish(display.World, limb.Item, limb.Cut, new(limb.World, limb.Birth,
            display.Physics.Capture(limb.Piece, 10 + limb.Cut.Shape.LimbSlot, "stock-limb", limb.Rig)));
        limb.NextSave = time + .5;
        limb.SavedFrozen = limb.Piece.Frozen;
    }

    static int FindItem(IReadOnlyList<GroundItemSnapshot> ground, StockLimbRules.Request request)
    {
        foreach (var item in ground)
            if (item.Fall is { } fall && fall.Character == request.Owner && fall.Bone == request.Bone
                && fall.Tick >= request.Tick && fall.Tick - request.Tick <= 2 && request.Records.Contains(item.Record))
                return item.Id;
        return -1;
    }

    void DrawDetached(object host, Display display, Detached limb, CharacterDrawList list, IDictionary drawn)
    {
        if (limb.Item >= 0 && ((HashSet<int>)hiddenGround.GetValue(StockHostParts.GroundItems.GetValue(host))!).Contains(limb.Item)) return;
        AdvancedFragmentScene.FillStockPalette(limb.Piece, limb.Rig, limb.Birth, limb.World, limb.Inverse, limb.Palette, limb.Motion);
        var root = limb.Piece.Pose[limb.Piece.Definition.Root];
        if (drawn[limb.Key] is not { } state || frozen.GetValue(state) is not true)
        {
            state = Activator.CreateInstance(stateType, nonPublic: true)!;
            frozen.SetValue(state, true);
            var now = stamp.GetValue(display.Renderer);
            posed.SetValue(state, now);
            seen.SetValue(state, now);
            drawn[limb.Key] = state;
        }
        bones.SetValue(state, limb.Palette);
        previous.SetValue(state, limb.Palette);
        list.Add(new CharacterInstance(limb.Key, limb.Look, root.Position, 0, [])
            { World = limb.World, FreezeFrom = limb.Key, Blood = limb.Blood });
        string name = limb.Cut.Shape.LimbSlot switch
        {
            0 => "Left arm",
            1 => "Right arm",
            2 => "Left leg",
            _ => "Right leg",
        };
        if (limb.Item < 0)
        {
            display.LooseLabels.Add(new(limb.Cut.Owner, LimbLabelId(limb), name, root.Position));
            return;
        }
        display.ShownItems.Add(limb.Item);
        display.NativeLabels[limb.Item] = new(root.Position, name);
        if (limb.NativePose is { } fallback)
        {
            var poses = (Dictionary<int, (Vector3 At, Quaternion Rotation, string Key)>)groundPoses.GetValue(StockHostParts.GroundItems.GetValue(host))!;
            if (poses.ContainsKey(limb.Item)) poses[limb.Item] = (root.Position, root.Rotation, fallback.Key);
        }
    }

    static int LimbLabelId(Detached limb) => 10 + limb.Cut.Shape.LimbSlot;

    DebrisFilter? SuppressNative(object host)
    {
        if (!displays.TryGetValue(host, out var display) || display.ShownItems.Count == 0) return null;
        var falls = (IList)nativeFalls.GetValue(StockHostParts.Debris.GetValue(host))!;
        var hidden = (HashSet<int>)hiddenGround.GetValue(StockHostParts.GroundItems.GetValue(host))!;
        var filter = new DebrisFilter(falls, [], hidden, []);
        foreach (int id in display.ShownItems)
            if (hidden.Add(id)) filter.Added.Add(id);
        for (int i = falls.Count - 1; i >= 0; i--)
        {
            if (falls[i] is not { } entry || fallingItem.GetValue(entry) is not GroundItemSnapshot item
                || !display.ShownItems.Contains(item.Id)) continue;
            filter.Removed.Add((i, entry));
            falls.RemoveAt(i);
        }
        return filter;
    }

    void HideWorldPlacements(object host)
    {
        if (!displays.TryGetValue(host, out var display) || display.ShownItems.Count == 0) return;
        var keys = (HashSet<string>)hiddenKeys.GetValue(StockHostParts.GroundItems.GetValue(host))!;
        foreach (int id in display.ShownItems)
            if (groundKey.Invoke(null, [id]) is string key) keys.Add(key);
    }

    void Release(object host, Display display, Detached limb, IDictionary drawn, bool restorePose = true)
    {
        drawn.Remove(limb.Key);
        display.Physics.Remove(limb.Piece);
        StockHeadMeshes.Retire(limb.Look);
        if (!restorePose || limb.NativePose is not { } pose) return;
        var poses = (Dictionary<int, (Vector3 At, Quaternion Rotation, string Key)>)groundPoses.GetValue(StockHostParts.GroundItems.GetValue(host))!;
        if (poses.ContainsKey(limb.Item)) poses[limb.Item] = pose;
    }

    void Reset(object host)
    {
        if (!displays.TryGetValue(host, out var display)) return;
        Abandon(display);
        if (display.Pending is { } pending)
            _ = pending.Work.ContinueWith(task => { _ = task.Exception; pending.Cancellation.Dispose(); }, TaskScheduler.Default);
        foreach (var retained in display.Owners.Values) StockHeadMeshes.Retire(retained.Look);
        bool sameWorld = StockHostParts.WorldEpoch(host) == display.Epoch
            && session.GetValue(host) is WorldSession shown && ReferenceEquals(shown.World, display.World);
        if (states.GetValue(display.Renderer) is IDictionary drawn)
            foreach (var detached in display.Detached) Release(host, display, detached, drawn, sameWorld);
        display.Physics.Dispose();
        display.NativeLabels.Clear();
        display.LooseLabels.Clear();
        displays.Remove(host);
    }

    static void Abandon(Display display)
    {
        if (display.Pending is not { } pending) return;
        pending.Abandoned = true;
        pending.Cancellation.Cancel();
        foreach (var stage in pending.Looks) StockHeadMeshes.Retire(stage.Look);
        pending.Looks.Clear();
        if (pending.Work.IsCompleted) Finish(display);
    }

    static void Finish(Display display)
    {
        var pending = display.Pending!;
        _ = pending.Work.Exception;
        pending.Cancellation.Dispose();
        display.Pending = null;
    }
}
