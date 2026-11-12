using Meitou.Game.Host;
using Meitou.Game.Rendering.Frame;
using System.Collections;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Brutality.Effects;
using Meitou.Brutality.Ui;
using Meitou.Content;
using Meitou.Data.Characters;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.Brutality;

internal sealed class StockWoundPresentation
{
    const int MaximumVariants = 16, MaximumOrdinaryVariants = 12;
    static readonly TimeSpan PunchUploadGrace = TimeSpan.FromSeconds(5);
    static StockWoundPresentation? installed;
    readonly ConditionalWeakTable<object, Display> displays = new();
    readonly ConditionalWeakTable<StockWoundRules.View, VisualStamp> stamps = new();
    readonly ModContext context;
    readonly string? installRoot = GameInstall.Locate()?.Root;
    readonly FieldInfo session, options, blood, assets, parts, states, bones;
    readonly MethodInfo renderer, rigs, drawKey, lastDrawn;
    readonly bool trace = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_TRACE_HITS") == "1";

    const long FallingHeadKey = 1L << 54;
    sealed record VisualStamp(ulong Body, ulong Bruises, SevereHeadWoundState Head, ChestHoleState? Hole, HeadCutState? Attached);
    readonly record struct Key(CharacterAppearance Original, VisualStamp Stamp, ulong Garments);
    readonly record struct ActorView(CharacterId Owner, StockWoundRules.View Wound, GarmentDamageActor? Garments);
    readonly record struct ParkedVariant(Key Key, StockHeadMeshes.Cost Cost, long RetryAt);
    sealed record Variant(CharacterAppearance Look, long StagingKey, CharacterAppearance? Detached, long DetachedStagingKey,
        StockWoundBuild.WoundSpill? Spill, Vertex[]? PunchExit, StockWoundBuild.HeadBounds? HeadBounds, Vertex[]? PunchEntry,
        StockHeadMeshes.Cost Cost, long Owner)
    {
        public readonly long UploadQueuedAt = Stopwatch.GetTimestamp();
        public bool Ready;
    }
    sealed record Pending(long Owner, Key Key, CharacterAppearance? LooseHead, Task<StockWoundBuild.Geometry?> Work,
        CancellationTokenSource Cancel, bool Restoring, int Priority)
    {
        public readonly Stopwatch Timer = Stopwatch.StartNew();
        public StockHeadMeshes.Cost? Cost;
    }
    sealed class Display(World world, object renderer)
    {
        public readonly World World = world;
        public readonly object Renderer = renderer;
        public readonly Dictionary<Key, Variant?> Variants = [];
        public readonly Dictionary<long, (Key Key, Variant Variant)> Shown = [];
        public readonly Dictionary<long, ParkedVariant> Parked = [];
        public readonly Dictionary<long, ActorView> ByKey = [];
        public readonly HashSet<long> StartedDetached = [];
        public readonly List<BrutalityPartLabel> HeadLabels = [];
        public StockWoundRules.Frame? Frame;
        public GarmentDamageFrame? GarmentFrame;
        public Pending? Pending;
        public bool NeedsHeadRoom;
        public long Serial;
    }

    StockWoundPresentation(ModContext context, Type host, Type chars)
    {
        this.context = context;
        session = Field(host, "Session");
        options = AccessTools.GetDeclaredFields(host).Single(f => f.FieldType.Name == "WorldOptions");
        blood = Field(options.FieldType, "MeitouBlood");
        assets = Field(chars, "_assets");
        parts = Field(assets.FieldType.GetGenericArguments()[1], "Parts");
        states = Field(chars, "_states");
        bones = Field(states.FieldType.GetGenericArguments()[1], "Bones");
        renderer = AccessTools.PropertyGetter(host, "ActiveCharacters") ?? throw new MissingMemberException("GameHost.ActiveCharacters");
        rigs = AccessTools.PropertyGetter(chars, "Rigs") ?? throw new MissingMemberException("CharacterRenderer.Rigs");
        drawKey = Method(host, "DrawKey", typeof(CharacterId));
        lastDrawn = Method(chars, "LastDrawn", typeof(long), typeof(string));
    }

