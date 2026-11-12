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
using Meitou.Simulation.Ragdolls;
using Meitou.Simulation.Carrying;

namespace Meitou.Brutality;

/// <summary>Draws two articulated fragments in place of the native corpse once both looks are ready.</summary>
internal sealed class StockTorsoFragments
{
    // Physics separately limits awake bodies; settled pieces remain visible within the mesh budget.
    const int MaxCuts = 24, MaximumRetries = 2, MaximumGeometryJobs = 2;
    const int TorsoLimbLabelBase = 1024;
    static StockTorsoFragments? installed;
    readonly ConditionalWeakTable<object, Display> displays = new();
    readonly ModContext context;
    readonly FieldInfo session, states, assets, stamp, bones, previous, attach, previousAttach, posed, seen, frozen;
    readonly FieldInfo parts, attachCount, attachIndex, partBone, partOffset, buildEye, stateWorld;
    readonly bool motionHistory;
    readonly MethodInfo renderer, rigs, drawKey, lastDrawn, bonePosition, prefetch;
    readonly Type stateType;
    readonly bool trace = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_TRACE_HITS") == "1";
    bool anatomyWarmRequested;

    sealed class Display(World world, int epoch, object renderer)
    {
        public readonly World World = world;
        public readonly int Epoch = epoch;
        public readonly object Renderer = renderer;
        public readonly IRagdollEnvironment Ground = new WalkabilityEnvironment(world.Walkability);
        public readonly AdvancedFragmentScene Physics = new();
        public readonly List<Split> Active = [];
        public readonly HashSet<CharacterId> Attempted = [];
        public readonly Dictionary<CharacterId, Retry> Retries = [];
        public readonly Dictionary<CharacterId, Dormant> DormantCuts = [];
        public readonly HashSet<long> HiddenDormant = [];
        public readonly List<Pending> Pending = [];
        public readonly List<BrutalityPartLabel> Labels = [];
        public bool NeedsSharedRoom;
        public long RehydrateAfter;
        public double? PreviousTime;
        public long Serial;
    }

    readonly record struct Retry(int Count, long Started);
    readonly record struct Dormant(long Key, StockHeadMeshes.Cost Cost);
    readonly record struct Candidate(StockTorsoRules.CutRequest Request, long Key, CharacterAppearance Look, CharacterSnapshot Actor,
        bool Dormant, bool Carried, int Priority, float Distance);

    sealed class Pending(StockTorsoRules.CutRequest request, long key, CharacterAppearance source,
        Task<StockTorsoPhysics.Prepared?> work, CancellationTokenSource cancel, bool dormant, bool carried)
    {
        public readonly StockTorsoRules.CutRequest Request = request;
        public readonly long Key = key;
        public readonly CharacterAppearance Source = source;
        public readonly Task<StockTorsoPhysics.Prepared?> Work = work;
        public readonly CancellationTokenSource Cancel = cancel;
        public readonly Stopwatch Timer = Stopwatch.StartNew();
        public readonly List<Staged> Looks = [];
        public readonly bool Dormant = dormant, Carried = carried;
        public StockHeadMeshes.Cost? Cost;
        public long QueueTicks;
        public double? ReadyMs, StagedMs, UploadMs;
        public bool WaitingForCapacity;
        public bool Abandoned;
    }

    sealed record Staged(long Key, int Side, CharacterAppearance Look);
    sealed record Attachment(int Slot, int Bone, Matrix4x4 Offset);
    sealed record Piece(Staged Look, AdvancedFragmentScene.Piece Physics, Matrix4x4[] Palette, Matrix4x4[] ActorMotion,
        Matrix4x4[] Attachments, Attachment[] Tags, int CutBone, IReadOnlyList<FragmentLimbLabels.Anchor> LimbLabels)
    {
        public long DrawKey = Look.Key;
        public CharacterId Target = CharacterId.None;
        public CharacterId Holder = CharacterId.None;
        public AdvancedFragmentScene.StockCarryGrip? CarryGrip;
        public bool HasRestingPalette;
        public readonly Matrix4x4[] PreviousPalette = new Matrix4x4[Palette.Length];
        public readonly Matrix4x4[] PreviousAttachments = new Matrix4x4[Attachments.Length];
        public long MotionFrame = -1;
        public object? MotionState;

        public void RememberPalette()
        {
            Palette.CopyTo(PreviousPalette, 0);
            Attachments.CopyTo(PreviousAttachments, 0);
        }
    }
    sealed record Split(CharacterId Owner, long OwnerKey, Matrix4x4 World, Matrix4x4 Inverse,
        SkeletonRig Rig, Matrix4x4[] Birth, Piece[] Pieces, Vector3 CutPoint, Vector3 CutNormal, CharacterInstance Template, StockHeadMeshes.Cost Cost)
    {
        public double NextSave;
        public bool SavedFrozen;
    }

