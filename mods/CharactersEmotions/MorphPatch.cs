using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Modding;

namespace Meitou.CharactersEmotions;

/// <summary>
/// Per-frame shape keys on the renderer (Harmony on <c>CharacterRenderer.Update</c>, the render thread's serial step that fills the frame's live list
/// before anything is drawn). For each drawn character the face clock tracks, the look's own pose weights with the managed keys set over them are
/// summed into morph offsets (the renderer's own <c>MeshMorphs.Deltas</c>) and written into a block of the renderer's morph arena that the mod
/// keeps for that asset (three regions used in turn, as the editor's blend does, so a frame in flight never reads the one written), and the asset's
/// <c>MorphBase</c> names it for that frame's draws. The prefix gives every asset its own base back before the renderer's update, so its blend and
/// build code never see the mod's. A failure logs once and stops the mod's part; the faces are then as without the mod.
/// </summary>
static class MorphPatch
{
    const string RendererType = "Meitou.Game.Rendering.Characters.CharacterRenderer";

    /// <summary>Updates an asset may go undrawn before its block is given back.</summary>
    const long EvictAfter = 600;

    static readonly ConditionalWeakTable<object, RendererFaces> Renderers = [];
    static readonly ConditionalWeakTable<object, StrongBox<FaceSupport>> SupportCache = [];
    static Reflected? s_r;
    static EmotionBook s_book = null!;
    static Action<string> s_log = null!;
    static volatile bool s_failed;

