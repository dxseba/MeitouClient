using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using HarmonyLib;
using Meitou.Data.Characters;
using Meitou.Game.Rendering.Frame;
using Meitou.Game.Rendering.Sky;
using Meitou.Modding;

namespace Meitou.FlowingHair;

/// <summary>
/// The hair meshes' side: a hair or beard mesh prepared as worn gear on a skeleton gets the plan's marker as its hanging-mass rule
/// (<c>CharacterContent.Mesh</c> and <c>Prepare</c>, so the worker's prefetch and the render thread's build share one cache entry), the marker
/// makes <c>GearSway.BakeMesh</c> bake it onto the plan's copies instead of a backpack's, and a look built with baked hair gets the doubled
/// palette (<c>CharacterContent.AddItemParts</c>).
/// </summary>
static class MeshPatches
{
    /// <summary>The baked hair of each built look: its plans, and whether each flows (its skeleton is the one the plan was made for).</summary>
    public static readonly ConditionalWeakTable<object, LookHair> Looks = new();

    static Func<object, object>? s_rigs;
    static Func<object, object>? s_rigCache;
    static Func<object, object>? s_rigTable;
    static Func<object, string, string?>? s_findMesh;
    static Action<object, bool>? s_swayPalette;
    static PropertyInfo? s_frontZ, s_backZ;
    static ModContext? s_context;
    static int s_faulted;

    public static void Install(ModContext context, Assembly game)
    {
        s_context = context;
        var content = Access.Type(game, "Meitou.Game.Rendering.Characters.CharacterContent");
        var asset = Access.Type(game, "Meitou.Game.Rendering.Characters.CharacterAsset");
        var gearSway = Access.Type(game, "Meitou.Game.Rendering.Characters.GearSway");
        var bake = Access.Nested(gearSway, "Bake");
        Bind(content, asset, bake);

        var harmony = context.Harmony;
        harmony.Patch(Access.Method(content, "Mesh", 10), prefix: new HarmonyMethod(typeof(MeshPatches), nameof(MeshPrefix)));
        harmony.Patch(Access.Method(content, "Prepare", 11), prefix: new HarmonyMethod(typeof(MeshPatches), nameof(PreparePrefix)));
        harmony.Patch(Access.Method(gearSway, "BakeMesh", 2), prefix: new HarmonyMethod(typeof(MeshPatches), nameof(BakePrefix)));
        harmony.Patch(Access.Method(content, "AddItemParts", 4), postfix: new HarmonyMethod(typeof(MeshPatches), nameof(ItemPartsPostfix)));
        harmony.Patch(Access.Method(typeof(WorldWeather), "Update", 4), postfix: new HarmonyMethod(typeof(MeshPatches), nameof(WeatherPostfix)));
    }

    static void Bind(Type content, Type asset, Type bake)
    {
        var rigsType = typeof(CharacterRigs);
        var cacheType = Access.Type(rigsType.Assembly, "Meitou.Data.Characters.SkeletonRigCache");
        s_rigs = Access.Getter<object>(content, "Rigs");
        s_rigCache = Access.Getter<object>(rigsType, "_rigCache");
        s_rigTable = Access.Getter<object>(cacheType, "_rigs");
        var find = Access.Method(content, "FindMesh", 1);
        s_findMesh = (target, path) => (string?)find.Invoke(target, [path]);
        s_swayPalette = Access.Setter<bool>(asset, "SwayPalette");
        s_frontZ = bake.GetProperty("FrontZ");
        s_backZ = bake.GetProperty("BackZ");
        HairPlans.MakeMarker = (bones, id, kind) => Activator.CreateInstance(bake, bones, float.NaN, (float)id, (float)kind)!;
    }

