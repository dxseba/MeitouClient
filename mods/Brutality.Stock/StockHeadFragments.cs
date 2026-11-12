using Meitou.Game.Host;
using Meitou.Game.Rendering.Frame;
using System.Collections;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Brutality.Rendering;
using Meitou.Brutality.Ui;
using Meitou.Content;
using Meitou.Data.Characters;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Ragdolls;
using Meitou.Simulation.Saving;

namespace Meitou.Brutality;

/// <summary>Replaces a native falling head once its cut meshes are ready, keeping its current placement and frozen palette.</summary>
internal sealed class StockHeadFragments
{
    static StockHeadFragments? installed;
    readonly ConditionalWeakTable<object, Display> displays = new();
    readonly ModContext context;
    readonly string? installRoot = GameInstall.Locate()?.Root;
    readonly FieldInfo decapitation, heads, session, states, bones, assets, assetParts, nativeKey;
    readonly Type characterState;
    readonly FieldInfo rendererStamp, previousBones, posed, seen, frozen;
    readonly MethodInfo renderer, rigs, lastDrawn, drawKey;
    readonly bool trace = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_TRACE_HITS") == "1";

    sealed class Display(World world, int epoch)
    {
        public readonly World World = world;
        public readonly int Epoch = epoch;
        public readonly StockHeadShardPhysics Physics = new(new WalkabilityEnvironment(world.Walkability));
        public readonly List<Fragment> Active = [];
        public readonly List<BrutalityPartLabel> Labels = [];
        public readonly Dictionary<long, NativeMotion> Native = [];
        public long NativeFrame;
        public readonly Dictionary<CharacterId, Split> Splits = [];
        public readonly HashSet<CharacterId> RestoreTried = [];
        public readonly Dictionary<CharacterId, (int Count, long NextTick)> Attempts = [];
        public Pending? Pending;
        public long Sequence;
        public int Group;
    }

    sealed class NativeMotion(CharacterId owner, HeadCutState cut, Matrix4x4[] palette, ulong blood, bool synthetic = false)
    {
        public readonly CharacterId Owner = owner;
        public readonly HeadCutState Cut = cut;
        public readonly Matrix4x4[] Palette = palette;
        public readonly ulong Blood = blood;
        public readonly bool Synthetic = synthetic;
        public long Seen;
        public long NextCapture;
        public StockHeadSplitSave.Pose? Settled;
        public Vector3? PoolFloor;
    }

    sealed record Fragment(long Key, CharacterId Owner, PreparedHeadBurstShard Piece, CharacterAppearance Look, ulong Blood)
    {
        public StockHeadSplitSave.Pose? Settled;
        public Vector3? PoolFloor;
    }
    sealed class Split(StockHeadRules.CutRequest request, Matrix4x4[] palette, Fragment first, Fragment second, double started)
    {
        public readonly StockHeadRules.CutRequest Request = request;
        public readonly Matrix4x4[] Palette = palette;
        public readonly Fragment First = first, Second = second;
        public readonly double Started = started;
        public double CaptureNext;
    }
    sealed class Pending(long native, CharacterId owner, StockHeadRules.CutRequest request, Matrix4x4[] palette, Vector2 travel,
        Task<StockHeadBuild.Cut?> worker, CancellationTokenSource cancel, StockHeadSplitSave.Layout? restoring = null,
        CharacterInstance? fallback = null)
    {
        public readonly long Native = native;
        public readonly CharacterId Owner = owner;
        public readonly StockHeadRules.CutRequest Request = request;
        public readonly Matrix4x4[] Palette = palette;
        public readonly Vector2 Travel = travel;
        public readonly Task<StockHeadBuild.Cut?> Worker = worker;
        public readonly CancellationTokenSource Cancel = cancel;
        public readonly StockHeadSplitSave.Layout? Restoring = restoring;
        public readonly CharacterInstance? Fallback = fallback;
        public readonly Stopwatch SinceCut = Stopwatch.StartNew();
        public StockHeadMeshes.Cost? Cost;
        public StockHeadMeshes.Capacity? ReportedCapacity;
        public readonly List<Fragment> Staged = [];
        public bool Abandoned;
    }