    StockTorsoFragments(ModContext context, Type host, Type chars)
    {
        this.context = context;
        session = Field(host, "Session");
        renderer = AccessTools.PropertyGetter(host, "ActiveCharacters") ?? throw new MissingMemberException("ActiveCharacters");
        rigs = AccessTools.PropertyGetter(chars, "Rigs") ?? throw new MissingMemberException("CharacterRenderer.Rigs");
        prefetch = Method(chars, "Prefetch", typeof(CharacterAppearance));
        drawKey = Method(host, "DrawKey", typeof(CharacterId));
        lastDrawn = Method(chars, "LastDrawn", typeof(long), typeof(string));
        bonePosition = Method(chars, "BonePosition", typeof(long), typeof(string));
        states = Field(chars, "_states"); assets = Field(chars, "_assets"); stamp = Field(chars, "_updateStamp");
        stateType = states.FieldType.GetGenericArguments()[1];
        buildEye = Field(chars, "_buildEye");
        bones = Field(stateType, "Bones"); previous = Field(stateType, "Previous");
        attach = Field(stateType, "Attach"); previousAttach = Field(stateType, "PreviousAttach");
        posed = Field(stateType, "Posed"); seen = Field(stateType, "Seen"); frozen = Field(stateType, "Frozen");
        stateWorld = Field(stateType, "World");
        motionHistory = StockTorsoMotion.Install(chars, context);
        var asset = assets.FieldType.GetGenericArguments()[1];
        parts = Field(asset, "Parts"); attachCount = Field(asset, "AttachCount");
        var part = parts.FieldType.GetGenericArguments()[0];
        attachIndex = Field(part, "AttachIndex"); partBone = Field(part, "Bone"); partOffset = Field(part, "Offset");
    }

    internal static bool Install(ModContext context)
    {
        if (!StockHeadMeshes.Install(context)) return false;
        try
        {
            var host = AccessTools.TypeByName("Meitou.Game.GameHost") ?? throw new TypeLoadException("GameHost");
            var chars = AccessTools.TypeByName("Meitou.Game.Rendering.Characters.CharacterRenderer") ?? throw new TypeLoadException("CharacterRenderer");
            var hooks = new StockTorsoFragments(context, host, chars);
            context.Harmony.Patch(Method(StockHostParts.Decapitation.FieldType, "AddHeads", typeof(CharacterDrawList)),
                postfix: new HarmonyMethod(typeof(StockTorsoFragments), nameof(HeadsListed)));
            context.Harmony.Patch(Method(host, "Run"),
                finalizer: new HarmonyMethod(typeof(StockTorsoFragments), nameof(HostFinished)));
            installed = hooks;
            return true;
        }
        catch (Exception error)
        {
            context.Log($"Brutality stock torso fragments unavailable: {error.GetBaseException().Message}");
            return false;
        }
    }

    static FieldInfo Field(Type type, string name) => AccessTools.Field(type, name) ?? throw new MissingFieldException(type.Name, name);
    static MethodInfo Method(Type type, string name, params Type[] args) => AccessTools.Method(type, name, args) ?? throw new MissingMethodException(type.Name, name);
    static void HostFinished(object __instance) => installed?.Reset(__instance);
    static void HeadsListed(object __instance, CharacterDrawList list)
    {
        if (installed is { } hooks && StockHostParts.HostOf(__instance) is { } host) hooks.Draw(host, list);
    }

    internal static void RequestRoom(object host)
    {
        if (installed?.displays.TryGetValue(host, out var display) == true) display.NeedsSharedRoom = true;
    }

    void Draw(object host, CharacterDrawList list)
    {
        if (session.GetValue(host) is not WorldSession shown) return;
        if (!anatomyWarmRequested)
        {
            anatomyWarmRequested = true;
            StockTorsoBuild.WarmAnatomy(context.Folder);
        }
        bool hasCuts = StockTorsoRules.HasCuts(shown.World);
        if (!hasCuts && !displays.TryGetValue(host, out _)) return;
        if (renderer.Invoke(host, null) is not { } chars || states.GetValue(chars) is not IDictionary drawn) return;
        int currentEpoch = StockHostParts.WorldEpoch(host);
        if (displays.TryGetValue(host, out var display)
            && (!ReferenceEquals(display.Renderer, chars) || !ReferenceEquals(display.World, shown.World) || display.Epoch != currentEpoch))
        {
            Reset(host);
            display = null;
        }
        if (display is null && !hasCuts) return;
        display ??= displays.GetValue(host, _ => new(shown.World, currentEpoch, chars));
        var actors = shown.CurrentSnapshot.Characters;
        double time = (shown.ShownTick + (shown.TimeScale > 0 ? shown.SimulationAlpha : 0)) * shown.World.TickSeconds;
        if (display.PreviousTime is { } before && shown.TimeScale > 0 && time > before
            && display.Active.Any(split => split.Pieces.Any(piece => !piece.Physics.Frozen)))
        {
            float delta = (float)Math.Min(time - before, .10);
            display.Physics.SyncNativeContacts(shown.World, shown.ShownTick, StockNativeRagdollContacts.Scene.Torso,
                id => display.Active.Any(split => split.Owner == id));
            display.Physics.Step(RagdollPhysics.StepOf(delta, (float)shown.TimeScale), display.Ground, shown.ShownTick, delta);
        }
        display.PreviousTime = time;
        for (int i = display.Active.Count - 1; i >= 0; i--)
        {
            var split = display.Active[i];
            if (actors.FirstOrDefault(c => c.Id == split.Owner) is not { Body.Dead: true })
            {
                Release(display, split, drawn);
                display.Active.RemoveAt(i);
                continue;
            }
            int index = list.Items.FindIndex(item => item.Key == split.OwnerKey);
            Replace(host, display, split, list, index, drawn, actors);
            PublishSavePose(display, split, time);
        }
        bool retirementTried = display.NeedsSharedRoom;
        if (retirementTried)
        {
            RetireSettled(display, list, drawn, actors, time);
            display.RehydrateAfter = shown.ShownTick + 90;
        }
        display.NeedsSharedRoom = false;
        if (display.Pending.Count > 0)
        {
            // Overlap geometry with preparation and upload of the previous pair. Only one pair at a time owns upload.
            bool uploadBusy = display.Pending.Any(pending => pending.Looks.Count > 0);
            foreach (var pending in display.Pending.ToArray())
                Prepare(host, display, pending, list, drawn, actors, time, ref uploadBusy, ref retirementTried);
        }
        if (hasCuts && display.Pending.Count < MaximumGeometryJobs) Begin(host, display, list, shown, allowDormant: !retirementTried);
        if (display.DormantCuts.Count > 0)
        {
            display.HiddenDormant.Clear();
            foreach (var (owner, dormant) in display.DormantCuts.ToArray())
                if (!StockTorsoRules.TryGetSaved(shown.World, owner, out _)) display.DormantCuts.Remove(owner);
                else display.HiddenDormant.Add(dormant.Key);
            foreach (var actor in actors)
                if (!actor.CarriedBy.IsNone && display.DormantCuts.TryGetValue(actor.Id, out var held))
                    display.HiddenDormant.Remove(held.Key);
            // A streamed-out split must not flicker back to an intact native corpse.
            list.Items.RemoveAll(item => display.HiddenDormant.Contains(item.Key));
        }
        PublishLabels(display);
    }

