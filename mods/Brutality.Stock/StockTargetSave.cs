using Meitou.Simulation.Player;
using System.Reflection;
using HarmonyLib;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Ai;
using Meitou.Data.Save;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Saving;

namespace Meitou.Brutality;

/// <summary>Extends stock order save/restore for an exact downed-enemy aim. Native capture still writes the actual target handle.</summary>
internal static class StockTargetSave
{
    // This row exists only on disk and during Restore. Without the mod the native reader skips it instead of attacking a different part.
    const TaskType SavedRow = (TaskType)(-0x42544149);
    const string SitePrefix = "dxseba.brutality.aim/";
    const int MaxPart = 1023, MaxTemplateLength = 512;
    /// <summary>The converted integrated row carries a stable part template but no part index.</summary>
    const int LegacyPartKind = -1, LegacyPartIndex = LegacyPartKind - 1;
    static readonly MethodInfo queueAdd = AccessTools.Method(typeof(OrderQueue), "Add", [typeof(OrderEntry)]);
    sealed record Scope(World World, Dictionary<int, StockTargetSystem.Focus> Details);
    [ThreadStatic] static Scope? active;

    internal static bool Ready { get; private set; }

    internal static void Install(ModContext context)
    {
        var methods = new (MethodInfo? Original, string? Prefix, string? Postfix, string? Finalizer)[]
        {
            (AccessTools.Method(typeof(OrderSave), nameof(OrderSave.Write)), null, nameof(Written), null),
            (AccessTools.Method(typeof(OrderSave), nameof(OrderSave.Read)), null, nameof(ReadLegacy), null),
            (AccessTools.Method(typeof(OrderSave), "Restore"), nameof(BeforeRestore), nameof(Restored), nameof(AfterRestore)),
            (AccessTools.Method(typeof(World), "OrderOwnerOf", [typeof(TaskType)]), nameof(ResolveOwner), null, null),
        };
        if (queueAdd is null || methods.Any(method => method.Original is null))
        { context.Log("Brutality aimed-order persistence unavailable on this host"); return; }

        var installed = new List<(MethodInfo Original, MethodInfo Patch)>();
        try
        {
            foreach (var (found, prefix, postfix, finalizer) in methods)
            {
                var original = found!;
                HarmonyMethod? Patch(string? name)
                {
                    if (name is null) return null;
                    var patch = AccessTools.Method(typeof(StockTargetSave), name);
                    installed.Add((original, patch));
                    return new(patch);
                }
                context.Harmony.Patch(original, prefix: Patch(prefix), postfix: Patch(postfix), finalizer: Patch(finalizer));
            }
            Ready = true;
        }
        catch (Exception error)
        {
            foreach (var (original, patch) in installed.AsEnumerable().Reverse()) context.Harmony.Unpatch(original, patch);
            context.Log($"Brutality aimed-order persistence unavailable: {error.GetBaseException().Message}");
        }
    }

    static void Written(FcsRecord ai, CharacterCold cold)
    {
        // A not-yet-restored queue already carries these row/point/handle/site values through the native writer.
        if (!Ready || cold.SavedOrders is not null) return;
        int index = 0;
        foreach (var entry in cold.OrderQueue)
        {
            int kind = entry.Row == TaskType.None ? -1 : entry.Owner.SaveKind(entry);
            if (kind < 0) continue;
            if (entry.Owner is StockTargetSystem && entry.Detail is StockTargetSystem.Focus focus)
            {
                ai.Ints["tjob" + index] = (int)SavedRow;
                ai.Strings["meitou tjob" + index] = SitePrefix + (focus.Template ?? "");
            }
            index++;
        }
    }