    StockHeadFragments(ModContext context, Type host, Type characterRenderer)
    {
        this.context = context;
        decapitation = StockHostParts.Decapitation;
        heads = Field(decapitation.FieldType, "_heads");
        session = Field(host, "Session");
        renderer = AccessTools.PropertyGetter(host, "ActiveCharacters") ?? throw new MissingMethodException(host.Name, "ActiveCharacters");
        drawKey = Method(host, "DrawKey", typeof(CharacterId));
        var head = AccessTools.Inner(decapitation.FieldType, "FallingHead") ?? throw new MissingMemberException("Decapitation", "FallingHead");
        nativeKey = Field(head, "Key");
        states = Field(characterRenderer, "_states");
        assets = Field(characterRenderer, "_assets");
        assetParts = Field(assets.FieldType.GetGenericArguments()[1], "Parts");
        characterState = AccessTools.Inner(characterRenderer, "CharState") ?? throw new MissingMemberException(characterRenderer.Name, "CharState");
        bones = Field(characterState, "Bones");
        rendererStamp = Field(characterRenderer, "_updateStamp");
        previousBones = Field(characterState, "Previous");
        posed = Field(characterState, "Posed");
        seen = Field(characterState, "Seen");
        frozen = Field(characterState, "Frozen");
        rigs = AccessTools.PropertyGetter(characterRenderer, "Rigs") ?? throw new MissingMethodException(characterRenderer.Name, "Rigs");
        lastDrawn = Method(characterRenderer, "LastDrawn", typeof(long), typeof(string));
    }

    internal static bool Install(ModContext context)
    {
        if (!StockHeadMeshes.Install(context)) return false;
        try
        {
            var host = AccessTools.TypeByName("Meitou.Game.GameHost") ?? throw new TypeLoadException("GameHost");
            var characters = AccessTools.TypeByName("Meitou.Game.Rendering.Characters.CharacterRenderer") ?? throw new TypeLoadException("CharacterRenderer");
            var instance = new StockHeadFragments(context, host, characters);
            context.Harmony.Patch(Method(StockHostParts.Decapitation.FieldType, "CutHead", typeof(CharacterSnapshot), typeof(CharacterAppearance)),
                postfix: new HarmonyMethod(typeof(StockHeadFragments), nameof(HeadCut)));
            context.Harmony.Patch(Method(StockHostParts.Decapitation.FieldType, "AddHeads", typeof(CharacterDrawList)),
                postfix: new HarmonyMethod(typeof(StockHeadFragments), nameof(HeadsListed)));
            context.Harmony.Patch(Method(host, "Run"),
                finalizer: new HarmonyMethod(typeof(StockHeadFragments), nameof(HostFinished)));
            installed = instance;
            return true;
        }
        catch (Exception error)
        {
            context.Log($"Brutality stock head fragments unavailable: {error.GetBaseException().Message}");
            return false;
        }
    }

    static FieldInfo Field(Type type, string name) =>
        AccessTools.Field(type, name) ?? throw new MissingFieldException(type.Name, name);

    static MethodInfo Method(Type type, string name, params Type[] args) =>
        AccessTools.Method(type, name, args) ?? throw new MissingMethodException(type.Name, name);

    static void HeadCut(object __instance, CharacterSnapshot c, CharacterAppearance look)
    {
        if (look.Headless && installed is { } hooks && StockHostParts.HostOf(__instance) is { } host)
            hooks.Begin(host, c.Id, look, new(c.Knockdown.X, c.Knockdown.Z));
    }

    static void HeadsListed(object __instance, CharacterDrawList list)
    {
        if (installed is { } hooks && StockHostParts.HostOf(__instance) is { } host) hooks.Draw(host, list);
    }
    static void HostFinished(object __instance) => installed?.Reset(__instance);