    void PublishLabels(Display display)
    {
        display.Labels.Clear();
        long frame = (long)stamp.GetValue(display.Renderer)!;
        foreach (var split in display.Active)
            foreach (var piece in split.Pieces)
            {
                if (piece.MotionFrame != frame) continue;
                bool positive = piece.Look.Side > 0;
                int key = positive ? 0 : 1;
                var definition = piece.Physics.Definition;
                bool rootNamed = false;
                foreach (var limb in piece.LimbLabels)
                {
                    int body = definition.BodyOfBone[limb.Bone];
                    int limbKey = limb.Root ? key : TorsoLimbLabelBase + limb.Bone * 2 + key;
                    display.Labels.Add(new(split.Owner, limbKey, limb.Name, piece.Physics.Pose[body].Position));
                    rootNamed |= limb.Root;
                }
                if (rootNamed) continue;
                string name = MathF.Abs(split.CutNormal.Y) >= .75f
                    ? positive ? "Upper body" : "Lower body"
                    : positive ? "Body half 1" : "Body half 2";
                display.Labels.Add(new(split.Owner, key, name, piece.Physics.Pose[definition.Root].Position));
            }
        StockPartLabels.Publish(display.World, "torso", display.Labels);
    }

    void Begin(object host, Display display, CharacterDrawList list, WorldSession shown, bool allowDormant)
    {
        if (rigs.Invoke(display.Renderer, null) is not CharacterRigs setup) return;
        if (display.Attempted.Count + display.Retries.Count > 128)
        {
            display.Attempted.RemoveWhere(id => !StockTorsoRules.TryGetSaved(shown.World, id, out _));
            foreach (var id in display.Retries.Keys.Where(id => !StockTorsoRules.TryGetSaved(shown.World, id, out _)).ToArray())
                display.Retries.Remove(id);
        }
        var candidates = new List<Candidate>();
        var actors = shown.CurrentSnapshot.Characters;
        var eye = buildEye.GetValue(display.Renderer) is Vector3 viewer ? viewer : Vector3.Zero;
        bool routineRestorePending = display.Pending.Any(pending => pending.Dormant && !pending.Carried);
        bool freshCutPending = display.Pending.Any(pending => !pending.Dormant && !pending.Request.Restoring);
        var wounds = StockWoundRules.FrameAt(shown.World, shown.ShownTick)?.Actors;
        foreach (var actor in actors)
        {
            if (actor is not { Body.Dead: true } || display.Attempted.Contains(actor.Id)
                || !StockTorsoRules.TryGet(shown.World, actor.Id, shown.ShownTick, out var request)
                || drawKey.Invoke(host, [actor.Id]) is not long key) continue;
            bool retry = display.Retries.TryGetValue(actor.Id, out var previous);
            if (retry && Stopwatch.GetElapsedTime(previous.Started).TotalSeconds < .5 * previous.Count) continue;
            bool dormant = display.DormantCuts.TryGetValue(actor.Id, out var old);
            bool carried = !actor.CarriedBy.IsNone || dormant && Carried(actors, display.World, actor.Id);
            if (dormant && !carried && (!allowDormant || routineRestorePending || freshCutPending || shown.ShownTick < display.RehydrateAfter
                || display.Active.Count >= MaxCuts || StockHeadMeshes.TorsoCapacity(old.Cost) != StockHeadMeshes.Capacity.Available)) continue;
            int index = list.Items.FindIndex(item => item.Key == key && !item.Hidden);
            if (index < 0 || !dormant && LastBody(display.Renderer, key) is null) continue;
            int priority = carried ? 0 : dormant ? 4 : retry ? 3 : request.Restoring ? 2 : 1;
            var item = list.Items[index];
            candidates.Add(new(request, key, item.Appearance, actor, dormant, carried, priority,
                Vector3.DistanceSquared(eye, item.Position)));
        }
        foreach (var candidate in candidates.OrderBy(entry => entry.Priority).ThenBy(entry => entry.Request.Tick).ThenBy(entry => entry.Distance))
        {
            if (display.Pending.Count == MaximumGeometryJobs) break;
            if (candidate.Dormant && !candidate.Carried)
            {
                if (routineRestorePending || freshCutPending) continue;
                routineRestorePending = true;
            }
            var victim = candidate.Request.Victim;
            freshCutPending |= !candidate.Dormant && !candidate.Request.Restoring;
            var actor = candidate.Actor;
            var limbs = StockLimbRules.For(shown.World, victim)?.Where(cut => cut.Tick < shown.ShownTick
                && (LimbState)((actor.LimbBits >> (cut.Shape.LimbSlot * 2)) & 3) == LimbState.Stump).ToArray() ?? [];
            var marks = wounds?.GetValueOrDefault(victim);
            StockTorsoBuild.Prior? prior = limbs.Length == 0 && marks is null ? null : new(limbs, marks);
            display.Attempted.Add(victim);
            var cancel = new CancellationTokenSource();
            var token = cancel.Token;
            var saved = candidate.Request.Restoring || candidate.Dormant ? StockTorsoSave.For(display.World, victim) : null;
            var work = Task.Run(() => StockTorsoPhysics.Build(context.Folder, setup, candidate.Look, candidate.Request.Shape,
                token, prior, saved), token);
            var pending = new Pending(candidate.Request, candidate.Key, candidate.Look, work, cancel, candidate.Dormant, candidate.Carried)
                { QueueTicks = Math.Max(0, shown.ShownTick - candidate.Request.Tick) };
            display.Pending.Add(pending);
            if (trace) Console.WriteLine($"Brutality stock torso queued owner={victim} priority={candidate.Priority} jobs={display.Pending.Count} queueTicks={pending.QueueTicks}");
        }
    }

