using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Items;

namespace Meitou.Disarm;

/// <summary>
/// The Kick menu on a loose ground item. The game has no menu-verb slot for ground items, so a Harmony prefix on <c>GroundItemPicks.GroundItemClick</c>
/// (a right press on an item with a selection) opens the game's own hold menu instead: Pick up (also the quick click) and Kick.
/// </summary>
static class KickMenu
{
    static ModContext? s_context;
    static Type s_picks = null!, s_window = null!;
    static FieldInfo s_player = null!;
    static MethodInfo s_pick = null!, s_menu = null!, s_pressOn = null!, s_selected = null!, s_say = null!;
    static PropertyInfo s_windowProperty = null!;
    static FieldInfo s_hold = null!;

    public static void Install(ModContext context)
    {
        s_context = context;
        s_picks = Required(AccessTools.TypeByName("Meitou.Game.Ui.GroundItemPicks"), "GroundItemPicks");
        s_window = Required(AccessTools.TypeByName("Meitou.Game.Ui.ContextMenuWindow"), "ContextMenuWindow");
        var player = Required(AccessTools.TypeByName("Meitou.Game.PlayerInterface"), "PlayerInterface");

        s_player = Required(AccessTools.GetDeclaredFields(s_picks).FirstOrDefault(f => f.FieldType == player), "GroundItemPicks.player");
        s_pick = Required(AccessTools.Method(s_picks, "PickGroundItem"), "PickGroundItem");
        s_menu = Required(AccessTools.Method(s_window, "Menu"), "ContextMenuWindow.Menu");
        s_pressOn = Required(AccessTools.Method(s_window, "ContextPressOn"), "ContextPressOn");
        s_hold = Required(AccessTools.Field(s_window, "ContextHoldSeconds"), "ContextHoldSeconds");
        s_windowProperty = Required(AccessTools.Property(player, "ContextMenuWindow"), "PlayerInterface.ContextMenuWindow");
        s_selected = Required(AccessTools.Method(player, "SelectedIds"), "SelectedIds");
        s_say = Required(AccessTools.Method(player, "Say"), "Say");

        var click = Required(AccessTools.Method(s_picks, "GroundItemClick"), "GroundItemClick");
        context.Harmony.Patch(click, prefix: new HarmonyMethod(typeof(KickMenu), nameof(BeforeGroundItemClick)));
    }

    static T Required<T>(T? found, string name) where T : class => found ?? throw new MissingMemberException("Disarm kick menu: " + name);

    #pragma warning disable IDE1006 // Harmony binds __result / __instance by name
    static bool BeforeGroundItemClick(object __instance, Vector2 p, ref bool __result)
    #pragma warning restore IDE1006
    {
        if (!DisarmMod.Settings.Enabled || !DisarmMod.Settings.Kick) return true;

        var player = s_player.GetValue(__instance)!;
        var selected = (IReadOnlyList<CharacterId>)s_selected.Invoke(player, null)!;
        if (selected.Count == 0 || s_pick.Invoke(__instance, [p]) is not int item || Snapshot(player, item) is not { } ground) return true;

        __result = Open(player, p, ground, selected);
        return !__result;
    }

    static GroundItemSnapshot? Snapshot(object player, int item)
    {
        var items = Traverse.Create(player).Field("Session").Property("CurrentSnapshot").Property("GroundItems").GetValue<IReadOnlyList<GroundItemSnapshot>>();
        foreach (var g in items ?? [])
        {
            if (g.Id == item) return g;
        }

        return null;
    }

    /// <summary>Starts the game's hold timer on the two-row menu; the quick release picks the item up, as without the mod.</summary>
    static bool Open(object player, Vector2 p, GroundItemSnapshot ground, IReadOnlyList<CharacterId> selected)
    {
        bool alt = Traverse.Create(player).Field("AltHeld").GetValue<bool>();
        Action pickUp = () => Say(player, new PickUpItemOrder([], ground.Id) { Tick = Tick, Alt = alt });
        Action kick = () =>
        {
            Say(player, new MoveOrder([selected[0]], ground.Position) { Tick = Tick });
            Say(player, new KickOrder(selected[0], ground.Id) { Tick = Tick });
        };

        var verbs = new List<(string Caption, Action Give)> { ("Pick up", pickUp), ("Kick", kick) };
        var window = s_windowProperty.GetValue(player)!;
        return (bool)s_pressOn.Invoke(window, [p, MenuFactory(window, ground.Name, verbs), pickUp, s_hold.GetValue(null)!])!;
    }

    static long Tick => s_context?.Tick ?? 0;

    static void Say(object player, SimCommand command) => s_say.Invoke(player, [command]);

    /// <summary>A <c>Func&lt;Vector2, ContextMenu?&gt;</c> over the window's own <c>Menu</c>; the record type is internal, so it is built as an expression.</summary>
    static Delegate MenuFactory(object window, string title, List<(string Caption, Action Give)> verbs)
    {
        var at = Expression.Parameter(typeof(Vector2), "at");
        var call = Expression.Call(Expression.Constant(window), s_menu, Expression.Constant(title), at, Expression.Constant(verbs));
        var funcType = typeof(Func<,>).MakeGenericType(typeof(Vector2), s_menu.ReturnType);
        return Expression.Lambda(funcType, call, at).Compile();
    }
}