    void Begin(object host, CharacterId victim, CharacterAppearance source, Vector2 travel)
    {
        if (session.GetValue(host) is not WorldSession shown
            || !StockHeadRules.TryGet(shown.World, victim, shown.ShownTick, out var request)
            || heads.GetValue(decapitation.GetValue(host)) is not IList falling) return;
        if (!StockHeadMeshes.CanCreate)
        {
            if (StockHeadMeshes.TorsoCapacity(new(1, 1)) == StockHeadMeshes.Capacity.LiveLimit)
                RequestRoom(host);
            return;
        }
        var display = For(host, shown);
        if (display.Pending is not null || !display.Physics.HasRoom(request.Shape.Mode == HeadCutMode.Burst ? 9 : 2)
            || drawKey.Invoke(host, [victim]) is not long from || renderer.Invoke(host, null) is not { } characters
            || states.GetValue(characters) is not IDictionary drawn || rigs.Invoke(characters, null) is not CharacterRigs setup) return;
        long key = from ^ (1L << 54);
        display.Attempts.TryGetValue(victim, out var attempt);
        if (attempt.Count >= 2 || shown.ShownTick < attempt.NextTick) return;

        CharacterInstance? fallback = null;
        object? held = drawn[key];
        if (!falling.Cast<object>().Any(head => nativeKey.GetValue(head) is long candidate && candidate == key))
        {
            // A head cut before the first live pose was drawn has no native falling head.
            // Take the corpse's rendered head pose once it is available.
            if (lastDrawn.Invoke(characters, [from, "Bip01 Head"]) is not ValueTuple<Matrix4x4, CharacterAppearance, Vector3> pose)
                return;
            held = drawn[from];
            fallback = new CharacterInstance(key, source.SeveredHead(), pose.Item3, 0, []) { World = pose.Item1 };
            if (trace) Console.WriteLine($"Brutality stock head cut uses corpse pose target={victim}");
        }
        if (held is null || bones.GetValue(held) is not Matrix4x4[] { Length: > 0 } palette) return;

        display.Attempts[victim] = (attempt.Count + 1, shown.ShownTick + 12);
        var cancel = new CancellationTokenSource();
        var token = cancel.Token;
        // Cut the stable source mesh. The native falling head still supplies the frozen pose and current transform.
        var accessories = lastDrawn.Invoke(characters, [from, "Bip01 Head"]) is ValueTuple<Matrix4x4, CharacterAppearance, Vector3> before
            ? before.Item2 : source;
        var task = Task.Run(() => StockHeadBuild.Build(context.Folder, setup, source, request.Shape, token, accessories), token);
        float force = travel.LengthSquared();
        if (!float.IsFinite(force)) travel = default;
        else if (force > 1) travel /= MathF.Sqrt(force);
        display.Pending = new(key, victim, request, palette.ToArray(), travel, task, cancel, fallback: fallback);
    }

    static void RequestRoom(object host)
    {
        StockTorsoFragments.RequestRoom(host);
        StockWoundPresentation.RequestHeadRoom(host);
    }