    (Matrix4x4 World, CharacterAppearance Look, Vector3 Bone)? LastBody(object chars, long key) =>
        lastDrawn.Invoke(chars, [key, "Bip01 Pelvis"]) is ValueTuple<Matrix4x4, CharacterAppearance, Vector3> value ? value : null;

    void Prepare(object host, Display display, Pending pending, CharacterDrawList list, IDictionary drawn,
        IReadOnlyList<CharacterSnapshot> actors, double time, ref bool uploadBusy, ref bool retirementTried)
    {
        if (pending.Abandoned)
        {
            if (pending.Work.IsCompleted) Finish(display, pending);
            return;
        }
        int index = list.Items.FindIndex(item => item.Key == pending.Key && !item.Hidden);
        if (index < 0 || pending.Timer.Elapsed.TotalSeconds > (pending.WaitingForCapacity ? 30 : 8))
        {
            Abandon(display, pending, index < 0 ? "corpse left the draw list" : "preparation timed out");
            return;
        }
        if (!ReferenceEquals(list.Items[index].Appearance, pending.Source))
        {
            Abandon(display, pending, "native corpse appearance changed");
            return;
        }
        if (!pending.Work.IsCompleted) return;
        if (!pending.Work.IsCompletedSuccessfully || pending.Work.Result is not { } ready)
        {
            if (trace) Console.WriteLine($"Brutality stock torso geometry unavailable: {pending.Work.Exception?.GetBaseException().Message ?? "no usable cut"}");
            Abandon(display, pending);
            return;
        }
        if (trace) pending.ReadyMs ??= pending.Timer.Elapsed.TotalMilliseconds;
        var original = list.Items[index];
        if (pending.Looks.Count == 0)
        {
            if (uploadBusy) return;
            bool foreground = !pending.Dormant || pending.Carried;
            if (display.Active.Count >= MaxCuts)
            {
                if (!retirementTried && foreground)
                {
                    retirementTried = true;
                    RetireSettled(display, list, drawn, actors, time);
                }
                if (display.Active.Count >= MaxCuts)
                {
                    pending.WaitingForCapacity = true;
                    return;
                }
            }
            var cost = pending.Cost ??= StockHeadMeshes.TorsoCost(ready.Cut.Pieces);
            var capacity = StockHeadMeshes.TorsoCapacity(cost, foreground: foreground);
            if (capacity != StockHeadMeshes.Capacity.Available)
            {
                if (capacity == StockHeadMeshes.Capacity.TooLarge) { Abandon(display, pending); return; }
                if (!pending.WaitingForCapacity && trace)
                    Console.WriteLine($"Brutality stock torso waiting for capacity owner={pending.Request.Victim} need={cost.Meshes} meshes status={capacity}");
                pending.WaitingForCapacity = true;
                if (!retirementTried && capacity == StockHeadMeshes.Capacity.LiveLimit && foreground)
                {
                    retirementTried = true;
                    if (RetireSettled(display, list, drawn, actors, time))
                        capacity = StockHeadMeshes.TorsoCapacity(cost, foreground: foreground);
                }
                if (capacity != StockHeadMeshes.Capacity.Available) return;
            }
            // Admit the pair together. The stock renderer still uploads them within its per-frame budget.
            uploadBusy = true;
            foreach (var fragment in ready.Cut.Pieces)
            {
                if (StockHeadMeshes.CreateTorso(original.Appearance, fragment.Body, fragment.Tissue, fragment.Equipment,
                        fragment.HiddenLimbs, fragment.ShowHead, foreground: foreground) is not { } appearance)
                {
                    Abandon(display, pending, "could not reserve both generated halves");
                    return;
                }
                pending.Looks.Add(new(long.MinValue | (1L << 50) | ++display.Serial, fragment.Side, appearance));
            }
            // The stock renderer still controls GPU upload. Start preparation for both halves ahead of ordinary looks.
            foreach (var fragment in pending.Looks) prefetch.Invoke(display.Renderer, [fragment.Look]);
        }
        if (trace) pending.StagedMs ??= pending.Timer.Elapsed.TotalMilliseconds;
        var loaded = (IDictionary)assets.GetValue(display.Renderer)!;
        bool uploaded = pending.Looks.Count == ready.Cut.Pieces.Count;
        foreach (var fragment in pending.Looks)
        {
            if (loaded.Contains(fragment.Look))
            {
                if (loaded[fragment.Look] is not { } asset || parts.GetValue(asset) is not ICollection items
                    || items.Count != fragment.Look.Parts.Count + 1)
                {
                    Abandon(display, pending, $"incomplete upload side={fragment.Side}");
                    return;
                }
                continue;
            }
            list.Add(original with { Key = fragment.Key, Appearance = fragment.Look, Hidden = true });
            uploaded = false;
        }
        if (!uploaded) return;
        if (trace) pending.UploadMs ??= pending.Timer.Elapsed.TotalMilliseconds;

        var rig = ready.Cut.Rig;
        var request = pending.Dormant ? pending.Request with { Restoring = true } : pending.Request;
        var restore = request.Restoring ? StockTorsoSave.For(display.World, request.Victim) : null;
        Matrix4x4 world, inverse;
        Matrix4x4[] skin;
        Meitou.Brutality.Physics.RigidTransform[] birth;
        if (restore is { } saved && Matrix4x4.Invert(saved.World, out var savedInverse)
            && StockTorsoPhysics.BoneWorld(rig, saved.Birth, saved.World) is { } savedBirth)
            (world, inverse, skin, birth) = (saved.World, savedInverse, saved.Birth, savedBirth);
        else
        {
            restore = null;
            if (pending.Dormant) { Abandon(display, pending, "saved pose is unavailable"); return; }
            if (LastBody(display.Renderer, pending.Key) is not { World: var at } || !Matrix4x4.Invert(at, out inverse)
                || drawn[pending.Key] is not { } source || bones.GetValue(source) is not Matrix4x4[] pose
                || StockTorsoPhysics.BoneWorld(rig, pose, at) is not { } liveBirth)
                return;
            (world, skin, birth) = (at, pose, liveBirth);
        }
        var localNormal = new Vector3(ready.Cut.Plane.X, ready.Cut.Plane.Y, ready.Cut.Plane.Z);
        int spine = rig.Handle("Bip01 Spine1") ?? rig.Handle("Bip01 Pelvis")!.Value;
        var cutPoint = rig.BindPosition[spine] - localNormal * Vector4.Dot(ready.Cut.Plane, new(rig.BindPosition[spine], 1));
        var normal = Vector3.TransformNormal(localNormal, skin[spine] * world);
        if (!float.IsFinite(normal.LengthSquared()) || normal.LengthSquared() < 1e-6f) { Abandon(display, pending); return; }
        normal = Vector3.Normalize(normal);
        var travel = pending.Request.Travel;
        if (!float.IsFinite(travel.LengthSquared())) travel = default;
        else if (travel.LengthSquared() > 1) travel = Vector2.Normalize(travel);
        float response = RobotRagdollWeight.Response(pending.Request.Robot);
        var push = new Vector3(travel.X, 0, travel.Y) * 3 * response;
        var spread = new Vector3(normal.X, 0, normal.Z);
        if (spread.LengthSquared() < .04f)
            spread = travel.LengthSquared() > .01f ? new(-travel.Y, 0, travel.X) : Vector3.UnitX;
        spread = Vector3.Normalize(spread);
        var pieces = new List<Piece>(2);
        foreach (var stage in pending.Looks)
        {
            var definition = stage.Side > 0 ? ready.Positive : ready.Negative;
            var speed = push + spread * (4 * stage.Side * response);
            var physical = display.Physics.Create(definition, bone => birth[bone],
                request.Restoring ? Vector3.Zero : speed, request.Restoring ? Vector3.Zero : push, request.Seed, birth);
            if (restore is { } savedPose && display.Physics.Restore(physical, stage.Side > 0 ? savedPose.Positive : savedPose.Negative) && trace)
                Console.WriteLine($"Brutality stock restored torso side={stage.Side}");
            if (stage.Side < 0 && restore is null && pending.Request.Restoring
                && StockTorsoProxySave.MigratedLowerPosition(display.World, pending.Request.Victim) is { } imported)
                display.Physics.MoveStockGrip(physical, physical.Pose[definition.Root].Position, imported, Quaternion.Identity);
            var asset = loaded[stage.Look]!;
            var attachments = ((IList)parts.GetValue(asset)!).Cast<object>()
                .Select(part => new Attachment((int)attachIndex.GetValue(part)!, (int)partBone.GetValue(part)!, (Matrix4x4)partOffset.GetValue(part)!))
                .Where(tag => tag.Slot >= 0).ToArray();
            var look = stage.Side > 0 ? stage with { Key = pending.Key } : stage;
            var anchor = definition.Bodies.Where(body => body.Name.StartsWith("Bip01 Spine", StringComparison.Ordinal)
                || body.Name is "Bip01 Pelvis" or "Bip01")
                .MinBy(body => Vector3.DistanceSquared(body.Bind.Position, cutPoint)) ?? definition.Bodies[definition.Root];
            IReadOnlyList<FragmentLimbLabels.Anchor> limbLabels = request.Shape.FullBody
                ? FragmentLimbLabels.For(definition, AdvancedFragmentScene.StockBoneComponents(definition, rig)) : [];
            pieces.Add(new(look, physical, new Matrix4x4[rig.BoneCount], new Matrix4x4[definition.Bodies.Length],
                new Matrix4x4[(int)attachCount.GetValue(asset)!], attachments, anchor.Bone, limbLabels));
        }
        if (!request.Restoring) display.Physics.SeparateTorsoPair(pieces[0].Physics, pieces[1].Physics);
        var split = new Split(request.Victim, pending.Key, world, inverse, rig, skin.ToArray(), pieces.ToArray(), cutPoint, localNormal, original, pending.Cost!.Value);
        display.Active.Add(split);
        display.Retries.Remove(split.Owner);
        display.DormantCuts.Remove(split.Owner);
        Replace(host, display, split, list, index, drawn, actors);
        PublishSavePose(display, split, time);
        if (!pending.Dormant && Contact(host, split.Owner, 0) is { } point) MasterImpactFeed.BeginTorso(host, request, original.Appearance, rig, point.Position, time);
        if (trace && pending.Dormant) Console.WriteLine($"Brutality stock torso rehydrated owner={split.Owner}");
        else if (trace)
        {
            double readyMs = pending.ReadyMs ?? 0, stagedMs = pending.StagedMs ?? 0, uploadMs = pending.UploadMs ?? 0;
            double total = pending.Timer.Elapsed.TotalMilliseconds;
            Console.WriteLine(FormattableString.Invariant($"Brutality stock torso timing owner={split.Owner} queueTicks={pending.QueueTicks} prepareMs={readyMs:F0} admitMs={stagedMs - readyMs:F0} uploadMs={uploadMs - stagedMs:F0} handoffMs={total - uploadMs:F0}"));
            Console.WriteLine(FormattableString.Invariant($"Brutality stock torso cut fullBody={pending.Request.Shape.FullBody} robot={pending.Request.Robot} actors={ready.Positive.Bodies.Length}+{ready.Negative.Bodies.Length} delayMs={total:F0} owner={split.Owner}"));
        }
        Finish(display, pending);
    }