    internal static bool Install(ModContext context)
    {
        if (!StockHeadMeshes.Install(context)) return false;
        var host = AccessTools.TypeByName("Meitou.Game.GameHost") ?? throw new TypeLoadException("GameHost");
        var chars = AccessTools.TypeByName("Meitou.Game.Rendering.Characters.CharacterRenderer") ?? throw new TypeLoadException("CharacterRenderer");
        var hooks = new StockWoundPresentation(context, host, chars);
        StockNativeHeadFall.Install(context, host, chars);
        // The weapon variant changes only its rigid part; run afterward so it can be preserved in this look.
        context.Harmony.Patch(Method(host, "FillDrawListCore", typeof(CharacterDrawList)),
            postfix: new HarmonyMethod(typeof(StockWoundPresentation), nameof(DrawListed)) { priority = Priority.Last });
        context.Harmony.Patch(Method(host, "Run"),
            finalizer: new HarmonyMethod(typeof(StockWoundPresentation), nameof(HostFinished)));
        installed = hooks;
        return true;
    }

    static FieldInfo Field(Type type, string name) =>
        AccessTools.Field(type, name) ?? throw new MissingFieldException(type.Name, name);
    static MethodInfo Method(Type type, string name, params Type[] parameters) =>
        AccessTools.Method(type, name, parameters) ?? throw new MissingMethodException(type.Name, name);
    static void DrawListed(object __instance, CharacterDrawList list) => installed?.Draw(__instance, list);
    static void HostFinished(object __instance) => installed?.Reset(__instance);

    internal static void RequestHeadRoom(object host)
    {
        if (installed?.displays.TryGetValue(host, out var display) == true) display.NeedsHeadRoom = true;
    }