    void Draw(object host, CharacterDrawList list)
    {
        if (session.GetValue(host) is not WorldSession shown
            || !displays.TryGetValue(host, out _) && !StockHeadSplitSave.Has(shown.World)) return;
        var display = For(host, shown);
        display.Labels.Clear();
        StockPartLabels.Publish(shown.World, "heads", display.Labels);
        if (renderer.Invoke(host, null) is not { } characters) return;
        foreach (var owner in display.Attempts.Keys.Where(id => !StockHeadRules.TryGet(shown.World, id, shown.ShownTick, out _)).ToArray())
            display.Attempts.Remove(owner);
        if (display.Pending is null) BeginRestore(host, display, list, characters);
        if (display.Pending is null && display.Active.Count == 0 && display.Native.Count == 0) return;
        double time = (shown.ShownTick + (shown.TimeScale > 0 ? shown.SimulationAlpha : 0)) * shown.World.TickSeconds;
        display.Physics.SyncNativeContacts(shown.World, shown.ShownTick,
            id => display.Active.Any(fragment => fragment.Owner == id) || display.Pending?.Owner == id
                || display.Native.Values.Any(motion => motion.Owner == id));
        display.Physics.Advance(time, (float)shown.TimeScale, shown.ShownTick);
        DrawNative(display, list);
        CaptureSplits(display, time);
        for (int i = display.Active.Count - 1; i >= 0; i--)
        {
            var fragment = display.Active[i];
            if (DrawPose(display, fragment) is not { } pose)
            {
                StockHeadMeshes.Retire(fragment.Look);
                display.Active.RemoveAt(i);
                continue;
            }
            list.Add(new CharacterInstance(fragment.Key, fragment.Look, pose.Position, 0, [])
                { World = pose.Transform, FreezeFrom = fragment.Key, Blood = fragment.Blood });
            display.Labels.Add(Label(fragment, pose.Position));
        }
        if (display.Pending is not { } pending) return;
        if (pending.Abandoned)
        {
            if (pending.Worker.IsCompleted) Finish(display);
            return;
        }
        int index = list.Items.FindLastIndex(instance => instance.Key == pending.Native);
        if (index < 0 && pending.Fallback is null || pending.SinceCut.Elapsed.TotalSeconds > 7)
        {
            if (index < 0 && pending.Restoring is not null) display.RestoreTried.Remove(pending.Owner);
            Abandon(display);
            return;
        }
        if (!pending.Worker.IsCompleted) return;
        if (!pending.Worker.IsCompletedSuccessfully || pending.Worker.Result is not { } built || (uint)built.HeadBone >= (uint)pending.Palette.Length)
        {
            if (pending.Worker.Exception is { } fault) context.Log($"Brutality stock head cut skipped: {fault.GetBaseException().Message}");
            else if (trace) Console.WriteLine($"Brutality stock head cut skipped missing geometry or pose target={pending.Owner} bones={pending.Palette.Length}");
            Abandon(display);
            return;
        }

        var whole = index >= 0 ? list.Items[index] : pending.Fallback!.Value;
        if (index < 0 && list.Items.FindIndex(item => item.Key == (pending.Native ^ (1L << 54))) is int corpseIndex && corpseIndex >= 0)
            whole = whole with { Blood = list.Items[corpseIndex].Blood };
        if (pending.Restoring is { } layout)
            whole = new CharacterInstance(pending.Native, whole.Appearance.SeveredHead(),
                layout.Palette.Length == 0 ? whole.Position : layout.First.Position, 0, [])
                { World = layout.Palette.Length == 0 ? whole.World : layout.First.Transform,
                    Blood = layout.Palette.Length == 0 ? whole.Blood : layout.Blood };
        if (pending.Staged.Count == 0)
        {
            var cost = pending.Cost ??= StockHeadMeshes.HeadCost(built.Pieces, built.Equipment);
            var capacity = StockHeadMeshes.TorsoCapacity(cost);
            if (trace && pending.ReportedCapacity != capacity)
            {
                pending.ReportedCapacity = capacity;
                Console.WriteLine($"Brutality stock head admission target={pending.Owner} mode={pending.Request.Shape.Mode} meshes={cost.Meshes} bytes={cost.Bytes} capacity={capacity}");
            }
            if (capacity != StockHeadMeshes.Capacity.Available)
            {
                if (capacity == StockHeadMeshes.Capacity.TooLarge) Abandon(display);
                else if (capacity == StockHeadMeshes.Capacity.LiveLimit) RequestRoom(host);
                return;
            }
        }
        if (pending.Staged.Count < built.Pieces.Count)
        {
            // The vanilla upload queue builds one more cut mesh each frame. The whole head remains visible during this handover.
            var piece = built.Pieces[pending.Staged.Count];
            if (StockHeadMeshes.Create(whole.Appearance, piece.Body, piece.Parts,
                    built.Equipment?.GetValueOrDefault(piece.Key)) is not { } variant)
            {
                if (StockHeadMeshes.TorsoCapacity(StockHeadMeshes.HeadCost([piece], built.Equipment)) == StockHeadMeshes.Capacity.LiveLimit)
                    RequestRoom(host);
                return;
            }
            pending.Staged.Add(new(long.MinValue | ++display.Sequence, pending.Owner, piece, variant, whole.Blood));
            if (trace && pending.Staged.Count == built.Pieces.Count)
                Console.WriteLine($"Brutality stock head staged target={pending.Owner} parts={pending.Staged.Count}");
        }
        var uploaded = (IDictionary)assets.GetValue(characters)!;
        bool ready = pending.Staged.Count == built.Pieces.Count;
        foreach (var fragment in pending.Staged)
        {
            if (uploaded.Contains(fragment.Look))
            {
                if (uploaded[fragment.Look] is not { } asset
                    || assetParts.GetValue(asset) is not ICollection parts || parts.Count != 1 + fragment.Look.Parts.Count)
                {
                    if (trace) Console.WriteLine($"Brutality stock head fragments skipped an incomplete upload, part={fragment.Piece.Key}");
                    Abandon(display);
                    return;
                }
                continue;
            }
            list.Add(whole with { Key = fragment.Key, Appearance = fragment.Look, FreezeFrom = null, Hidden = true });
            ready = false;
        }
        if (!ready) return;
        if (pending.Restoring is { } saved)
        {
            if (pending.Staged.Count != 2) { Abandon(display); return; }
            if (saved.Palette.Length == 0)
            {
                var actor = list.Items[index];
                saved = saved with { Palette = pending.Palette, Blood = actor.Blood,
                    First = PlaceImported(display, actor, pending.Staged[0].Piece, -1),
                    Second = PlaceImported(display, actor, pending.Staged[1].Piece, 1) };
            }
            pending.Staged[0].Settled = saved.First;
            pending.Staged[1].Settled = saved.Second;
            int restoredGroup = ++display.Group;
            foreach (var fragment in pending.Staged)
            {
                HoldSavedPose(characters, fragment.Key, saved.Palette);
                var at = fragment.Settled!.Value;
                if (display.Physics.Resume(fragment.Key, fragment.Piece.Key, at, restoredGroup, whole.Appearance.Race.GetBool("is robot")))
                    fragment.Settled = null;
                list.Add(new CharacterInstance(fragment.Key, fragment.Look, at.Position, 0, [])
                    { World = at.Transform, FreezeFrom = fragment.Key, Blood = fragment.Blood });
                display.Labels.Add(Label(fragment, at.Position));
            }
            CommitSplit(display, pending, time);
            StockHeadSplitSave.Restored(display.World, pending.Owner, saved);
            CaptureSplits(display, time);
            display.Active.AddRange(pending.Staged);
            if (trace) Console.WriteLine($"Brutality stock restored detached head split target={pending.Owner}");
            Finish(display);
            return;
        }
        if (whole.World is not { } matrix) return;
        var skin = pending.Palette[built.HeadBone] * matrix;
        if (!display.Physics.HasRoom(pending.Staged.Count)) return;
        if (!Matrix4x4.Decompose(skin, out var scale, out var spin, out _))
        {
            Abandon(display);
            return;
        }
        int group = ++display.Group;
        bool split = pending.Request.Shape.Mode == HeadCutMode.DetachedSplit;
        foreach (var fragment in pending.Staged)
        {
            var piece = fragment.Piece;
            var start = Vector3.Transform(piece.Centre, skin);
            HoldSavedPose(characters, fragment.Key, pending.Palette);
            if (display.Physics.Place(fragment.Key, piece.Key, start, whole.Position, spin, matrix, piece.HalfExtent * Vector3.Abs(scale),
                    pending.Travel, piece.Brain, pending.Request.Shape.Mode == HeadCutMode.Burst,
                    split ? float.PositiveInfinity : StockHeadShardPhysics.Lifetime, pending.Request.Seed, group,
                    whole.Appearance.Race.GetBool("is robot")) is null)
            {
                Abandon(display);
                return;
            }
        }
        var falling = (IList)heads.GetValue(decapitation.GetValue(host))!;
        for (int i = falling.Count - 1; i >= 0; i--)
            if (nativeKey.GetValue(falling[i]) is long key && key == pending.Native) { falling.RemoveAt(i); break; }
        StockDetachedPools.ReplaceHead(host, pending.Native);
        if (index >= 0) list.Items.RemoveAt(index);
        foreach (var fragment in pending.Staged)
        {
            var position = display.Physics.Position(fragment.Key)!.Value;
            list.Add(whole with { Key = fragment.Key, Appearance = fragment.Look, Position = position,
                World = display.Physics.Transform(fragment.Key), FreezeFrom = fragment.Key });
            display.Labels.Add(Label(fragment, position));
        }
        display.Active.AddRange(pending.Staged);
        if (split)
        {
            foreach (var fragment in pending.Staged)
            {
                long key = fragment.Key;
                StockDetachedPools.Head(host, shown, key, whole.Appearance.Race, .5f,
                    () => fragment.Settled is not null ? fragment.PoolFloor : display.Physics.PoolContact(key),
                    () => display.Active.Contains(fragment));
            }
            CommitSplit(display, pending, time);
            CaptureSplits(display, time);
        }
        if (pending.Request.Shape.Mode == HeadCutMode.Burst)
        {
            var skull = Vector3.Zero;
            int count = 0;
            foreach (var fragment in pending.Staged)
                if (!fragment.Piece.Brain && display.Physics.Position(fragment.Key) is { } point)
                {
                    skull += point;
                    count++;
                }
            if (count > 0)
                Meitou.Brutality.Effects.MasterImpactFeed.EmitHeadBurst(host, skull / count, pending.Travel,
                    unchecked((uint)whole.Appearance.Race.GetInt("blood colour", 0x800000)) & 0xFFFFFF);
        }
        if (trace) Console.WriteLine(FormattableString.Invariant($"Brutality stock head fragments mode={pending.Request.Shape.Mode} skin={pending.Staged.Count(x => !x.Piece.Brain)} brain={pending.Staged.Count(x => x.Piece.Brain)} delayMs={pending.SinceCut.Elapsed.TotalMilliseconds:F0}"));
        StockHeadRules.Complete(shown.World, pending.Owner, pending.Request);
        Finish(display);
    }

