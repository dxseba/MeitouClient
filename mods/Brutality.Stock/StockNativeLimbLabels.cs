using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Brutality.Ui;
using Meitou.Data.Ui;
using Meitou.Game.Host;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Items;

namespace Meitou.Brutality;

/// <summary>Uses the displayed limb position for native item labels and honors the part label setting.</summary>
internal sealed class StockNativeLimbLabels(FieldInfo playerField, FieldInfo sessionField, PropertyInfo hiddenProperty)
{
    static readonly ConditionalWeakTable<World, DrawnLimbs> drawn = new();
    static StockNativeLimbLabels? installed;
    readonly FieldInfo player = playerField, session = sessionField;
    readonly FieldInfo? width = AccessTools.Field(playerField.FieldType, "Width"), height = AccessTools.Field(playerField.FieldType, "Height");
    readonly PropertyInfo hidden = hiddenProperty;
    readonly ConditionalWeakTable<object, View> views = new();
    Projection? projection;

    sealed class DrawnLimbs
    {
        internal IReadOnlyDictionary<int, LimbLabel>? Items;
    }

    internal readonly record struct LimbLabel(Vector3 Position, string Name);

    sealed record Projection(MethodInfo Project, MethodInfo Ray, MethodInfo Hidden, float RadiusSquared)
    {
        internal View Bind(object player) => new(
            Project.CreateDelegate<Func<Vector3, Vector2?>>(player),
            Ray.CreateDelegate<Func<Vector2, (Vector3 Origin, Vector3 Direction)?>>(player),
            Hidden.CreateDelegate<Func<(Vector3 Origin, Vector3 Direction), float, bool>>(player));
    }

    sealed record View(Func<Vector3, Vector2?> Project, Func<Vector2, (Vector3 Origin, Vector3 Direction)?> Ray,
        Func<(Vector3 Origin, Vector3 Direction), float, bool> Hidden);

    internal static void Install(ModContext context, Type ground, FieldInfo session)
    {
        if (AccessTools.Method(ground, "AddItemLabels", [typeof(List<UiControl>)]) is not { } labels
            || AccessTools.Property(ground, "GroundItemHidden") is not { CanRead: true, CanWrite: true } hidden
            || AccessTools.Field(ground, "<player>P") is not { } player) return;
        var adapter = new StockNativeLimbLabels(player, session, hidden);
        context.Harmony.Patch(labels, prefix: new HarmonyMethod(typeof(StockNativeLimbLabels), nameof(Hide)),
            finalizer: new HarmonyMethod(typeof(StockNativeLimbLabels), nameof(Restore)));
        installed = adapter;

        var playerType = player.FieldType;
        if (AccessTools.Method(ground, "NearLabelItems", [typeof(Vector3)]) is not { } near
            || AccessTools.Method(playerType, "Project", [typeof(Vector3)]) is not { } project
            || AccessTools.Method(playerType, "Ray", [typeof(Vector2)]) is not { } ray
            || AccessTools.Method(playerType, "Hidden", [typeof((Vector3, Vector3)), typeof(float)]) is not { } occluded
            || AccessTools.TypeByName("Meitou.Game.Ui.PointerView")?.GetField("HighlightRadius")?.GetRawConstantValue() is not float radius)
            return;

        adapter.projection = new(project, ray, occluded, radius * radius);
        context.Harmony.Patch(near, postfix: new HarmonyMethod(typeof(StockNativeLimbLabels), nameof(Relocate)));
        if (AccessTools.Method(ground, "AddLabelControls") is { } controls
            && AccessTools.Field(ground, "_itemLabels")?.FieldType == typeof(List<(int, UiRect)>))
            context.Harmony.Patch(controls, prefix: new HarmonyMethod(typeof(StockNativeLimbLabels), nameof(Fit)));
#if BRUTALITY_STOCK_SAVE_PROBE
        if (LabelProbe.Enabled)
            context.Harmony.Patch(labels, prefix: new HarmonyMethod(typeof(LabelProbe), nameof(LabelProbe.UseCameraFocus)),
                finalizer: new HarmonyMethod(typeof(LabelProbe), nameof(LabelProbe.RestoreFocus)));
#endif
    }

    /// <summary>Shares the rendered roots with the UI pass on the render thread.</summary>
    internal static void Publish(World world, IReadOnlyDictionary<int, LimbLabel> items) =>
        drawn.GetOrCreateValue(world).Items = items;

    sealed record Filter(Func<int, bool>? Previous, HashSet<int> CustomLimbs)
    {
        internal bool Contains(int item) => CustomLimbs.Contains(item) || Previous?.Invoke(item) == true;
    }

    static void Hide(object __instance, out Filter? __state) => __state = installed?.HideLabels(__instance);

    static void Relocate(object __instance, Vector3 focus, List<(GroundItemSnapshot Item, float Distance, Vector2 At)> __result) =>
        installed?.RelocateLabels(__instance, focus, __result);

    static void Fit(object __instance, List<(GroundItemSnapshot Item, float Distance, Vector2 At)> near,
        List<(int Id, UiRect Rect)> placed, List<(int Id, UiRect Rect)> ____itemLabels) =>
        installed?.FitLabels(__instance, near, placed, ____itemLabels);