    void Draw(object host, CharacterDrawList list)
    {
        if (session.GetValue(host) is not WorldSession shown) return;
        displays.TryGetValue(host, out var display);
        if (display is not null && !ReferenceEquals(display.World, shown.World))
        {
            Reset(host);
            display = null;
        }
        display?.HeadLabels.Clear();
        if (options.GetValue(host) is not { } graphics || blood.GetValue(graphics) is not true
            || renderer.Invoke(host, null) is not { } chars) return;
        var frame = StockWoundRules.FrameAt(shown.World, shown.ShownTick);
        var garmentFrame = StockGarmentDamage.FrameAt(shown.World, shown.ShownTick);
        if (display is null && frame is not { Actors.Count: > 0 } && garmentFrame is not { Actors.Count: > 0 }) return;
        if (display is not null && !ReferenceEquals(display.Renderer, chars))
        {
            Reset(host);
            display = null;
        }
        display ??= displays.GetValue(host, _ => new(shown.World, chars));
        StockPartLabels.Publish(shown.World, "loose-heads", display.HeadLabels);
        if (!ReferenceEquals(display.Frame, frame) || !ReferenceEquals(display.GarmentFrame, garmentFrame))
        {
            display.Frame = frame;
            display.GarmentFrame = garmentFrame;
            if (trace && frame is not null && frame.Actors.Values.Any(wound => wound.AttachedHead is not null))
                Console.WriteLine($"Brutality stock head frame tick={shown.ShownTick} actors={frame.Actors.Count} variants={display.Variants.Count}");
            display.ByKey.Clear();
            if (frame is not null)
                foreach (var (id, wound) in frame.Actors)
                    if (drawKey.Invoke(host, [id]) is long key)
                        display.ByKey.Add(key, new(id, wound, garmentFrame?.Actors.GetValueOrDefault(id)));
            if (garmentFrame is not null)
                foreach (var (id, worn) in garmentFrame.Actors)
                    if (drawKey.Invoke(host, [id]) is long key)
                        display.ByKey.TryAdd(key, new(id, StockWoundRules.EmptyView, worn));
            foreach (long key in display.Parked.Keys.Where(key => !display.ByKey.ContainsKey(key)).ToArray())
                display.Parked.Remove(key);
            foreach (long key in display.Shown.Keys.Where(key => !display.ByKey.ContainsKey(key)).ToArray())
            {
                display.Shown.Remove(key);
                display.StartedDetached.Remove(key);
            }
        }
        if (assets.GetValue(chars) is not IDictionary loaded) return;
        Finish(host, display, list);
        bool headRoom = display.NeedsHeadRoom;
        if (headRoom)
        {
            display.NeedsHeadRoom = false;
            MakeRoom(display, list, long.MinValue, priority: 1, pressure: true);
        }
        if (display.Pending is null) PrepareNext(host, display, list, chars, onlyPunches: headRoom);
        int count = list.Items.Count;
        for (int i = 0; i < count; i++)
        {
            var item = list.Items[i];
            if (!TryLook(display, item, out var actor, out var key)) continue;
            var wound = actor.Wound;
            var stamp = key.Stamp;
            int headIndex = stamp.Attached is null ? -1 : list.Items.FindIndex(entry => entry.Key == (item.Key ^ FallingHeadKey));
            if (display.Variants.TryGetValue(key, out var variant))
            {
                if (variant is not null && IsReady(display, key, variant, loaded, stamp.Attached is not null))
                {
                    if (trace && stamp.Hole is not null && variant.PunchExit is { Length: > 0 }
                        && (!display.Shown.TryGetValue(item.Key, out var previous) || previous.Key != key))
                        Console.WriteLine($"Brutality stock punch-through shown owner={actor.Owner} tick={shown.ShownTick} born={wound.PunchTick}");
                    display.Shown[item.Key] = (key, variant);
                    list.Items[i] = item with { Appearance = variant.Look };
                    if (variant.PunchExit is { Length: > 0 } exit && wound.PunchTick is { } birth && stamp.Hole is { } hole
                        && MasterImpactFeed.AcceptsPunch(host, actor.Owner, birth, shown.ShownTick, item.Appearance)
                        && WoundContact(chars, item.Key, exit) is { } punch)
                    {
                        var entry = variant.PunchEntry;
                        var keyForPose = item.Key;
                        MasterImpactFeed.BeginPunch(host, actor.Owner, birth, item.Appearance, hole.Region, punch,
                            half => (half == 0 ? exit : entry) is { Length: > 0 } rim ? WoundContact(chars, keyForPose, rim) : null,
                            (shown.ShownTick + shown.SimulationAlpha) * shown.World.TickSeconds);
                    }
                    if (variant.Spill is { } spill && item.Rotation is null
                        && MasterImpactFeed.AcceptsWound(host, actor.Owner)
                        && !StockTorsoRules.TryGet(shown.World, actor.Owner, shown.ShownTick, out _)
                        && WoundContact(chars, item.Key, spill.Anchor) is { } contact)
                        MasterImpactFeed.BeginWound(host, actor.Owner, item.Appearance, spill.Rig, contact,
                            (shown.ShownTick + shown.SimulationAlpha) * shown.World.TickSeconds);
                    continue;
                }
                if (variant is not null && display.Variants[key] is not null)
                {
                    if (stamp.Hole is not null && Stopwatch.GetElapsedTime(variant.UploadQueuedAt) >= PunchUploadGrace)
                    {
                        display.Variants.Remove(key);
                        Retire(variant);
                        Park(display, item.Key, key, variant.Cost);
                        if (trace) Console.WriteLine($"Brutality stock punch-through upload timed out owner={actor.Owner} tick={shown.ShownTick}");
                    }
                    else
                    {
                        list.Add(item with { Key = variant.StagingKey, Appearance = variant.Look, Hidden = true });
                        if (variant.Detached is { } cut)
                        {
                            var source = headIndex >= 0 ? list.Items[headIndex] : item;
                            list.Add(source with { Key = variant.DetachedStagingKey, Appearance = cut, Hidden = true, FreezeFrom = null });
                        }
                    }
                }
            }
            // The preparation pass has already selected the highest-priority new look.
            if (display.Shown.TryGetValue(item.Key, out var prior) && ReferenceEquals(prior.Key.Original, key.Original)
                && prior.Key.Garments == key.Garments)
                list.Items[i] = item with { Appearance = prior.Variant.Look };
        }
        // Replace the falling head's skin and use the same contact physics as the other head fragments.
        foreach (var (owner, shownLook) in display.Shown)
        {
            if (shownLook.Variant.Detached is not { } cut || shownLook.Key.Stamp.Attached is not { } shape
                || shownLook.Variant.HeadBounds is not { } bounds || !display.ByKey.TryGetValue(owner, out var actor)) continue;
            long headKey = owner ^ FallingHeadKey;
            string part = shape.Mode == HeadCutMode.AttachedSplit ? "Head half" : "Severed head";
            int index = list.Items.FindIndex(entry => entry.Key == headKey);
            if (index < 0)
            {
                int bodyIndex = list.Items.FindIndex(entry => entry.Key == owner && !entry.Hidden);
                if (bodyIndex >= 0 && StockHeadFragments.RestoredLoose(host, shown, chars, actor.Owner, shape,
                        list.Items[bodyIndex] with { Appearance = shownLook.Key.Original }, cut, bounds) is { } loose)
                {
                    list.Add(loose);
                    if (!loose.Hidden) display.HeadLabels.Add(new(actor.Owner, 310, part, loose.Position));
                }
                continue;
            }
            if (!display.StartedDetached.Contains(owner) && loaded.Contains(cut)
                && StockNativeHeadFall.StartReplacement(host, headKey, shown.ShownTick + shown.SimulationAlpha))
                display.StartedDetached.Add(owner);
            var head = list.Items[index] with { Appearance = cut };
            if (display.StartedDetached.Contains(owner))
                head = StockHeadFragments.StartNative(host, shown, chars, actor.Owner, shape, head, bounds);
            list.Items[index] = head;
            if (!head.Hidden) display.HeadLabels.Add(new(actor.Owner, 310, part, head.Position));
        }
    }