    internal static CharacterInstance StartNative(object host, WorldSession shown, object characters,
        CharacterId owner, HeadCutState cut, CharacterInstance head, StockWoundBuild.HeadBounds bounds)
    {
        if (installed is not { } hooks || head.World is not { } world) return head;
        var display = hooks.For(host, shown);
        // The native head remains a usable fallback when all physics slots are busy.
        if (display.Native.ContainsKey(head.Key) || !display.Physics.HasRoom(10)
            || hooks.states.GetValue(characters) is not IDictionary drawn || drawn[head.Key] is not { } held
            || hooks.bones.GetValue(held) is not Matrix4x4[] palette || (uint)bounds.Bone >= (uint)palette.Length) return head;
        var skin = palette[bounds.Bone] * world;
        if (!Matrix4x4.Decompose(skin, out var scale, out var rotation, out _)) return head;
        var centre = Vector3.Transform(bounds.Centre, skin);
        if (display.Physics.Place(head.Key, 0, centre, head.Position, rotation, world,
                bounds.HalfExtent * Vector3.Abs(scale), default, brain: false, burst: false, float.PositiveInfinity,
                unchecked((ulong)head.Key), ++display.Group, head.Appearance.Race.GetBool("is robot"), native: true) is not { } transform
            || display.Physics.Position(head.Key) is not { } at) return head;
        var motion = new NativeMotion(owner, cut, palette.ToArray(), head.Blood);
        display.Native.Add(head.Key, motion);
        long key = head.Key;
        StockDetachedPools.Head(host, shown, key, head.Appearance.Race, 1,
            () => motion.Settled is not null ? motion.PoolFloor : display.Physics.PoolContact(key),
            () => display.Native.ContainsKey(key));
        if (display.Physics.SavePose(head.Key) is { } pose) CaptureLoose(display, head.Key, motion, pose);
        return head with { Position = at, World = transform };
    }

