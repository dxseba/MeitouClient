using Meitou.Data;
using Meitou.Data.Gameplay.Combat;
using Meitou.Data.Ui;
using Meitou.Game.Rendering.Ui;
using Meitou.Game.Ui;
using Meitou.Simulation;
using Meitou.Simulation.Items;

namespace Meitou.GearCompareAlt;

/// <summary>
/// The panel beside an item's tooltip while the hold key is down (README "What it does"). A postfix on the tooltip runtime's <c>ItemLines</c>
/// notes which item the tooltip about to be placed shows; a postfix on its <c>PlaceTooltip</c>, when it places that list, lays out the comparison
/// as a second tooltip beside the first's panel. Host thread only; a failure logs once and leaves the game's tooltip alone.
/// </summary>
static class HoverPanel
{
    static GearCompareAltMod? s_mod;
    static object? s_tip;
    static IReadOnlyList<TooltipLine>? s_tipLines;
    static (object Tip, InventoryItemView? Worn, string Slot, int Settings)? s_key;
    static IReadOnlyList<TooltipLine>? s_lines;
    static bool s_placing;
    static int s_failed;

    public static void Use(GearCompareAltMod mod) => s_mod = mod;

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void ItemLinesPostfix(object t, IReadOnlyList<TooltipLine> __result) => (s_tip, s_tipLines) = (t, __result);

    static void PlacePostfix(object __instance, List<UiControl> controls, UiPainter painter, IReadOnlyList<TooltipLine> lines, string prefix)
    {
        if (s_placing || prefix != "tooltip" || !ReferenceEquals(lines, s_tipLines) || s_tip is not { } tip || s_mod is not { } mod) return;

        s_placing = true;
        try
        {
            if (mod.Settings.Enabled && mod.HoldDown() && Panel(mod, __instance, tip) is { } panel && TooltipRect(controls) is { } rect)
                GameUi.Place(__instance, controls, painter, panel, rect, "gearcompare");
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            if (Interlocked.Exchange(ref s_failed, 1) == 0) mod.LogError($"comparison panel failed ({e.GetType().Name}: {e.Message})");
        }
        finally
        {
            s_placing = false;
        }
    }
#pragma warning restore IDE1006

    /// <summary>The game's tooltip panel just placed (its plate or panel control, the last of them).</summary>
    static UiRect? TooltipRect(List<UiControl> controls)
    {
        for (int i = controls.Count - 1; i >= 0; i--)
        {
            if (controls[i].Name is "tooltip-plate" or "tooltip-panel") return controls[i].Bounds;
        }

        return null;
    }

    /// <summary>The panel's lines for the tip's item; null for an item that is not gear, worn itself, or with nothing to compare against.</summary>
    static IReadOnlyList<TooltipLine>? Panel(GearCompareAltMod mod, object tooltips, object tip)
    {
        string record = GameUi.Tip<string>(tip, "Record") ?? "";
        var kind = GearReader.KindOf(mod.Data.Find(record));
        if (kind is not (GearKind.Armour or GearKind.Weapon or GearKind.Crossbow) || GameUi.Combat(tooltips) is not { } combat) return null;

        var owner = GameUi.Owner(tip);
        var wearer = Wearer(owner);
        if (wearer?.ItemGrid is not { } grid) return null;

        var (worn, slot) = WornSlot.For(mod.Data, combat, kind.Value, record, grid.Items, mod.Tr);
        if (slot is null) return null;
        if (worn is not null && owner is { Holder: ItemHolder.Character } o && o.Character == wearer.Id
            && worn.Index == GameUi.Tip<int>(tip, "Index"))
            return null;   // the hovered item is the worn one

        if (worn is null && !mod.Settings.CompareEmptySlot) return null;

        int settingsKey = HashCode.Combine(mod.Settings.ShowTotal, mod.Settings.TotalPerProperty, mod.Settings.TotalPerKg,
            mod.Settings.ColourBetter, mod.Settings.ColourWorse);
        var key = (tip, worn, slot, settingsKey);
        if (s_key is { } last && Equals(last, key) && s_lines is not null) return s_lines;

        s_key = key;
        s_lines = Build(mod, combat, GameUi.PlayerOfTooltips(tooltips), tip, worn, slot, wearer);
        return s_lines;
    }

    static IReadOnlyList<TooltipLine>? Build(GearCompareAltMod mod, CombatDatabase combat, object? player, object tip, InventoryItemView? worn,
        string slot,
        CharacterSnapshot wearer)
    {
        var item = ItemOf(tip) with { Price = GameUi.Price(player, tip) };
        if (GearReader.Read(mod.Data, combat, item) is not { } fresh) return null;

        var wornItem = worn is null ? (GearItem?)null : WornSlot.ItemOf(worn) with { Price = GameUi.Price(player, GameUi.TipOf(worn)) };
        var wornStats = wornItem is { } w ? GearReader.Read(mod.Data, combat, w) : null;
        var comparison = Comparison.Of(wornStats, fresh, mod.Tr);
        string title = $"{mod.Tr("Worn")} ({slot}): {worn?.Name ?? mod.Tr("nothing")}";
        if (mod.Settings.Debug)
            mod.Log($"hovered '{item.Name}' for {wearer.Name}, worn '{worn?.Name ?? "nothing"}' ({slot}): {comparison.Rows.Count} rows");

        return PanelLines.Build(title, comparison, mod.Settings, mod.Tr);
    }

    static GearItem ItemOf(object tip)
    {
        int quantity = Math.Max(GameUi.Tip<int>(tip, "Quantity"), 1);
        return new GearItem(GameUi.Tip<string>(tip, "Record") ?? "", GameUi.Tip<string>(tip, "Material") ?? "",
            GameUi.Tip<string>(tip, "Company") ?? "", GameUi.Tip<int>(tip, "Level"), GameUi.Tip<float>(tip, "Weight") / quantity,
            GameUi.Tip<string>(tip, "Name") ?? "");
    }

    /// <summary>Whose gear: the player character holding the item, else the HUD's selected one.</summary>
    static CharacterSnapshot? Wearer(ItemOwner? owner)
    {
        if (owner is { Holder: ItemHolder.Character } o && FrameState.Character(o.Character) is { IsPlayer: true } holder) return holder;

        var id = FrameState.Shown ?? (FrameState.Selected.Count > 0 ? FrameState.Selected[0] : (CharacterId?)null);
        return id is { } who ? FrameState.Character(who) : null;
    }
}