    bool TryLook(Display display, CharacterInstance item, out ActorView actor, out Key key)
    {
        actor = default;
        key = default;
        if (item.Hidden || item.FreezeFrom is not null || !display.ByKey.TryGetValue(item.Key, out actor)) return false;
        var stamp = stamps.GetValue(actor.Wound, static value => new(value.Body.StateKey(), value.Bruises?.StateKey() ?? 0,
            value.Head with { HeavyHits = 0 }, value.Hole, value.AttachedHead));
        if (stamp.Attached is not null && !item.Appearance.Headless) return false;
        // Build major cuts on the limb pass's skin so the existing stump remains attached.
        if (StockHeadMeshes.IsGeneratedBody(item.Appearance)
            && (stamp.Attached is null && stamp.Hole is null && actor.Garments is null
                || StockTorsoRules.TryGetSaved(display.World, actor.Owner, out _))) return false;
        key = new(item.Appearance, stamp, actor.Garments?.Revision ?? 0);
        return true;
    }

    void PrepareNext(object host, Display display, CharacterDrawList list, object chars, bool onlyPunches)
    {
        // A new tunnel or severe head wound takes the worker before another ordinary cut.
        (CharacterInstance Item, ActorView Actor, Key Key, ParkedVariant? Parked, int Priority)? selected = null;
        int best = -1;
        long now = Stopwatch.GetTimestamp();
        foreach (var item in list.Items)
        {
            if (!TryLook(display, item, out var actor, out var key) || display.Variants.ContainsKey(key)
                || onlyPunches && key.Stamp.Hole is null) continue;
            int priority = PriorityOf(key.Stamp);
            if (display.Shown.TryGetValue(item.Key, out var shown) && SameMajorWound(shown.Key.Stamp, key.Stamp)) priority = 0;
            ParkedVariant? restore = null;
            if (display.Parked.TryGetValue(item.Key, out var parked) && parked.Key == key)
            {
                if (key.Stamp.Hole is null || parked.RetryAt > now) continue;
                restore = parked;
            }
            // A new hole takes precedence over rebuilding an older one after a cache eviction.
            int rank = priority == 2 && restore is null ? 3 : priority;
            if (rank <= best) continue;
            selected = (item, actor, key, restore, priority);
            best = rank;
            if (best == 3) break;
        }
        if (selected is not { } next) return;

        bool foreground = next.Key.Stamp.Hole is not null;
        var cost = next.Parked?.Cost ?? new StockHeadMeshes.Cost(1, 1);
        var capacity = StockHeadMeshes.TorsoCapacity(cost, foreground: foreground);
        if (next.Parked is { } retry)
        {
            // Restore from spare capacity or reclaim an offscreen look. Repeatedly evicting visible
            // wounds to resurrect another parked one would make the holes flicker under pressure.
            if (capacity == StockHeadMeshes.Capacity.LiveLimit || display.Variants.Count >= MaximumVariants)
                MakeRoom(display, list, next.Item.Key, next.Priority, pressure: capacity == StockHeadMeshes.Capacity.LiveLimit,
                    foreground: foreground, restoring: true);
            if (display.Variants.Count >= MaximumVariants || StockHeadMeshes.TorsoCapacity(cost, foreground: foreground) != StockHeadMeshes.Capacity.Available)
            {
                display.Parked[next.Item.Key] = retry with { RetryAt = now + Stopwatch.Frequency };
                return;
            }
        }
        else if (!MakeRoom(display, list, next.Item.Key, next.Priority, foreground: foreground)
            || StockHeadMeshes.TorsoCapacity(cost, foreground: foreground) != StockHeadMeshes.Capacity.Available)
        {
            if (next.Priority > 0 && capacity == StockHeadMeshes.Capacity.LiveLimit) StockTorsoFragments.RequestRoom(host);
            return;
        }
        if (rigs.Invoke(chars, null) is not CharacterRigs stockRigs) return;
        var looseHead = next.Key.Stamp.Attached is null ? null
            : list.Items.Find(entry => entry.Key == (next.Item.Key ^ FallingHeadKey)).Appearance
                ?? StockHeadFragments.WithHeadEquipment(context, next.Item.Appearance, installRoot).SeveredHead();
        var cancel = new CancellationTokenSource();
        var token = cancel.Token;
        var work = Task.Run(() => StockWoundBuild.Build(context.Folder, stockRigs, next.Key.Original,
            next.Actor.Wound, looseHead, token, next.Actor.Garments), token);
        display.Pending = new(next.Item.Key, next.Key, looseHead, work, cancel, next.Parked is not null, next.Priority);
        if (trace && next.Key.Stamp.Attached is not null)
            Console.WriteLine($"Brutality stock attached head building key={next.Item.Key} detached={looseHead is not null}");
        if (trace && next.Key.Stamp.Hole is not null)
            Console.WriteLine($"Brutality stock punch-through building actor={next.Item.Key} cached={display.Variants.Count} restored={next.Parked is not null}");
    }

