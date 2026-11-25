using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.ForceAwakening;

/// <summary>
/// The click-a-target cast mode: while a skill is armed (<see cref="ForceUi.Armed"/>), the next left press in the world is the skill's, not a
/// selection drag. Harmony: a prefix on <c>Meitou.Game.PlayerInterface.LeftDown</c> (the left press past the HUD's own handlers). A press over the
/// interface (the bar itself included) goes on as before. The target is the character under the pointer (<c>ModFrame.Hovered</c>), else the ground
/// point along the pointer's ray (<c>PlayerInterface.Ray</c>, then <c>WorldPicking.MoveHit</c>, as the debug world click). A failure logs once and
/// leaves the press to the game.
/// </summary>
static class ClickPatch
{
    static ForceUi? s_ui;
    static ModContext? s_context;
    static MethodInfo? s_overHud, s_ray, s_moveHit;
    static PropertyInfo? s_picking;
    static int s_failed;

    public static void Install(ModContext context, ForceUi ui)
    {
        s_context = context;
        s_ui = ui;
        var type = AccessTools.TypeByName("Meitou.Game.PlayerInterface");
        var leftDown = type is null ? null : AccessTools.Method(type, "LeftDown", [typeof(Vector2)]);
        s_overHud = type is null ? null : AccessTools.Method(type, "OverHud", [typeof(Vector2)]);
        s_ray = type is null ? null : AccessTools.Method(type, "Ray", [typeof(Vector2)]);
        s_picking = type is null ? null : AccessTools.Property(type, "Picking");
        s_moveHit = s_picking is null ? null : AccessTools.Method(s_picking.PropertyType, "MoveHit", [typeof(Vector3), typeof(Vector3)]);
        if (leftDown is null || s_overHud is null)
        {
            context.LogError("Meitou.Game.PlayerInterface.LeftDown or OverHud not found: target skills cannot be clicked (the port needs an update)");
            return;
        }

        context.Harmony.Patch(leftDown, prefix: new HarmonyMethod(typeof(ClickPatch), nameof(Prefix)));
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static bool Prefix(object __instance, Vector2 p, ref bool __result)
    {
        try
        {
            if (s_ui is not { Armed: true } ui || s_overHud!.Invoke(__instance, [p]) is true) return true;

            var target = ui.Hovered;
            Vector3? point = target is null ? Ground(__instance, p) : null;
            ui.Click(target, point);
            __result = true;
            return false;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            if (Interlocked.Exchange(ref s_failed, 1) == 0) s_context?.LogError($"target click failed ({e.GetType().Name}: {e.Message})");
            return true;
        }
    }
#pragma warning restore IDE1006

    static Vector3? Ground(object player, Vector2 p)
    {
        if (s_ray?.Invoke(player, [p]) is not ValueTuple<Vector3, Vector3> ray || s_picking?.GetValue(player) is not { } picking) return null;

        return s_moveHit?.Invoke(picking, [ray.Item1, ray.Item2]) as Vector3?;
    }
}
