using System.Collections;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Characters;
using Meitou.Modding;

namespace Meitou.Transmog;

/// <summary>
/// The model adjustment of skinned appearances (clothing, backpacks, limbs), which have no attachment point to carry it: a prefix and postfix on
/// <c>CharacterContent.AddItemParts</c> note which of a built asset's parts come from an adjusted part of a transmog look (the parts are added in
/// the look's order, a mesh not found adding none), and a postfix on <c>CharacterRenderer.PoseLive</c> multiplies their placed world matrices (this
/// update's and the last, for the motion vectors) by the adjustment in the character's frame. The world views (colour, motion, shadows) read
/// those placements; a portrait or another eye places its own parts and shows them unadjusted. A rigid part takes its adjustment through its
/// own <see cref="CharacterPart.Local"/> (<see cref="LookBuilder"/>) everywhere.
/// </summary>
static class RenderPatch
{
    const string Content = "Meitou.Game.Rendering.Characters.CharacterContent";
    const string Renderer = "Meitou.Game.Rendering.Characters.CharacterRenderer";

    /// <summary>The adjusted parts of each asset: their index in its parts and the adjustment.</summary>
    static readonly ConditionalWeakTable<object, (int Index, Matrix4x4 Transform)[]> Adjusted = new();

    static int s_anyAdjusted;
    static ModContext? s_context;
    static int s_faulted;

    static Func<object, IList>? s_partsOf;
    static Func<object, string>? s_labelOf;
    static Func<object, int>? s_liveCount;
    static Func<object, int, object?>? s_assetAt;
    static Func<object, int, int>? s_partBaseAt;
    static Func<object, int, bool>? s_portraitOnlyAt;
    static Action<object, int, Matrix4x4>? s_adjust;

    public static void Install(ModContext context)
    {
        s_context = context;
        var content = AccessTools.TypeByName(Content);
        var renderer = AccessTools.TypeByName(Renderer);
        var add = content is null ? null : AccessTools.Method(content, "AddItemParts");
        var pose = renderer is null ? null : AccessTools.Method(renderer, "PoseLive");
        if (add is null || pose is null || !Bind(renderer!))
        {
            context.LogError("CharacterContent.AddItemParts or CharacterRenderer.PoseLive not found: skinned gear is drawn unadjusted");
            return;
        }

        context.Harmony.Patch(add, prefix: new HarmonyMethod(typeof(RenderPatch), nameof(AddItemPartsPrefix)),
            postfix: new HarmonyMethod(typeof(RenderPatch), nameof(AddItemPartsPostfix)));
        context.Harmony.Patch(pose, postfix: new HarmonyMethod(typeof(RenderPatch), nameof(PoseLivePostfix)));
    }

    #region Accessors

    /// <summary>Compiles the reads and the matrix write of the renderer's internals; false when one of them is missing.</summary>
    static bool Bind(Type renderer)
    {
        try
        {
            var asset = AccessTools.TypeByName("Meitou.Game.Rendering.Characters.CharacterAsset");
            var part = AccessTools.TypeByName("Meitou.Game.Rendering.Characters.AssetPart");
            s_partsOf = Getter<IList>(asset, "Parts");
            s_labelOf = Getter<string>(part, "Label");
            s_liveCount = Getter<int>(renderer, "_liveCount");
            s_assetAt = LiveField<object?>(renderer, "Asset");
            s_partBaseAt = LiveField<int>(renderer, "PartBase");
            s_portraitOnlyAt = LiveField<bool>(renderer, "PortraitOnly");
            s_adjust = FrameAdjuster(renderer);
            return true;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or NullReferenceException)
        {
            s_context?.LogError($"renderer internals changed: {e.Message}");
            return false;
        }
    }

    static Func<object, T> Getter<T>(Type type, string field)
    {
        var target = Expression.Parameter(typeof(object));
        var read = Expression.PropertyOrField(Expression.Convert(target, type), field);
        return Expression.Lambda<Func<object, T>>(Expression.Convert(read, typeof(T)), target).Compile();
    }

    /// <summary>A field of the renderer's <c>_live[i]</c>.</summary>
    static Func<object, int, T> LiveField<T>(Type renderer, string field)
    {
        var target = Expression.Parameter(typeof(object));
        var index = Expression.Parameter(typeof(int));
        var lives = Expression.Field(Expression.Convert(target, renderer), "_live");
        var read = Expression.Field(Expression.ArrayAccess(lives, index), field);
        return Expression.Lambda<Func<object, int, T>>(Expression.Convert(read, typeof(T)), target, index).Compile();
    }