    static bool SameMajorWound(VisualStamp previous, VisualStamp next) =>
        previous.Hole == next.Hole && previous.Head == next.Head && previous.Attached == next.Attached;

    static int PriorityOf(VisualStamp stamp) => stamp.Hole is not null ? 2
        : stamp.Attached is not null || stamp.Head.Kind != SevereHeadWoundKind.None ? 1 : 0;

    bool MakeRoom(Display display, CharacterDrawList list, long owner, int priority, bool pressure = false,
        bool foreground = false, bool restoring = false)
    {
        bool reserve = priority == 0 && display.Variants.Keys.Count(key => PriorityOf(key.Stamp) == 0) >= MaximumOrdinaryVariants;
        if (!pressure && !reserve && display.Variants.Count < MaximumVariants
            && StockHeadMeshes.TorsoCapacity(new(1, 1), foreground: foreground) == StockHeadMeshes.Capacity.Available) return true;

        var visible = list.Items.Where(item => !item.Hidden).Select(item => item.Key).ToHashSet();
        var protectedKeys = display.Shown.Where(pair => visible.Contains(pair.Key) || visible.Contains(pair.Key ^ FallingHeadKey))
            .Select(pair => pair.Value.Key).ToHashSet();
        Key? eviction = null;
        int best = int.MaxValue;
        foreach (var (key, variant) in display.Variants)
        {
            int rank = PriorityOf(key.Stamp);
            if (reserve && rank != 0 || pressure && variant is null) continue;
            // A visible punch must survive until its hidden staging item can upload. An obsolete,
            // distant, or expired upload can still make room for a different character's cut.
            if (rank == 2 && variant is { Ready: false } && visible.Contains(variant.Owner)
                && Stopwatch.GetElapsedTime(variant.UploadQueuedAt) < PunchUploadGrace
                && list.Items.Any(item => item.Key == variant.Owner && TryLook(display, item, out _, out var current) && current == key)) continue;
            if (protectedKeys.Contains(key))
            {
                bool obsolete = display.Shown.TryGetValue(owner, out var shown) && shown.Key == key;
                if (!obsolete && (restoring || rank > priority)) continue;
                // A major wound may displace an ordinary one even while the crowd is visible.
                rank += obsolete ? 4 : 8;
            }
            if (variant is null) rank = -1;
            if (rank >= best) continue;
            eviction = key;
            best = rank;
        }
        if (eviction is not { } old) return false;
        if (display.Variants.Remove(old, out var retired) && retired is not null)
        {
            Retire(retired);
            if (!retired.Ready && old.Stamp.Hole is not null && display.ByKey.TryGetValue(retired.Owner, out var actor))
            {
                Park(display, retired.Owner, old, retired.Cost);
                if (trace) Console.WriteLine($"Brutality stock punch-through staging retired owner={actor.Owner} tick={display.Frame?.Tick} priority={priority}");
            }
        }
        foreach (long actor in display.Shown.Where(pair => pair.Value.Key == old).Select(pair => pair.Key).ToArray())
        {
            if (retired is not null) Park(display, actor, old, retired.Cost);
            if (trace && visible.Contains(actor) && old.Stamp.Hole is not null && display.ByKey.TryGetValue(actor, out var wound))
                Console.WriteLine($"Brutality stock punch-through retired owner={wound.Owner} tick={display.Frame?.Tick} priority={priority}");
            display.Shown.Remove(actor);
            display.StartedDetached.Remove(actor);
        }
        return true;
    }

