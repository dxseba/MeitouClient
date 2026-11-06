using System.Reflection;
using HarmonyLib;
using Meitou.Brutality.Ui;
using Meitou.Data.Ui;
using Meitou.Engine;
using Meitou.Modding;
using Meitou.Simulation.Items;

namespace Meitou.Brutality;

/// <summary>Filters a custom limb's native item label only during the ground-label pass.</summary>
internal sealed class StockNativeLimbLabels(FieldInfo sessionField, PropertyInfo hiddenProperty)
{
    static StockNativeLimbLabels? installed;
    readonly FieldInfo session = sessionField;
    readonly PropertyInfo hidden = hiddenProperty;

    internal static void Install(ModContext context, Type player, FieldInfo session)
    {
        if (AccessTools.Method(player, "AddItemLabels", [typeof(List<UiControl>)]) is not { } labels
            || AccessTools.Property(player, "GroundItemHidden") is not { CanRead: true, CanWrite: true } hidden) return;
        context.Harmony.Patch(labels, prefix: new HarmonyMethod(typeof(StockNativeLimbLabels), nameof(Hide)),
            finalizer: new HarmonyMethod(typeof(StockNativeLimbLabels), nameof(Restore)));
        installed = new(session, hidden);
    }

    sealed record Filter(Func<int, bool>? Previous, HashSet<int> CustomLimbs)
    {
        internal bool Contains(int item) => CustomLimbs.Contains(item) || Previous?.Invoke(item) == true;
    }

    static void Hide(object __instance, out Filter? __state) => __state = installed?.HideLabels(__instance);

    static Exception? Restore(object __instance, Exception? __exception, Filter? __state)
    {
        if (__state is not null) installed?.hidden.SetValue(__instance, __state.Previous);
        return __exception;
    }

    Filter? HideLabels(object player)
    {
        if (BrutalityUiAdapter.Preferences.ShowPartLabels || session.GetValue(player) is not WorldSession shown
            || !StockLimbRules.HasCuts(shown.World)) return null;
        var matched = new HashSet<int>();
        foreach (var item in shown.CurrentSnapshot.GroundItems)
            if (StockLimbSave.IsCustomItem(shown.World, item.Id) || item.Fall is { } fall && IsCustomLimb(item, fall)) matched.Add(item.Id);
        if (matched.Count == 0) return null;
        var filter = new Filter(hidden.GetValue(player) as Func<int, bool>, matched);
        hidden.SetValue(player, (Func<int, bool>)filter.Contains);
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