    /// <summary><c>_partFrames[i].Model</c> and <c>.PreviousModel</c> each multiplied by the adjustment, in place.</summary>
    static Action<object, int, Matrix4x4> FrameAdjuster(Type renderer)
    {
        var target = Expression.Parameter(typeof(object));
        var index = Expression.Parameter(typeof(int));
        var transform = Expression.Parameter(typeof(Matrix4x4));
        var frames = Expression.Field(Expression.Convert(target, renderer), "_partFrames");
        var frame = Expression.ArrayAccess(frames, index);
        var model = Expression.Field(frame, "Model");
        var previous = Expression.Field(Expression.ArrayAccess(frames, index), "PreviousModel");
        var body = Expression.Block(
            Expression.Assign(model, Expression.Multiply(transform, model)),
            Expression.Assign(previous, Expression.Multiply(transform, previous)));
        return Expression.Lambda<Action<object, int, Matrix4x4>>(body, target, index, transform).Compile();
    }

    #endregion

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void AddItemPartsPrefix(object asset, CharacterAppearance c, SkeletonRig rig, out int __state)
    {
        __state = -1;
        try
        {
            if (LookBuilder.OnHead.TryGetValue(c, out var heads)) MoveToHead(c, rig, heads);
            __state = s_partsOf!(asset).Count;
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    static void AddItemPartsPostfix(object asset, CharacterAppearance c, int __state)
    {
        try
        {
            if (__state >= 0 && LookBuilder.Skinned.TryGetValue(c, out var adjusted)) Note(asset, c, adjusted, __state);
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    static void PoseLivePostfix(object __instance)
    {
        try
        {
            if (Volatile.Read(ref s_anyAdjusted) != 0) Place(__instance);
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }
#pragma warning restore IDE1006

    /// <summary>The attachment point the converted head pieces hang from: the head bone, no offset of its own.</summary>
    const string HeadPoint = "dxseba.transmog:head";

    /// <summary>
    /// The adjusted skinned head pieces of a look become parts on the head bone before its asset's parts are made: placed by the inverse of the
    /// head's binding pose (a piece weighted to the head is drawn where its skinning would put it, less the bone's shape scale), then the
    /// adjustment in the head's frame. They then follow the head and show in portraits as any part on a bone. Once per look (locked: assets of
    /// one look may be built on several threads).
    /// </summary>
    static void MoveToHead(CharacterAppearance c, SkeletonRig rig, Dictionary<CharacterPart, Matrix4x4> heads)
    {
        int head = rig.HeadBone;
        if (head < 0 || rig.Skeleton.Bones.FirstOrDefault(b => b.Handle == head)?.Name is not { } bone) return;

        lock (heads)
        {
            var unbind = Matrix4x4.CreateTranslation(-rig.BindPosition[head]) * Matrix4x4.CreateFromQuaternion(rig.BindRotationInverse[head])
                * Matrix4x4.CreateScale(Vector3.One / rig.BindScale[head]);
            c.AttachmentPoints[HeadPoint] = new AttachmentPoint(HeadPoint, bone, Vector3.Zero, Quaternion.Identity);
            for (int i = 0; i < c.Parts.Count; i++)
            {
                var part = c.Parts[i];
                if (part.Mode != AttachMode.SharedSkeleton || !heads.TryGetValue(part, out var transform)) continue;

                c.Parts[i] = part with { Mode = AttachMode.Bone, Point = HeadPoint, Local = unbind * transform };
            }
        }
    }

    /// <summary>Pairs the asset parts added from <paramref name="first"/> on with the look's parts by their label, in order.</summary>
    static void Note(object asset, CharacterAppearance c, Dictionary<CharacterPart, Matrix4x4> adjusted, int first)
    {
        var parts = s_partsOf!(asset);
        var found = new List<(int, Matrix4x4)>();
        int next = first;
        foreach (var part in c.Parts)
        {
            if (next >= parts.Count) break;

            if (s_labelOf!(parts[next]!) != $"{part.Record.Type} '{part.Record.Name}' .{part.Field}") continue;

            if (adjusted.TryGetValue(part, out var transform)) found.Add((next, transform));
            next++;
        }

        if (found.Count == 0) return;

        Adjusted.AddOrUpdate(asset, [.. found]);
        Interlocked.Exchange(ref s_anyAdjusted, 1);
    }

    /// <summary>The adjusted parts of every character placed in the world this update.</summary>
    static void Place(object renderer)
    {
        int count = s_liveCount!(renderer);
        for (int i = 0; i < count; i++)
        {
            if (s_assetAt!(renderer, i) is not { } asset || !Adjusted.TryGetValue(asset, out var parts) || s_portraitOnlyAt!(renderer, i)) continue;

            int partBase = s_partBaseAt!(renderer, i);
            foreach (var (index, transform) in parts) s_adjust!(renderer, partBase + index, transform);
        }
    }

    static void Fault(Exception e)
    {
        if (Interlocked.Exchange(ref s_faulted, 1) == 0) s_context?.LogError($"a skinned gear adjustment failed, drawn unadjusted: {e}");
    }
}
