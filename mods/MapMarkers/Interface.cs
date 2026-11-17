using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Game.Ui;

namespace Meitou.MapMarkers;

/// <summary>
/// Reflection into the game's internal interface types: <c>PlayerInterface</c> (its faithful screens, UI scale and pointer) and the
/// <c>WorldMap</c> and <c>ScreenRouter</c> that hold one.
/// </summary>
static class Interface
{
    public static Type Player { get; private set; } = null!;
    public static Type WorldMap { get; private set; } = null!;
    public static Type Router { get; private set; } = null!;

    static FieldInfo s_screens = null!, s_mouse = null!, s_mapPlayer = null!, s_routerPlayer = null!, s_mapShown = null!;
    static PropertyInfo s_uiScale = null!;
    static MethodInfo s_focusEdit = null!;

    public static void Resolve()
    {
        Player = Required(AccessTools.TypeByName("Meitou.Game.PlayerInterface"), "PlayerInterface");
        WorldMap = Required(AccessTools.TypeByName("Meitou.Game.Ui.WorldMap"), "WorldMap");
        Router = Required(AccessTools.TypeByName("Meitou.Game.Ui.ScreenRouter"), "ScreenRouter");

        s_screens = Required(AccessTools.Field(Player, "FaithfulScreens"), "PlayerInterface.FaithfulScreens");
        s_mouse = Required(AccessTools.Field(Player, "Mouse"), "PlayerInterface.Mouse");
        s_uiScale = Required(AccessTools.Property(Player, "UiScale"), "PlayerInterface.UiScale");
        s_mapPlayer = Required(PlayerField(WorldMap), "WorldMap.player");
        s_routerPlayer = Required(PlayerField(Router), "ScreenRouter.player");
        s_mapShown = Required(AccessTools.Field(WorldMap, "MapShown"), "WorldMap.MapShown");
        s_focusEdit = Required(AccessTools.Method(typeof(MyGuiScreenHost), "FocusEdit"), "MyGuiScreenHost.FocusEdit");
    }

    static T Required<T>(T? found, string name) where T : class => found ?? throw new MissingMemberException("Map markers: " + name);

    /// <summary>The primary-constructor field a type keeps its <c>PlayerInterface</c> in.</summary>
    static FieldInfo? PlayerField(Type type) => AccessTools.GetDeclaredFields(type).FirstOrDefault(f => f.FieldType == Player);

    public static MyGuiScreenHost Screens(object player) => (MyGuiScreenHost)s_screens.GetValue(player)!;

    public static float UiScale(object player) => (float)s_uiScale.GetValue(player)!;

    public static Vector2 Mouse(object player) => (Vector2)s_mouse.GetValue(player)!;

    public static void SetMouse(object player, Vector2 p) => s_mouse.SetValue(player, p);

    public static object PlayerOfMap(object worldMap) => s_mapPlayer.GetValue(worldMap)!;

    public static object PlayerOfRouter(object router) => s_routerPlayer.GetValue(router)!;

    /// <summary>Whether the map tab showed at the map's last once-a-frame update.</summary>
    public static bool MapShown(object worldMap) => (bool)s_mapShown.GetValue(worldMap)!;

    /// <summary>Puts the typing focus on an edit box the mod added (the host's own focus step; it has no public one).</summary>
    public static void Focus(MyGuiScreenHost screens, string screen, Data.Ui.UiControl box) => s_focusEdit.Invoke(screens, [screen, box]);
}