    static Exception? Restore(object __instance, Exception? __exception, Filter? __state)
    {
        if (__state is not null) installed?.hidden.SetValue(__instance, __state.Previous);
        return __exception;
    }

    void RelocateLabels(object ground, Vector3 focus, List<(GroundItemSnapshot Item, float Distance, Vector2 At)> near)
    {
        if (!BrutalityUiAdapter.Preferences.ShowPartLabels || projection is not { } camera
            || player.GetValue(ground) is not { } owner || session.GetValue(owner) is not WorldSession shown
            || !drawn.TryGetValue(shown.World, out var limbs) || limbs.Items is not { Count: > 0 } items) return;

        // Re-evaluate from the snapshot so limbs thrown into view also get a native, clickable item label.
        near.RemoveAll(candidate => items.ContainsKey(candidate.Item.Id));
        var view = views.GetValue(owner, camera.Bind);
        var hiddenItem = hidden.GetValue(ground) as Func<int, bool>;
        foreach (var item in shown.CurrentSnapshot.GroundItems)
        {
            if (!items.TryGetValue(item.Id, out var limb) || hiddenItem?.Invoke(item.Id) == true
                || Vector3.DistanceSquared(focus, limb.Position) > camera.RadiusSquared) continue;

            var middle = limb.Position + Vector3.UnitY * .5f;
            if (view.Project(middle) is not { } at || view.Ray(at) is not { } ray) continue;

            float distance = Vector3.Distance(ray.Origin, middle);
            if (view.Hidden(ray, distance)) continue;
            var labeledItem = item with { Name = limb.Name };
            near.Add((labeledItem, distance, at));
#if BRUTALITY_STOCK_SAVE_PROBE
            LabelProbe.Report(labeledItem);
#endif
        }
    }

    void FitLabels(object ground, List<(GroundItemSnapshot Item, float Distance, Vector2 At)> near,
        List<(int Id, UiRect Rect)> placed, List<(int Id, UiRect Rect)> hitBoxes)
    {
        if (!BrutalityUiAdapter.Preferences.ShowPartLabels || player.GetValue(ground) is not { } owner
            || session.GetValue(owner) is not WorldSession shown || !drawn.TryGetValue(shown.World, out var limbs)
            || limbs.Items is not { Count: > 0 } items || width?.GetValue(owner) is not int w || height?.GetValue(owner) is not int h
            || w <= 8 || h <= 8 || hitBoxes.Count != placed.Count) return;

        var occupied = placed.Where(label => !items.ContainsKey(label.Id)).Select(label => label.Rect).ToList();
        for (int i = 0; i < placed.Count; i++)
        {
            var (id, rect) = placed[i];
            if (!items.TryGetValue(id, out var limb)) continue;
            float wide = Math.Min(w - 8, Math.Max(rect.Width, (limb.Name.Length * 8 + 18) * Math.Max(1, h / 1080f)));
            var box = StockPartLabels.PlaceLabel(near[i].At, wide, rect.Height, w, h, occupied) ?? rect;
            occupied.Add(box);
            // The native picker and the rendered label must use the same bounds.
            placed[i] = hitBoxes[i] = (id, box);
        }
    }

#if BRUTALITY_STOCK_SAVE_PROBE
    static class LabelProbe
    {
        internal static readonly bool Enabled = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_STOCK_LABEL_PROBE") == "1";
        static readonly HashSet<int> reported = new();

        internal static void UseCameraFocus(object __instance, ref Vector3? ___HighlightFocus, out Vector3? __state)
        {
            __state = ___HighlightFocus;
            if (___HighlightFocus is null && installed is { } adapter && adapter.player.GetValue(__instance) is { } owner
                && adapter.session.GetValue(owner) is WorldSession shown)
                ___HighlightFocus = shown.Camera.Current.Target;
        }

        internal static Exception? RestoreFocus(ref Vector3? ___HighlightFocus, Exception? __exception, Vector3? __state)
        {
            ___HighlightFocus = __state;
            return __exception;
        }

        internal static void Report(in GroundItemSnapshot item)
        {
            if (Enabled && reported.Add(item.Id)) Console.WriteLine($"Brutality stock native highlight label {item.Id}: {item.Name}");
        }
    }
#endif

    Filter? HideLabels(object ground)
    {
        if (BrutalityUiAdapter.Preferences.ShowPartLabels || session.GetValue(player.GetValue(ground)) is not WorldSession shown
            || !StockLimbRules.HasCuts(shown.World)) return null;
        var matched = new HashSet<int>();
        foreach (var item in shown.CurrentSnapshot.GroundItems)
            if (StockLimbSave.IsCustomItem(shown.World, item.Id) || item.Fall is { } fall && IsCustomLimb(item, fall)) matched.Add(item.Id);
        if (matched.Count == 0) return null;
        var filter = new Filter(hidden.GetValue(ground) as Func<int, bool>, matched);
        hidden.SetValue(ground, (Func<int, bool>)filter.Contains);
        return filter;

        bool IsCustomLimb(in GroundItemSnapshot item, in FallSource fall)
        {
            if (StockLimbRules.For(shown.World, fall.Character) is not { } cuts) return false;
            foreach (var cut in cuts)
                if (cut.Bone == fall.Bone && fall.Tick >= cut.Tick && fall.Tick - cut.Tick <= 2 && cut.Records.Contains(item.Record)) return true;
            return false;
        }
    }
}