    /// <summary>Upgrades integrated exact aims before a stock restore or a save of the still-pending queue.</summary>
    static void ReadLegacy(FcsRecord ai, CharacterCold cold)
    {
        if (!Ready || cold.SavedOrders is not { } orders) return;
        SavedOrder[]? rewritten = null;
        for (int i = 0; i < orders.Count; i++)
        {
            var row = orders[i];
            if (row.Row != TaskType.FocusedMeleeAttack || row.Kind != 1
                || !ai.Strings.TryGetValue("meitou tjobD" + i, out var template)) continue;

            bool valid = !row.Lost && row.Site is null && row.Hand.RecordType == FcsRecordType.Character
                && Hand.IsComplete(ai, "tjobT" + i) && template.Length is > 0 and <= MaxTemplateLength;
            rewritten ??= orders.ToArray();
            rewritten[i] = row with { Row = SavedRow, Kind = LegacyPartKind, Site = valid ? SitePrefix + template : null, Lost = !valid };
        }

        if (rewritten is not null) cold.SavedOrders = rewritten;
    }

    static void BeforeRestore(World world, CharacterCold cold, out Scope? __state)
    {
        __state = active;
        active = null;
        if (cold.OrderQueue.Count != 0 || cold.SavedOrders is not { } orders || !orders.Any(row => row.Row == SavedRow)) return;
        var details = new Dictionary<int, StockTargetSystem.Focus>();
        var rewritten = orders.ToArray();
        for (int i = 0; i < rewritten.Length; i++)
        {
            var row = rewritten[i];
            if (row.Row != SavedRow) continue;
            if (row.Lost || row.Hand.RecordType != FcsRecordType.Character || row.Kind is < LegacyPartKind or > MaxPart + 1
                || row.Site is not { } site || !site.StartsWith(SitePrefix, StringComparison.Ordinal)
                || site.Length > SitePrefix.Length + MaxTemplateLength || (site.Length == SitePrefix.Length) != (row.Kind == 0))
            {
                // Keep the native row indices intact for other scoped save adapters, even when this order is invalid.
                rewritten[i] = row with { Lost = true };
                continue;
            }
            details.Add(i, new(row.Kind - 1, row.Kind == 0 ? null : site[SitePrefix.Length..]));
            rewritten[i] = row with { Site = null, Kind = i };
        }
        cold.SavedOrders = rewritten;
        active = new(world, details);
    }

    static bool ResolveOwner(World __instance, TaskType row, ref IOrderOwner? __result)
    {
        if (row != SavedRow || active is not { } scope || !ReferenceEquals(scope.World, __instance)) return true;
        __result = __instance.Systems.OfType<StockTargetSystem>().FirstOrDefault();
        return false;
    }

    internal static StockTargetSystem.Focus? FocusOf(World world, TaskType row, int kind) =>
        row == SavedRow && active is { } scope && ReferenceEquals(scope.World, world) ? scope.Details.GetValueOrDefault(kind) : null;

    static void Restored(World world, CharacterCold cold)
    {
        if (active is not { } scope || !ReferenceEquals(scope.World, world) || !cold.OrderQueue.Any(row => row.Row == SavedRow)) return;
        var rows = cold.OrderQueue.ToArray();
        cold.OrderQueue.Clear();
        foreach (var row in rows)
        {
            if (row.Row != SavedRow)
            {
                queueAdd.Invoke(cold.OrderQueue, [row]);
                continue;
            }

            if (row is not { Owner: StockTargetSystem, Detail: StockTargetSystem.Focus focus }) continue;
            var selected = focus.Part == LegacyPartIndex ? ResolveLegacyFocus(world, row.Target, focus.Template) : focus;
            if (selected is not null)
                queueAdd.Invoke(cold.OrderQueue, [new OrderEntry(TaskType.FocusedMeleeAttack, row.Owner)
                { Target = row.Target, Point = row.Point, Detail = selected }]);
        }
    }

    /// <summary>Uses an older aim only when its target still has one part with that exact template.</summary>
    static StockTargetSystem.Focus? ResolveLegacyFocus(World world, CharacterId target, string? template)
    {
        var table = world.Characters;
        if (template is null || !table.TryResolveNext(target, out int slot) || table.Cold(slot)?.Medical is not { } medical) return null;
        int found = -1;
        for (int i = 0; i < medical.Parts.Count; i++)
        {
            if (medical.Parts[i].Template.StringId != template) continue;
            if (found >= 0) return null;
            found = i;
        }

        return found is >= 0 and <= MaxPart ? new(found, template) : null;
    }

    static Exception? AfterRestore(Exception? __exception, Scope? __state)
    { active = __state; return __exception; }
}