    static void Park(Display display, long owner, Key key, StockHeadMeshes.Cost cost) =>
        display.Parked[owner] = new(key, cost, Stopwatch.GetTimestamp() + Stopwatch.Frequency);

    CutFluidContact? WoundContact(object chars, long key, Vertex[] anchor)
    {
        if (lastDrawn.Invoke(chars, [key, "Bip01 Pelvis"]) is not ValueTuple<Matrix4x4, CharacterAppearance, Vector3> drawn
            || states.GetValue(chars) is not IDictionary instances || instances[key] is not { } state
            || bones.GetValue(state) is not Matrix4x4[] palette) return null;
        return StockWoundAnchor.Pose(anchor, palette, drawn.Item1);
    }

    bool IsReady(Display display, Key key, Variant variant, IDictionary loaded, bool headListed)
    {
        if (variant.Ready) return true;
        var detached = headListed ? variant.Detached : null;
        if (!loaded.Contains(variant.Look) || detached is not null && !loaded.Contains(detached)) return false;
        if (Complete(variant.Look) && (detached is null || Complete(detached)))
        {
            variant.Ready = true;
            if (trace) Console.WriteLine($"Brutality stock wound mesh ready head={key.Stamp.Head.Kind} body={key.Stamp.Body != 0} punch={variant.PunchExit is { Length: > 0 }} attached={key.Stamp.Attached is not null}");
            return true;
        }
        if (trace) Console.WriteLine("Brutality stock wound upload incomplete");
        Retire(variant);
        display.Variants[key] = null;
        return false;

        bool Complete(CharacterAppearance look) => loaded[look] is { } asset && parts.GetValue(asset) is ICollection actual
            && actual.Count >= look.Parts.Count + 1;
    }