    static void Fault(string what, Exception e)
    {
        if (Interlocked.Exchange(ref s_faulted, 1) == 0) s_context?.LogError($"{what} failed; hair is drawn as without the mod: {e}");
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    /// <summary>
    /// <c>Mesh(path, boneCount, lodScale, shadowLevels, limbs, close, icon, partMap, worn, sway)</c>: a hair mesh is asked again with its marker.
    /// </summary>
    static bool MeshPrefix(object __instance, object?[] __args, MethodBase __originalMethod, ref object? __result) =>
        Redirect(__instance, __args, __originalMethod, ref __result, iconAt: 6, wornAt: 8, swayAt: 9);

    /// <summary><c>Prepare(path, boneCount, lodScale, shadowLevels, pixelsPerRadian, limbs, close, icon, partMap, worn, sway)</c>: the same.</summary>
    static bool PreparePrefix(object __instance, object?[] __args, MethodBase __originalMethod, ref object? __result) =>
        Redirect(__instance, __args, __originalMethod, ref __result, iconAt: 7, wornAt: 9, swayAt: 10);

    /// <summary><c>GearSway.BakeMesh(parts, in bake)</c> with a plan's marker: the plan's bake, and no backpack mass.</summary>
    static bool BakePrefix(IReadOnlyList<ModelPart> parts, object?[] __args)
    {
        try
        {
            if (__args[1] is not { } bake || !float.IsNaN((float)s_frontZ!.GetValue(bake)!)) return true;

            if (HairPlans.Get((int)(float)s_backZ!.GetValue(bake)!) is { } plan)
                HairBake.Bake(plan, parts, HairPlans.RigsBySize.GetValueOrDefault((plan.Bones, !plan.IsTail)));
        }
        catch (Exception e)
        {
            Fault("baking a hair mesh", e);
        }

        return false;
    }

    static void ItemPartsPostfix(object __instance, object asset, CharacterAppearance c, SkeletonRig rig)
    {
        try
        {
            Look(__instance, asset, c, rig);
        }
        catch (Exception e)
        {
            Fault("building a look's hair", e);
        }
    }

    static void WeatherPostfix(Meitou.Data.World.WeatherState __result) => HairWind.Weather = __result;
#pragma warning restore IDE1006

    static bool Redirect(object content, object?[] args, MethodBase original, ref object? result, int iconAt, int wornAt, int swayAt)
    {
        object? marker;
        try
        {
            marker = MarkerFor(content, args, iconAt, wornAt, swayAt);
        }
        catch (Exception e)
        {
            Fault("choosing a hair mesh", e);
            return true;
        }

        if (marker is null) return true;

        var again = (object?[])args.Clone();
        again[swayAt] = marker;
        try
        {
            result = original.Invoke(content, again);
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(e.InnerException).Throw();
        }

        return false;
    }

    /// <summary>
    /// The plan's marker for a worn hair or beard mesh, or a tailed race's body mesh, on a skeleton with a free-bone plan; null to leave the call
    /// as it is.
    /// </summary>
    static object? MarkerFor(object content, object?[] args, int iconAt, int wornAt, int swayAt)
    {
        if (args[swayAt] is not null || args[iconAt] is true || args[0] is not string path || args[1] is not int bones) return null;

        var kind = args[wornAt] is true ? HairPlans.Classify(path) : HairPlans.ClassifyBody(path);
        if (kind is not { } found) return null;

        bool head = found is HairKind.Scalp or HairKind.Beard;
        if (RigOfSize(content, bones, head) is not { } rig) return null;

        return HairPlans.For(path, bones, found, rig)?.Marker;
    }

    /// <summary>
    /// A loaded rig of <paramref name="bones"/> bones, with a head bone when <paramref name="head"/> (the body's rig is read before its meshes are
    /// prepared).
    /// </summary>
    static SkeletonRig? RigOfSize(object content, int bones, bool head)
    {
        if (HairPlans.RigsBySize.TryGetValue((bones, head), out var known)) return known;

        if (s_rigTable!(s_rigCache!(s_rigs!(content))) is not ConcurrentDictionary<string, Lazy<SkeletonRig>> table) return null;

        foreach (var lazy in table.Values)
        {
            if (lazy.IsValueCreated && lazy.Value is { } rig && rig.BoneCount == bones && (!head || rig.HeadBone >= 0))
                return HairPlans.RigsBySize.GetOrAdd((bones, head), rig);
        }

        return null;
    }

    /// <summary>A look with baked hair or a baked tail: its plans are kept with whether each flows, and its palette holds the copies.</summary>
    static void Look(object content, object asset, CharacterAppearance c, SkeletonRig rig)
    {
        Looks.Remove(asset);
        var plans = new List<HairPlan>();
        var flows = new List<bool>();
        if (s_findMesh!(content, c.BodyMesh) is { } body && HairPlans.Find(body, rig.BoneCount) is { Baked: true, IsTail: true } tail)
            Keep(plans, flows, tail, rig);

        foreach (var part in c.Parts)
        {
            if (c.Animal is not null || part is not { Mode: AttachMode.SharedSkeleton, Slot: AttachSlot.Hair or AttachSlot.Beard }) continue;

            if (s_findMesh!(content, part.Mesh) is { } path && HairPlans.Find(path, rig.BoneCount) is { Baked: true } plan)
                Keep(plans, flows, plan, rig);
        }

        if (plans.Count == 0) return;

        s_swayPalette!(asset, true);
        Looks.Add(asset, new LookHair([.. plans], [.. flows]));
    }

    /// <summary>A plan flows when the look's skeleton is the plan's and no plan kept before has its copies.</summary>
    static void Keep(List<HairPlan> plans, List<bool> flows, HairPlan plan, SkeletonRig rig)
    {
        flows.Add(plan.Matches(rig) && !plans.Any(p => p.Slots.Intersect(plan.Slots).Any()));
        plans.Add(plan);
    }
}

/// <summary>A look's baked hair meshes and whether each flows; one that does not keeps its copies on the head bone.</summary>
sealed record LookHair(HairPlan[] Plans, bool[] Flows);