    public static void Install(ModContext context, EmotionBook book)
    {
        s_book = book;
        s_log = context.LogError;
        var type = typeof(ModContext).Assembly.GetType(RendererType) ?? throw new MissingMemberException(RendererType);
        s_r = new Reflected(type);
        var update = AccessTools.Method(type, "Update", [typeof(Vector3)]) ?? throw new MissingMethodException(RendererType, "Update");
        context.Harmony.Patch(update, prefix: new HarmonyMethod(typeof(MorphPatch), nameof(Prefix)),
            postfix: new HarmonyMethod(typeof(MorphPatch), nameof(Postfix)));
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void Prefix(object __instance)
    {
        try
        {
            if (s_r is { } r && Renderers.TryGetValue(__instance, out var faces)) faces.Restore(r);
        }
        catch (Exception e)
        {
            Fail(e);
        }
    }

    static void Postfix(object __instance)
    {
        if (s_failed || s_r is not { } r) return;

        try
        {
            var weights = FaceBoard.Weights;
            if (weights.Count == 0 && !Renderers.TryGetValue(__instance, out _)) return;

            Renderers.GetValue(__instance, i => new RendererFaces(r.Arena(i))).Apply(r, __instance, weights);
        }
        catch (Exception e)
        {
            Fail(e);
        }
    }

#pragma warning restore IDE1006

    static void Fail(Exception e)
    {
        if (s_failed) return;

        s_failed = true;
        s_log($"live expressions stopped (the renderer differs from the one this port was built for): {e}");
    }

    /// <summary>What the mesh of <paramref name="poses"/> supports: every default key, the animation keys, the speech keys.</summary>
    static FaceSupport SupportOf(Reflected r, object poses) => SupportCache.GetValue(poses, p =>
    {
        var names = r.PoseNames(p);
        var support = FaceSupport.None;
        if (s_book.BaseKeys.Count > 0 && s_book.BaseKeys.All(names.Contains)) support |= FaceSupport.Base;
        if (s_book.AnimKeys.Count > 0 && s_book.AnimKeys.All(names.Contains)) support |= FaceSupport.Anim;
        if (EmotionBook.SpeechKeys.All(names.Contains)) support |= FaceSupport.Speech;
        return new StrongBox<FaceSupport>(support);
    }).Value;

    /// <summary>The mod's morph blocks of one renderer, by asset.</summary>
    sealed class RendererFaces(object arena)
    {
        readonly Dictionary<object, Override> _overrides = new(ReferenceEqualityComparer.Instance);
        readonly Dictionary<int, Stack<int>> _free = [];
        long _stamp;

        public void Restore(Reflected r)
        {
            foreach (var (asset, o) in _overrides) r.SetBase(asset, o.Original);
        }

        public void Apply(Reflected r, object renderer, IReadOnlyDictionary<long, IReadOnlyDictionary<string, float>> weights)
        {
            _stamp++;
            int count = r.LiveCount(renderer);
            for (int i = 0; i < count && weights.Count > 0; i++)
            {
                long id = FaceBoard.FromDrawKey(r.KeyAt(renderer, i));
                if (weights.TryGetValue(id, out var w)) Face(r, r.AssetAt(renderer, i), id, w);
            }

            Evict();
        }

        void Face(Reflected r, object asset, long id, IReadOnlyDictionary<string, float> w)
        {
            if (r.Morph(asset) is not { } morph || r.Busy(asset))
            {
                FaceBoard.Support[id] = FaceSupport.None;
                return;
            }

            var (poses, own, cutHorns) = r.Source(morph);
            var support = SupportOf(r, poses);
            FaceBoard.Support[id] = support;
            if ((support & FaceSupport.Base) == 0 || w.Count == 0) return;

            if (!_overrides.TryGetValue(asset, out var o))
            {
                uint original = r.Base(asset);
                if (original == 0) return;   // built without a morph: its parts read none

                o = new Override(original, r.SlotCount(poses), Block(r, r.SlotCount(poses)));
                _overrides[asset] = o;
            }

            o.Seen = _stamp;
            if (o.Last is null || !Same(o.Last, w)) Write(r, o, poses, own, cutHorns, w);
            r.SetBase(asset, o.Current);
        }

        void Write(Reflected r, Override o, object poses, IReadOnlyDictionary<string, float> own, bool cutHorns, IReadOnlyDictionary<string, float> w)
        {
            var merged = new Dictionary<string, float>(own, StringComparer.Ordinal);
            foreach (var (key, value) in w) merged[key] = value;

            var deltas = r.Deltas(poses, merged, cutHorns) ?? new Vector4[o.Slots];
            int first = o.First + o.Turn * o.Slots;
            o.Turn = (o.Turn + 1) % 3;
            r.Write(arena, first, deltas);
            o.Current = (uint)(first - 1);
            o.Last = w;
        }

        int Block(Reflected r, int slots) =>
            _free.TryGetValue(slots, out var stack) && stack.Count > 0 ? stack.Pop() : r.Reserve(arena, 3 * slots);

        void Evict()
        {
            if (_stamp % 120 != 0) return;

            foreach (var (asset, o) in _overrides.Where(p => _stamp - p.Value.Seen > EvictAfter).ToList())
            {
                _overrides.Remove(asset);
                if (!_free.TryGetValue(o.Slots, out var stack)) _free[o.Slots] = stack = new Stack<int>();
                stack.Push(o.First);
            }
        }

        static bool Same(IReadOnlyDictionary<string, float> a, IReadOnlyDictionary<string, float> b)
        {
            if (ReferenceEquals(a, b)) return true;

            if (a.Count != b.Count) return false;

            foreach (var (key, value) in a)
            {
                if (!b.TryGetValue(key, out float other) || other != value) return false;
            }

            return true;
        }
    }

    /// <summary>An asset's own base and the mod's block for it (three regions of <see cref="Slots"/> entries).</summary>
    sealed class Override(uint original, int slots, int first)
    {
        public uint Original { get; } = original;
        public int Slots { get; } = slots;
        public int First { get; } = first;
        public int Turn;
        public uint Current;
        public long Seen;
        public IReadOnlyDictionary<string, float>? Last;
    }

    /// <summary>The renderer's members the patch reads and writes, found once by name.</summary>
    sealed class Reflected
    {
        readonly Func<object, int, long> _keyAt;
        readonly Func<object, int, object> _assetAt;
        readonly FieldInfo _liveCount, _content, _morphBase, _morph, _blending, _isIcon, _slotCount, _poses;
        readonly PropertyInfo _morphs, _sourcePoses, _sourceWeights, _sourceCutHorns;
        readonly MethodInfo _deltas, _reserve, _write;

        public Reflected(Type renderer)
        {
            var asm = renderer.Assembly;
            var live = renderer.GetNestedType("Live", BindingFlags.NonPublic) ?? throw new MissingMemberException(renderer.FullName, "Live");
            var liveArray = Field(renderer, "_live");
            _keyAt = LiveReader<long>(renderer, liveArray, live, Field(live, "Key"));
            _assetAt = LiveReader<object>(renderer, liveArray, live, Field(live, "Asset"));
            _liveCount = Field(renderer, "_liveCount");
            _content = Field(renderer, "_content");

            var content = _content.FieldType;
            _morphs = content.GetProperty("Morphs", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new MissingMemberException(content.FullName, "Morphs");
            var arena = _morphs.PropertyType;
            _reserve = AccessTools.Method(arena, "Reserve", [typeof(int)]) ?? throw new MissingMethodException(arena.FullName, "Reserve");
            _write = AccessTools.Method(arena, "Write", [typeof(int), typeof(Vector4[])])
                ?? throw new MissingMethodException(arena.FullName, "Write");

            var asset = Type(asm, "Meitou.Game.Rendering.Characters.CharacterAsset");
            (_morphBase, _morph) = (Field(asset, "MorphBase"), Field(asset, "Morph"));
            (_blending, _isIcon) = (Field(asset, "Blending"), Field(asset, "IsIcon"));
            var source = _morph.FieldType;
            _sourcePoses = Property(source, "Poses");
            _sourceWeights = Property(source, "Weights");
            _sourceCutHorns = Property(source, "CutHorns");

            var mesh = _sourcePoses.PropertyType;
            (_slotCount, _poses) = (Field(mesh, "SlotCount"), Field(mesh, "Poses"));
            _deltas = AccessTools.Method(mesh, "Deltas") ?? throw new MissingMethodException(mesh.FullName, "Deltas");
        }

        public object Arena(object renderer) => _morphs.GetValue(_content.GetValue(renderer))!;
        public int LiveCount(object renderer) => (int)_liveCount.GetValue(renderer)!;
        public long KeyAt(object renderer, int i) => _keyAt(renderer, i);
        public object AssetAt(object renderer, int i) => _assetAt(renderer, i);
        public object? Morph(object asset) => _morph.GetValue(asset);
        public bool Busy(object asset) => (bool)_blending.GetValue(asset)! || (bool)_isIcon.GetValue(asset)!;
        public uint Base(object asset) => (uint)_morphBase.GetValue(asset)!;
        public void SetBase(object asset, uint value) => _morphBase.SetValue(asset, value);
        public int SlotCount(object poses) => (int)_slotCount.GetValue(poses)!;

        public (object Poses, IReadOnlyDictionary<string, float> Weights, bool CutHorns) Source(object morph) =>
            (_sourcePoses.GetValue(morph)!, (IReadOnlyDictionary<string, float>)_sourceWeights.GetValue(morph)!,
                (bool)_sourceCutHorns.GetValue(morph)!);

        public HashSet<string> PoseNames(object poses) =>
            [.. ((IDictionary)_poses.GetValue(poses)!).Keys.Cast<string>()];

        public Vector4[]? Deltas(object poses, IReadOnlyDictionary<string, float> weights, bool cutHorns) =>
            (Vector4[]?)_deltas.Invoke(poses, [weights, cutHorns]);

        public int Reserve(object arena, int count) => (int)_reserve.Invoke(arena, [count])!;
        public void Write(object arena, int first, Vector4[] deltas) => _write.Invoke(arena, [first, deltas]);

        static Type Type(Assembly asm, string name) => asm.GetType(name) ?? throw new MissingMemberException(name);

        static FieldInfo Field(Type type, string name) => AccessTools.Field(type, name) ?? throw new MissingFieldException(type.FullName, name);

        static PropertyInfo Property(Type type, string name) =>
            AccessTools.Property(type, name) ?? throw new MissingMemberException(type.FullName, name);

        /// <summary>A reader of field <paramref name="field"/> of <c>_live[i]</c>, by reference (the entries are large structs).</summary>
        static Func<object, int, T> LiveReader<T>(Type renderer, FieldInfo array, Type live, FieldInfo field)
        {
            var method = new DynamicMethod("CharactersEmotions_" + field.Name, typeof(T), [typeof(object), typeof(int)], renderer, true);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, renderer);
            il.Emit(OpCodes.Ldfld, array);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldelema, live);
            il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Ret);
            return method.CreateDelegate<Func<object, int, T>>();
        }
    }
}