    static void DrawNative(Display display, CharacterDrawList list)
    {
        if (display.Native.Count == 0) return;
        long frame = ++display.NativeFrame;
        for (int index = 0; index < list.Items.Count; index++)
        {
            var head = list.Items[index];
            if (!display.Native.TryGetValue(head.Key, out var motion)) continue;
            motion.Seen = frame;
            if (motion.Settled is { } settled)
                list.Items[index] = head with { Position = settled.Position, World = settled.Transform };
            else if (display.Physics.SavePose(head.Key) is { } pose)
            {
                list.Items[index] = head with { Position = pose.Position, World = pose.Transform };
                CaptureLoose(display, head.Key, motion, pose);
            }
        }
        foreach (var (key, motion) in display.Native)
        {
            if (motion.Seen == frame || motion.Synthetic && list.Items.Any(item => (item.Key ^ (1L << 54)) == key)) continue;
            display.Physics.Remove(key);
            display.Native.Remove(key);
        }
    }

    // A loaded corpse has no stock FallingHead. Rebuild the loose side with its own frozen palette.
    internal static CharacterInstance? RestoredLoose(object host, WorldSession shown, object characters,
        CharacterId owner, HeadCutState cut, CharacterInstance body, CharacterAppearance look, StockWoundBuild.HeadBounds bounds)
    {
        if (installed is not { } hooks || hooks.rigs.Invoke(characters, null) is not CharacterRigs rigs
            || rigs.For(body.Appearance) is not { } setup) return null;
        var display = hooks.For(host, shown);
        long key = body.Key ^ (1L << 54);
        if (!display.Native.TryGetValue(key, out var motion))
        {
            var saved = StockHeadSplitSave.Loose(shown.World, owner);
            bool fitted = saved?.Palette.Length == setup.Rig.BoneCount;
            var palette = fitted ? saved!.Palette : Enumerable.Repeat(Matrix4x4.Identity, setup.Rig.BoneCount).ToArray();
            var pose = fitted ? saved!.First : GroundImported(display, body, bounds.Centre, bounds.HalfExtent, 1);
            motion = new NativeMotion(owner, cut, palette, fitted ? saved!.Blood : body.Blood, synthetic: true);
            if (!display.Physics.Resume(key, 0, pose, ++display.Group, body.Appearance.Race.GetBool("is robot")))
                motion.Settled = pose with { Moving = null };
            display.Native.Add(key, motion);
            if (hooks.trace) Console.WriteLine($"Brutality stock restored loose head owner={owner} moving={motion.Settled is null}");
            CaptureLoose(display, key, motion, motion.Settled ?? pose);
        }
        if (motion.Settled is not { } && display.Physics.SavePose(key) is { } moving) CaptureLoose(display, key, motion, moving);
        if ((motion.Settled ?? display.Physics.SavePose(key)) is not { } placement) return null;
        if (hooks.states.GetValue(characters) is IDictionary drawn && !drawn.Contains(key))
            hooks.HoldSavedPose(characters, key, motion.Palette);
        return new CharacterInstance(key, look, placement.Position, 0, [])
            { World = placement.Transform, FreezeFrom = key, Blood = motion.Blood };
    }