    void Finish(object host, Display display, CharacterDrawList list)
    {
        if (display.Pending is not { Work.IsCompleted: true } pending) return;
        bool foreground = pending.Key.Stamp.Hole is not null;
        if (foreground && !list.Items.Any(item => item.Key == pending.Owner && !item.Hidden))
        {
            display.Pending = null;
            pending.Cancel.Dispose();
            _ = pending.Work.Exception;
            return;
        }
        if (pending.Work.IsCompletedSuccessfully && pending.Work.Result is { } prepared)
        {
            var cost = pending.Cost ??= StockHeadMeshes.WoundCost(prepared);
            var capacity = cost.Meshes == 0 && prepared.VestTears is not null ? StockHeadMeshes.Capacity.Available
                : StockHeadMeshes.TorsoCapacity(cost, foreground: foreground);
            if (capacity is StockHeadMeshes.Capacity.LiveLimit or StockHeadMeshes.Capacity.PendingRetirement)
            {
                if (pending.Timer.Elapsed.TotalSeconds < 5)
                {
                    if (capacity == StockHeadMeshes.Capacity.LiveLimit
                        && !MakeRoom(display, list, pending.Owner, pending.Priority, pressure: true, foreground: foreground, restoring: pending.Restoring)
                        && !pending.Restoring && pending.Priority > 0)
                        StockTorsoFragments.RequestRoom(host);
                    return;
                }
                Park(display, pending.Owner, pending.Key, cost);
                display.Pending = null;
                pending.Cancel.Dispose();
                return;
            }
        }
        display.Pending = null;
        pending.Cancel.Dispose();
        Variant? variant = null;
        if (pending.Work.IsCompletedSuccessfully && pending.Work.Result is { } geometry)
        {
            bool attached = pending.Key.Stamp.Attached is not null;
            var body = StockHeadMeshes.CreateWounds(pending.Key.Original, geometry.Body, geometry.Tissue, geometry.Equipment,
                showHead: attached, foreground: foreground, materialOnly: geometry.VestTears is not null);
            if (body is not null)
            {
                if (geometry.VestTears is { } vest) StockVestTears.Mark(body, vest);
                CharacterAppearance? head = geometry.Detached is { } cut && pending.LooseHead is { } loose
                    ? StockHeadMeshes.CreateWounds(loose, cut.Body, cut.Tissue, cut.Equipment, foreground: foreground) : null;
                if (geometry.Detached is null || head is not null)
                    variant = new(body, NextKey(display), head, head is null ? 0 : NextKey(display), geometry.Spill,
                        geometry.PunchExit, geometry.Detached?.Bounds, geometry.PunchEntry, pending.Cost!.Value, pending.Owner);
                else StockHeadMeshes.Retire(body);
            }
        }
        if (trace && pending.Work.Exception is { } fault)
            Console.WriteLine($"Brutality stock wound geometry unavailable: {fault.GetBaseException().Message}");
        else _ = pending.Work.Exception;
        if (trace && pending.Key.Stamp.Attached is not null && variant is null)
            Console.WriteLine($"Brutality stock attached head build produced no look geometry={pending.Work.IsCompletedSuccessfully && pending.Work.Result is not null}");
        if (variant is not null) display.Parked.Remove(pending.Owner);
        display.Variants.Add(pending.Key, variant);
    }

    static long NextKey(Display display) => long.MinValue | (1L << 52) | ++display.Serial;

    static void Retire(Variant variant)
    {
        StockHeadMeshes.Retire(variant.Look);
        if (variant.Detached is { } detached) StockHeadMeshes.Retire(detached);
    }

    void Reset(object host)
    {
        if (!displays.TryGetValue(host, out var display)) return;
        display.HeadLabels.Clear();
        displays.Remove(host);
        foreach (var variant in display.Variants.Values)
            if (variant is not null) Retire(variant);
        if (display.Pending is not { } pending) return;
        pending.Cancel.Cancel();
        _ = pending.Work.ContinueWith(task =>
        {
            _ = task.Exception;
            pending.Cancel.Dispose();
        }, TaskScheduler.Default);
    }
}