    static void PublishSavePose(Display display, Split split, double time)
    {
        if (split.SavedFrozen || time < split.NextSave) return;
        var positive = split.Pieces.First(piece => piece.Look.Side > 0).Physics;
        var negative = split.Pieces.First(piece => piece.Look.Side < 0).Physics;
        StockTorsoSave.Publish(display.World, split.Owner, new(split.World, split.Birth,
            display.Physics.Capture(positive, 0, "stock-torso", split.Rig),
            display.Physics.Capture(negative, 1, "stock-torso", split.Rig)));
        split.NextSave = time + .5;
        split.SavedFrozen = positive.Frozen && negative.Frozen;
    }

    internal static CutFluidContact? Contact(object host, CharacterId owner, int half)
    {
        if (installed is not { } hooks || !hooks.displays.TryGetValue(host, out var display)
            || display.Active.FirstOrDefault(split => split.Owner == owner) is not { } cut
            || cut.Pieces.FirstOrDefault(piece => piece.Look.Side == (half == 0 ? 1 : -1)) is not { } piece) return null;
        var transform = piece.Palette[piece.CutBone] * FrameWorld(cut, piece, out _);
        var normal = Vector3.TransformNormal(cut.CutNormal * -piece.Look.Side, transform);
        var position = Vector3.Transform(cut.CutPoint, transform);
        if (!float.IsFinite(position.LengthSquared()) || !float.IsFinite(normal.LengthSquared()) || normal.LengthSquared() < 1e-8f)
            return null;
        return new(position, Vector3.Normalize(normal));
    }