    static void CaptureLoose(Display display, long key, NativeMotion motion, StockHeadSplitSave.Pose pose)
    {
        if (pose.Moving is null || display.World.Tick >= motion.NextCapture)
        {
            StockHeadSplitSave.Publish(display.World, motion.Owner, motion.Cut, unchecked((ulong)key), motion.Palette, motion.Blood, pose, default);
            motion.NextCapture = display.World.Tick + 8;
        }
        if (pose.Moving is not null) return;
        motion.PoolFloor = display.Physics.PoolContact(key);
        motion.Settled = pose;
        display.Physics.Remove(key);
    }

    internal static Meitou.Brutality.Effects.CutFluidContact? MechanicalContact(object host, CharacterId owner, int half)
    {
        if (installed is not { } hooks || !hooks.displays.TryGetValue(host, out var display)
            || !display.Splits.TryGetValue(owner, out var split)
            || DrawPose(display, half == 0 ? split.First : split.Second) is not { } pose) return null;
        return new(pose.Position, half == 0 ? Vector3.UnitY : -Vector3.UnitY);
    }

    void BeginRestore(object host, Display display, CharacterDrawList list, object characters)
    {
        if (!StockHeadMeshes.CanCreate || rigs.Invoke(characters, null) is not CharacterRigs setup) return;
        foreach (var (owner, layout) in StockHeadSplitSave.Pending(display.World))
        {
            if (display.RestoreTried.Contains(owner) || drawKey.Invoke(host, [owner]) is not long key) continue;
            int index = list.Items.FindIndex(item => item.Key == key && item.Appearance.Headless && !item.Hidden);
            if (index < 0) continue;
            var look = WithHeadEquipment(context, list.Items[index].Appearance, installRoot);
            if (setup.For(look) is not { } rig || layout.Palette.Length != 0 && rig.Rig.BoneCount != layout.Palette.Length) continue;
            var palette = layout.Palette.Length == 0 ? Enumerable.Repeat(Matrix4x4.Identity, rig.Rig.BoneCount).ToArray() : layout.Palette;
            display.RestoreTried.Add(owner);
            var cancel = new CancellationTokenSource();
            var token = cancel.Token;
            var worker = Task.Run(() => StockHeadBuild.Build(context.Folder, setup, look, layout.Shape, token), token);
            display.Pending = new(key, owner, new(display.World.Tick, layout.Seed, layout.Shape), palette, default, worker, cancel, layout);
            return;
        }
    }

    internal static CharacterAppearance WithHeadEquipment(ModContext context, CharacterAppearance look, string? installRoot)
    {
        if (!look.Headless || look.Loadout is not { } worn || installRoot is null) return look;
        var clothing = SavedKit.Copy(worn);
        clothing.Decapitated = false;
        return CharacterAppearance.Build(context.Data, installRoot, (look.Character ?? look.Race).StringId, new() { Loadout = clothing });
    }

    static StockHeadSplitSave.Pose PlaceImported(Display display, CharacterInstance owner, PreparedHeadBurstShard piece, int side) =>
        GroundImported(display, owner, piece.Centre, piece.HalfExtent, side, piece.Radius);

    static StockHeadSplitSave.Pose GroundImported(Display display, CharacterInstance owner, Vector3 centre, Vector3 halfExtent, int side, float radius = 1)
    {
        // Older saves preserve the cut plane but have no stock pose; place the loose mesh beside the corpse.
        var yaw = Matrix4x4.CreateRotationY(owner.Yaw);
        var flat = Vector3.TransformNormal(new Vector3(side * (2.8f + radius * .55f), 0, 2.2f), yaw);
        var rotation = Matrix4x4.CreateScale(owner.Scale) * Matrix4x4.CreateRotationZ(side * 1.10f) * yaw;
        var at = owner.Position + flat;
        float ground = display.World.Walkability.GroundHeight(at.X, at.Z, at.Y);
        float halfHeight = Vector3.Dot(halfExtent, Vector3.Abs(new(rotation.M12, rotation.M22, rotation.M32)));
        if (float.IsFinite(ground)) at.Y = ground + MathF.Max(.15f, halfHeight);
        var transform = rotation * Matrix4x4.CreateTranslation(at - Vector3.Transform(centre, rotation));
        return new(at, transform);
    }

