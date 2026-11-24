using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Data.Gameplay.Combat;
using Meitou.Data.Ui;
using Meitou.Game.Rendering.Ui;
using Meitou.Game.Ui;
using Meitou.Simulation;
using Meitou.Simulation.Items;

namespace Meitou.GearCompareAlt;

/// <summary>
/// The game's interface members the port reaches by reflection (internal to the game): the tooltip runtime's <c>PlaceTooltip</c> and combat data,
/// the item tip's fields, the player interface's screens, painter and tooltips, the production windows' player. Looked up once; a member that is
/// missing leaves the feature off (logged by <see cref="GearCompareAltMod"/>).
/// </summary>
static class GameUi
{
    public static readonly Type? Tooltips = AccessTools.TypeByName("Meitou.Game.Ui.TooltipRuntime");
    public static readonly Type? ItemTip = AccessTools.TypeByName("Meitou.Game.Ui.TooltipRuntime+ItemTip");
    public static readonly Type? Production = AccessTools.TypeByName("Meitou.Game.Ui.ProductionWindows");
    public static readonly Type? Player = AccessTools.TypeByName("Meitou.Game.PlayerInterface");

    public static readonly MethodInfo? PlaceTooltip = Tooltips is null ? null : AccessTools.Method(Tooltips, "PlaceTooltip");
    public static readonly MethodInfo? ItemLines = Tooltips is null ? null : AccessTools.Method(Tooltips, "ItemLines");
    public static readonly MethodInfo? AddCraftContent = Production is null ? null : AccessTools.Method(Production, "AddCraftContent");
    public static readonly MethodInfo? MouseDown = Player is null ? null : AccessTools.Method(Player, "MouseDown");

    static readonly PropertyInfo? CombatOfProperty = Tooltips is null ? null : AccessTools.Property(Tooltips, "CombatOf");
    static readonly FieldInfo? ScreensField = Player is null ? null : AccessTools.Field(Player, "FaithfulScreens");
    static readonly FieldInfo? PainterField = Player is null ? null : AccessTools.Field(Player, "UiPainter");
    static readonly PropertyInfo? TooltipsProperty = Player is null ? null : AccessTools.Property(Player, "TooltipRuntime");
    static readonly FieldInfo? ProductionPlayer = Production is null ? null
        : AccessTools.GetDeclaredFields(Production).FirstOrDefault(f => f.FieldType == Player);

    static readonly FieldInfo? TooltipsPlayer = Tooltips is null ? null
        : AccessTools.GetDeclaredFields(Tooltips).FirstOrDefault(f => f.FieldType == Player);
    static readonly PropertyInfo? PricesOf = Player is null ? null : AccessTools.Property(Player, "PricesOf");
    static readonly MethodInfo? TipOfView = ItemTip is null ? null
        : AccessTools.GetDeclaredMethods(ItemTip).FirstOrDefault(m => m.Name == "Of" && m.GetParameters() is [{ ParameterType: var t }, ..]
            && t == typeof(InventoryItemView));

    static readonly Dictionary<string, PropertyInfo?> TipProperties = [];

    /// <summary>Whether every member the hover panel needs was found.</summary>
    public static bool HoverReady => PlaceTooltip is not null && ItemLines is not null && ItemTip is not null && CombatOfProperty is not null;

    /// <summary>Whether every member the crafting comparison needs was found.</summary>
    public static bool CraftReady => PlaceTooltip is not null && AddCraftContent is not null && MouseDown is not null && ScreensField is not null
        && PainterField is not null && TooltipsProperty is not null && ProductionPlayer is not null && CombatOfProperty is not null;

    /// <summary>A field of an item tip, by name.</summary>
    public static T? Tip<T>(object tip, string name)
    {
        if (!TipProperties.TryGetValue(name, out var property)) TipProperties[name] = property = AccessTools.Property(ItemTip!, name);
        return property?.GetValue(tip) is T value ? value : default;
    }

    /// <summary>The combat data the tooltips print from.</summary>
    public static CombatDatabase? Combat(object tooltips) => (CombatOfProperty?.GetValue(tooltips) as Func<CombatDatabase?>)?.Invoke();

    /// <summary>Lays out <paramref name="lines"/> as a tooltip beside <paramref name="anchor"/>, under its own control names.</summary>
    public static void Place(object tooltips, List<UiControl> controls, UiPainter painter, IReadOnlyList<TooltipLine> lines, UiRect anchor,
        string prefix) =>
        PlaceTooltip!.Invoke(tooltips, [controls, painter, lines, TooltipKind.Inventory, anchor, prefix, null, null]);

    /// <summary>The faithful screen control under <paramref name="p"/>.</summary>
    public static (string Screen, UiControl Control)? HitTest(object player, Vector2 p) =>
        (ScreensField?.GetValue(player) as MyGuiScreenHost)?.HitTest(p);

    public static object? PlayerOf(object production) => ProductionPlayer?.GetValue(production);

    public static UiPainter? Painter(object player) => PainterField?.GetValue(player) as UiPainter;

    public static object? TooltipsOf(object player) => TooltipsProperty?.GetValue(player);

    /// <summary>The player interface the tooltips belong to.</summary>
    public static object? PlayerOfTooltips(object tooltips) => TooltipsPlayer?.GetValue(tooltips);

    /// <summary>The value of one unit of the tip's item as the tooltip prices it; null when the game does not price it or a member is missing.
    /// </summary>
    public static int? Price(object? player, object? tip)
    {
        if (player is null || tip is null || PricesOf?.GetValue(player) is not Delegate prices) return null;

        return prices.DynamicInvoke(tip) is ValueTuple<int, int, int?> p ? p.Item1 : null;
    }

    /// <summary>The tip the game builds for an inventory item (its own tooltip's).</summary>
    public static object? TipOf(InventoryItemView view) => TipOfView?.Invoke(null, [view, null, null]);

    /// <summary>Who holds the tip's item (null: a trader's stock, a preview).</summary>
    public static ItemOwner? Owner(object tip) => Tip<ItemOwner?>(tip, "Owner");
}

/// <summary>What the frame callback last saw, for the interface patches on the same thread.</summary>
static class FrameState
{
    public static WorldSnapshot? Snapshot;
    public static IReadOnlyList<CharacterId> Selected = [];
    public static CharacterId? Shown;
    public static bool Alt, Ctrl, Shift, OverviewOpen;

    public static CharacterSnapshot? Character(CharacterId id) => Snapshot?.Characters.FirstOrDefault(c => c.Id == id);
}