    static Matrix4x4 FrameWorld(Split split, Piece piece, out Matrix4x4 inverse)
    {
        // The native motion-vector pass tracks instance placement even when the palette is supplied externally.
        int root = piece.Physics.Definition.Root;
        var movement = piece.Physics.Pose[root].Position - piece.Physics.InitialPose[root].Position;
        inverse = Matrix4x4.CreateTranslation(-movement) * split.Inverse;
        return split.World * Matrix4x4.CreateTranslation(movement);
    }

    void Replace(object host, Display display, Split split, CharacterDrawList list, int index, IDictionary drawn,
        IReadOnlyList<CharacterSnapshot> actors)
    {
        var original = index >= 0 ? list.Items[index] : split.Template;
        if (index >= 0) list.Items.RemoveAt(index);
        var frame = StockTorsoProxies.For(display.World)?.Frame;
        foreach (var piece in split.Pieces)
        {
            var target = piece.Look.Side > 0 ? split.Owner
                : frame?.TryProxy(split.Owner, out var proxy) == true ? proxy : CharacterId.None;
            var lastHolder = piece.Holder;
            var oldKey = piece.DrawKey;
            var snapshot = actors.FirstOrDefault(actor => actor.Id == target);
            if (snapshot is not null && !snapshot.CarriedBy.IsNone)
                PlaceOnArm(host, display, split, piece, snapshot, actors);
            else if (piece.Physics.Carried)
            {
                piece.Holder = CharacterId.None;
                display.Physics.ReleaseCarried(piece.Physics, preserveDynamic: true);
                split.SavedFrozen = false;
            }
            if (original.Hidden && piece.Holder.IsNone) continue;
            var frameNumber = (long)stamp.GetValue(display.Renderer)!;
            bool consecutive = motionHistory && piece.MotionFrame >= 0 && piece.MotionFrame + 1 == frameNumber && lastHolder == piece.Holder;
            if (consecutive) piece.RememberPalette();
            var world = FrameWorld(split, piece, out var inverse);
            bool resting = piece.Physics.Frozen && piece.Holder.IsNone;
            if (!resting || !piece.HasRestingPalette)
            {
                AdvancedFragmentScene.FillStockPalette(piece.Physics, split.Rig, split.Birth, split.World, inverse, piece.Palette, piece.ActorMotion);
                if (piece.CarryGrip is { } grip)
                    AdvancedFragmentScene.ApplyStockCarryPalette(piece.Physics, grip, inverse, piece.Palette, piece.ActorMotion);
                foreach (var tag in piece.Tags)
                {
                    var bone = StockTorsoPhysics.Bind(split.Rig, tag.Bone) * piece.Palette[tag.Bone];
                    var offset = tag.Offset;
                    if (Matrix4x4.Decompose(bone, out var scale, out var rotation, out var position))
                    {
                        offset.Translation *= scale.Y;
                        piece.Attachments[tag.Slot] = offset * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);
                    }
                }
            }
            piece.HasRestingPalette = resting;
            long key = !target.IsNone && drawKey.Invoke(host, [target]) is long characterKey ? characterKey : piece.Look.Key;
            if (piece.DrawKey != key) drawn.Remove(piece.DrawKey);
            if (piece.Target != target && !piece.Target.IsNone) StockTorsoProxies.ForgetDrawn(display.World, piece.Target);
            piece.DrawKey = key;
            piece.Target = target;
            if (!target.IsNone)
                StockTorsoProxies.PublishDrawn(display.World, target, piece.Physics.Pose[piece.Physics.Definition.Root].Position);
            if (drawn[key] is not { } state || frozen.GetValue(state) is not true)
            {
                state = Activator.CreateInstance(stateType, nonPublic: true)!;
                frozen.SetValue(state, true);
                posed.SetValue(state, frameNumber);
                seen.SetValue(state, frameNumber);
                drawn[key] = state;
            }
            bool continuous = consecutive && key == oldKey && ReferenceEquals(piece.MotionState, state)
                && (long)seen.GetValue(state)! == frameNumber;
            if (!continuous)
            {
                // Rebase both halves of the velocity calculation when the palette history is unavailable.
                if (motionHistory) piece.RememberPalette();
                stateWorld.SetValue(state, world);
            }
            if (motionHistory && !ReferenceEquals(piece.MotionState, state)) StockTorsoMotion.Mark(state);
            piece.MotionState = state;
            piece.MotionFrame = frameNumber;
            bones.SetValue(state, piece.Palette);
            previous.SetValue(state, motionHistory ? piece.PreviousPalette : piece.Palette);
            attach.SetValue(state, piece.Attachments);
            previousAttach.SetValue(state, motionHistory ? piece.PreviousAttachments : piece.Attachments);
            list.Add(original with
            {
                Key = key, Appearance = piece.Look.Look, World = world, FreezeFrom = key, Bones = null,
                Position = piece.Physics.Pose[piece.Physics.Definition.Root].Position,
            });
        }
    }

    void PlaceOnArm(object host, Display display, Split split, Piece piece, CharacterSnapshot body, IReadOnlyList<CharacterSnapshot> actors)
    {
        if (body.Carry is not { } native || actors.FirstOrDefault(actor => actor.Id == body.CarriedBy) is not { } carrier) return;
        var (destination, orientation) = native.World(carrier.Position, carrier.Yaw);
        var forward = new Vector3(MathF.Sin(carrier.Yaw), 0, MathF.Cos(carrier.Yaw));
        var outside = Vector3.Cross(Vector3.UnitY, forward);
        const float outwardBias = .4f;
        var armNormal = Vector3.Normalize(Vector3.UnitY + outside * outwardBias);
        if (drawKey.Invoke(host, [carrier.Id]) is long carrierKey
            && bonePosition.Invoke(display.Renderer, [carrierKey, "Bip01 L UpperArm"]) is Vector3 shoulder
            && bonePosition.Invoke(display.Renderer, [carrierKey, "Bip01 L Forearm"]) is Vector3 elbow)
        {
            var direction = elbow - shoulder;
            float length = direction.Length();
            if (length > .01f)
            {
                var chest = bonePosition.Invoke(display.Renderer, [carrierKey, "Bip01 Spine1"]) is Vector3 at ? at : carrier.Position;
                var away = shoulder - chest;
                away.Y = 0;
                float side = Vector3.Dot(away, outside);
                if (MathF.Abs(side) > .01f) outside *= MathF.CopySign(1, side);
                var armAxis = direction / length;
                var top = Vector3.UnitY - armAxis * armAxis.Y;
                var edge = outside - armAxis * Vector3.Dot(outside, armAxis);
                var radial = top + edge * outwardBias;
                if (radial.LengthSquared() < .01f) radial = edge;
                if (radial.LengthSquared() > .01f) armNormal = Vector3.Normalize(radial);
                destination = (elbow + shoulder) * .5f + armNormal * Math.Clamp(length * .23f, .45f, 1.15f);
            }
        }
        if (!float.IsFinite(destination.LengthSquared()) || !float.IsFinite(orientation.LengthSquared())) return;
        if (piece.CarryGrip is null)
        {
            var pelvis = Bone("Bip01 Pelvis") ?? Bone("Bip01") ?? Vector3.Zero;
            var neck = Bone("Bip01 Neck") ?? Bone("Bip01 Spine2") ?? Bone("Bip01 Head") ?? pelvis + Vector3.UnitY;
            var leftKnee = Bone("Bip01 L Calf");
            var rightKnee = Bone("Bip01 R Calf");
            var knees = leftKnee is { } left && rightKnee is { } right ? (left + right) * .5f : leftKnee ?? rightKnee;
            var spine = neck - pelvis;
            var upperLegs = knees is { } knee ? pelvis - knee : spine;
            piece.CarryGrip = AdvancedFragmentScene.BuildStockGrip(piece.Physics, split.World, spine, upperLegs,
                rigs.Invoke(display.Renderer, null) as CharacterRigs, split.Template.Appearance, split.Template.Build);

            Vector3? Bone(string name) => split.Rig.Handle(name) is int bone
                ? Vector3.Transform(split.Rig.BindPosition[bone], split.Birth[bone]) : null;
        }
        var grip = piece.CarryGrip;
        bool entering = piece.Holder != body.CarriedBy;
        piece.Holder = body.CarriedBy;
        var drape = grip.Drape(orientation, forward);
        display.Physics.CarryStockGrip(piece.Physics, grip, destination, drape, armNormal, entering);
        if (trace && entering)
        {
            var axis = Vector3.Transform(grip.LengthAxis, drape);
            Console.WriteLine(FormattableString.Invariant($"Brutality stock carry support side={piece.Look.Side} clip={grip.CarrySkin is not null} axisY={axis.Y:F3} axisForward={Vector3.Dot(axis, forward):F3} supportUp={armNormal.Y:F3}"));
        }
        split.SavedFrozen = false;
    }

    static bool Carried(IReadOnlyList<CharacterSnapshot> actors, World world, CharacterId owner)
    {
        var lower = StockTorsoProxies.For(world)?.Frame.TryProxy(owner, out var proxy) == true ? proxy : CharacterId.None;
        return actors.Any(actor => (actor.Id == owner || actor.Id == lower) && !actor.CarriedBy.IsNone);
    }

    bool RetireSettled(Display display, CharacterDrawList list, IDictionary drawn, IReadOnlyList<CharacterSnapshot> actors, double time)
    {
        var eye = buildEye.GetValue(display.Renderer) is Vector3 camera ? camera : Vector3.Zero;
        Split? selected = null;
        float farthest = -1;
        bool selectedFrozen = false;
        foreach (var split in display.Active)
        {
            if (split.Pieces.Any(piece => !piece.Holder.IsNone) || Carried(actors, display.World, split.Owner)) continue;
            bool frozen = split.Pieces.All(piece => piece.Physics.Frozen);
            if (!frozen && (selectedFrozen || split.Pieces.Any(piece => !display.Physics.RestingNearGround(piece.Physics, display.Ground)))) continue;
            float distance = Vector3.DistanceSquared(eye, split.Pieces[0].Physics.Pose[split.Pieces[0].Physics.Definition.Root].Position);
            if (frozen == selectedFrozen && distance <= farthest) continue;
            selected = split;
            farthest = distance;
            selectedFrozen = frozen;
        }
        if (selected is not { } old) return false;
        foreach (var piece in old.Pieces) display.Physics.Freeze(piece.Physics);
        old.NextSave = 0;
        PublishSavePose(display, old, time);
        if (StockTorsoSave.For(display.World, old.Owner) is null) return false;
        foreach (var piece in old.Pieces)
            list.Items.RemoveAll(item => item.Key == piece.DrawKey && ReferenceEquals(item.Appearance, piece.Look.Look));
        Release(display, old, drawn);
        display.Active.Remove(old);
        display.Attempted.Remove(old.Owner);
        display.DormantCuts[old.Owner] = new(old.OwnerKey, old.Cost);
        if (trace) Console.WriteLine($"Brutality stock torso parked owner={old.Owner} released={old.Cost.Meshes} meshes");
        return true;
    }

    static void Release(Display display, Split split, IDictionary drawn)
    {
        foreach (var piece in split.Pieces)
        {
            drawn.Remove(piece.DrawKey);
            if (!piece.Target.IsNone) StockTorsoProxies.ForgetDrawn(display.World, piece.Target);
            display.Physics.Remove(piece.Physics);
            StockHeadMeshes.Retire(piece.Look.Look);
        }
    }

    void Reset(object host)
    {
        if (!displays.TryGetValue(host, out var display)) return;
        foreach (var pending in display.Pending.ToArray())
        {
            Abandon(display, pending);
            if (display.Pending.Contains(pending))
                _ = pending.Work.ContinueWith(task => { _ = task.Exception; pending.Cancel.Dispose(); }, TaskScheduler.Default);
        }
        if (states.GetValue(display.Renderer) is IDictionary drawn)
            foreach (var split in display.Active) Release(display, split, drawn);
        display.Physics.Dispose();
        StockPartLabels.Publish(display.World, "torso", []);
        displays.Remove(host);
    }

    void Abandon(Display display, Pending pending, string? retryReason = null)
    {
        if (pending.Abandoned) return;
        var victim = pending.Request.Victim;
        int attempts = display.Retries.GetValueOrDefault(victim).Count;
        if (retryReason is not null && attempts < MaximumRetries)
        {
            display.Attempted.Remove(victim);
            display.Retries[victim] = new(attempts + 1, Stopwatch.GetTimestamp());
            if (trace) Console.WriteLine($"Brutality stock torso retry owner={victim} attempt={attempts + 1}: {retryReason}");
        }
        else
        {
            display.Retries.Remove(victim);
            if (pending.Dormant) display.DormantCuts.Remove(victim);
            if (trace && retryReason is not null) Console.WriteLine($"Brutality stock torso fallback owner={victim}: {retryReason}");
        }
        pending.Abandoned = true;
        pending.Cancel.Cancel();
        foreach (var fragment in pending.Looks) StockHeadMeshes.Retire(fragment.Look);
        pending.Looks.Clear();
        if (pending.Work.IsCompleted) Finish(display, pending);
    }

    static void Finish(Display display, Pending pending)
    {
        _ = pending.Work.Exception;
        pending.Cancel.Dispose();
        display.Pending.Remove(pending);
    }
}