    void HoldSavedPose(object characters, long key, Matrix4x4[] palette)
    {
        var state = Activator.CreateInstance(characterState, nonPublic: true)!;
        var stamp = rendererStamp.GetValue(characters);
        bones.SetValue(state, (Matrix4x4[])palette.Clone());
        previousBones.SetValue(state, (Matrix4x4[])palette.Clone());
        posed.SetValue(state, stamp);
        seen.SetValue(state, stamp);
        frozen.SetValue(state, true);
        ((IDictionary)states.GetValue(characters)!)[key] = state;
    }

    static StockHeadSplitSave.Pose? DrawPose(Display display, Fragment fragment) => fragment.Settled
        ?? display.Physics.SavePose(fragment.Key);

    static void CommitSplit(Display display, Pending pending, double time)
    {
        if (display.Splits.Count >= StockHeadSplitSave.Capacity)
        {
            var oldest = display.Splits.MinBy(entry => entry.Value.Started);
            RemoveSplit(display, oldest.Key, oldest.Value, evict: true);
        }
        display.Splits[pending.Owner] = new(pending.Request, pending.Palette,
            pending.Staged[0], pending.Staged[1], time)
            { CaptureNext = 0 };
    }

    static void RemoveSplit(Display display, CharacterId owner, Split split, bool evict)
    {
        foreach (var fragment in new[] { split.First, split.Second })
        {
            display.Physics.Remove(fragment.Key);
            StockHeadMeshes.Retire(fragment.Look);
            display.Active.Remove(fragment);
        }
        display.Splits.Remove(owner);
        StockHeadSplitSave.Remove(display.World, owner, evict);
    }

    static void CaptureSplits(Display display, double time)
    {
        foreach (var (owner, split) in display.Splits)
        {
            if (!StockHeadSplitSave.Exists(display.World, owner) && split.CaptureNext > 0)
            {
                RemoveSplit(display, owner, split, evict: false);
                continue;
            }
            if (split.First.Settled is not null && split.Second.Settled is not null || time < split.CaptureNext
                || DrawPose(display, split.First) is not { } first || DrawPose(display, split.Second) is not { } second) continue;
            StockHeadSplitSave.Publish(display.World, owner, split.Request.Shape, split.Request.Seed, split.Palette,
                split.First.Blood, first, second);
            split.CaptureNext = time + .25;
            if (first.Moving is not null || second.Moving is not null) continue;
            split.First.PoolFloor = display.Physics.PoolContact(split.First.Key);
            split.Second.PoolFloor = display.Physics.PoolContact(split.Second.Key);
            split.First.Settled = first;
            split.Second.Settled = second;
            display.Physics.Remove(split.First.Key);
            display.Physics.Remove(split.Second.Key);
        }
    }

    Display For(object host, WorldSession shown)
    {
        int currentEpoch = StockHostParts.WorldEpoch(host);
        if (displays.TryGetValue(host, out var display) && display.Epoch == currentEpoch && ReferenceEquals(display.World, shown.World))
            return display;
        Reset(host);
        return displays.GetValue(host, _ => new(shown.World, currentEpoch));
    }

    void Reset(object host)
    {
        if (!displays.TryGetValue(host, out var display)) return;
        Abandon(display);
        if (display.Pending is { } pending)
            _ = pending.Worker.ContinueWith(task =>
            {
                _ = task.Exception;
                pending.Cancel.Dispose();
            }, TaskScheduler.Default);
        foreach (var fragment in display.Active) StockHeadMeshes.Retire(fragment.Look);
        display.Physics.Dispose();
        StockPartLabels.Publish(display.World, "heads", []);
        displays.Remove(host);
    }

    static BrutalityPartLabel Label(Fragment fragment, Vector3 position)
    {
        string part = fragment.Piece.Key switch
        {
            20 => "Head half 1",
            21 => "Head half 2",
            _ => fragment.Piece.Brain ? "Brain fragment" : "Head fragment",
        };
        return new(fragment.Owner, 300 + fragment.Piece.Key, part, position);
    }

    static void Abandon(Display display)
    {
        if (display.Pending is not { } pending) return;
        pending.Abandoned = true;
        pending.Cancel.Cancel();
        foreach (var fragment in pending.Staged)
        {
            display.Physics.Remove(fragment.Key);
            StockHeadMeshes.Retire(fragment.Look);
        }
        pending.Staged.Clear();
        if (pending.Worker.IsCompleted) Finish(display);
    }

    static void Finish(Display display)
    {
        var pending = display.Pending!;
        _ = pending.Worker.Exception;
        pending.Cancel.Dispose();
        display.Pending = null;
    }
}
