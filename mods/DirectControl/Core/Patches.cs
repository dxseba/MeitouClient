using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Game.Host.Cameras;
using Meitou.Game.Host.Input;
using Meitou.Modding;

namespace Meitou.DirectControl;

/// <summary>
/// The Harmony patches on Meitou's host (README "Harmony"): the control tick's keys before the strategy camera reads them, the drawn camera
/// after the follow camera placed it, the held pointer's mouse look, the Action RPG scheme's mouse buttons, and the hidden head. Each body is
/// wrapped: a throw is logged once and the game's own behaviour runs.
/// </summary>
static class Patches
{
    /// <summary>
    /// <c>ActionState.Set</c> (internal): how the taken camera keys are let go for the tick.
    /// </summary>
    public static Action<ActionState, InputAction, bool, bool, bool>? SetAction { get; private set; }

    static readonly HashSet<MouseButton> Eaten = [];
    static int s_faulted;

    public static void Install(ModContext context, bool actionScheme)
    {
        var set = AccessTools.Method(typeof(ActionState), "Set");
        if (set is not null) SetAction = AccessTools.MethodDelegate<Action<ActionState, InputAction, bool, bool, bool>>(set);

        Patch(context, typeof(CameraRig), "Update", nameof(ControlTickPrefix));
        Patch(context, "Meitou.Game.Scenarios.SandboxScenario", "FollowCamera", postfix: nameof(FollowCameraPostfix));
        Patch(context, "Meitou.Game.Host.GameLoop", "OnMouseMove", nameof(MouseMovePrefix));
        Patch(context, "Meitou.Game.Host.MetalGameLoop", "OnMouseMove", nameof(MouseMovePrefix));
        Patch(context, "Meitou.Game.Host.WorldSync.InteriorView", "UpdateInteriorView", postfix: nameof(InteriorPostfix));
        if (actionScheme)
        {
            Patch(context, "Meitou.Game.Host.GameLoop", "PointerDown", nameof(PointerDownPrefix));
            Patch(context, "Meitou.Game.Host.GameLoop", "PointerUp", nameof(PointerUpPrefix));
        }

        if (!HeadHide.Ready) context.LogError("CharacterAppearance members not found: the head stays drawn in first person");
        else PatchHead(context);
    }

    static void PatchHead(ModContext context)
    {
        Patch(context, "Meitou.Game.GameHost", "Drawn", postfix: nameof(HeadHide.DrawnPostfix), owner: typeof(HeadHide));
        Patch(context, "Meitou.Game.Effects.Decapitation", "CutHead", nameof(HeadHide.CutHeadPrefix), owner: typeof(HeadHide));
        Patch(context, "Meitou.Game.Rendering.Characters.CharacterContent", "BodyMaterial", postfix: nameof(HeadHide.MaterialPostfix),
            owner: typeof(HeadHide));
        Patch(context, "Meitou.Game.Rendering.Characters.CharacterContent", "ItemMaterial", postfix: nameof(HeadHide.MaterialPostfix),
            owner: typeof(HeadHide));
    }

    static void Patch(ModContext context, string type, string method, string? prefix = null, string? postfix = null, Type? owner = null)
    {
        if (AccessTools.TypeByName(type) is { } t) Patch(context, t, method, prefix, postfix, owner);
        else context.LogError($"{type} not found: {method} is not patched");
    }

    static void Patch(ModContext context, Type type, string method, string? prefix = null, string? postfix = null, Type? owner = null)
    {
        var target = AccessTools.Method(type, method);
        if (target is null)
        {
            context.LogError($"{type.FullName}.{method} not found: not patched");
            return;
        }

        owner ??= typeof(Patches);
        context.Harmony.Patch(target, prefix: prefix is null ? null : new HarmonyMethod(owner, prefix),
            postfix: postfix is null ? null : new HarmonyMethod(owner, postfix));
    }

    /// <summary>
    /// Prefix of <c>CameraRig.Update(float, ActionState)</c>: direct control reads and takes its keys.
    /// </summary>
    static void ControlTickPrefix(ActionState actions)
    {
        try
        {
            Pilot.Current?.ControlTick(actions);
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    /// <summary>
    /// Postfix of <c>SandboxScenario.FollowCamera()</c>: the first-person or third-person camera over the follow camera.
    /// </summary>
    static void FollowCameraPostfix()
    {
        try
        {
            if (Pilot.Current is { } pilot && HostAccess.Camera is { } camera) pilot.ApplyView(camera);
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    /// <summary>
    /// Prefix of <c>GameLoop.OnMouseMove(IMouse, Vector2)</c>: while the view is held, each move adds to the tick's mouse movement and the pointer is
    /// put back where it was (the host's own pointer hold).
    /// </summary>
#pragma warning disable IDE1006 // Harmony's injected parameter names
    static bool MouseMovePrefix(object __instance, object m, Vector2 p)
    {
        try
        {
            if (Pilot.Current is not { LookActive: true } pilot || HostAccess.Session is not { } session) return true;

            var field = AccessTools.Field(__instance.GetType(), "_lastMouse");
            if (field is null) return true;

            var centre = WindowCentre(__instance) ?? field.GetValue(__instance) as Vector2? ?? p;
            if (field.GetValue(__instance) is Vector2 last) session.Input.AddMouseDelta(p.X - last.X, p.Y - last.Y);

            HostAccess.HoldPointer(m, centre);
            field.SetValue(__instance, centre);
            if (Pixels(__instance, centre) is { } ui) pilot.Pinned(ui);

            return false;
        }
        catch (Exception e)
        {
            Fault(e);
            return true;
        }
    }

    /// <summary>
    /// The middle of the game window, in window units (the loop's <c>_window.Size</c>).
    /// </summary>
    static Vector2? WindowCentre(object loop)
    {
        object? window = AccessTools.Field(loop.GetType(), "_window")?.GetValue(loop);
        object? size = window?.GetType().GetProperty("Size")?.GetValue(window);
        if (size is null) return null;

        var type = size.GetType();
        float x = Convert.ToSingle(type.GetField("X")?.GetValue(size) ?? 0), y = Convert.ToSingle(type.GetField("Y")?.GetValue(size) ?? 0);
        return x > 0 && y > 0 ? new Vector2(x / 2, y / 2) : null;
    }

    /// <summary>
    /// A window point as the interface's pixels (the loop's own <c>Pixels</c>).
    /// </summary>
    static Vector2? Pixels(object loop, Vector2 at) =>
        AccessTools.Method(loop.GetType(), "Pixels", [typeof(Vector2)])?.Invoke(loop, [at]) as Vector2?;

#pragma warning restore IDE1006

    /// <summary>
    /// Prefix of <c>GameLoop.PointerDown</c> (Action RPG): while the view is held a button goes to the control tick only, not to the world.
    /// </summary>
    static bool PointerDownPrefix(MouseButton? physical)
    {
        try
        {
            if (Pilot.Current is not { LookActive: true } || physical is not { } button || HostAccess.Session is not { } session) return true;

            session.Input.SetMouseButton(button, true);
            Eaten.Add(button);
            return false;
        }
        catch (Exception e)
        {
            Fault(e);
            return true;
        }
    }

    /// <summary>
    /// Prefix of <c>GameLoop.PointerUp</c>: the release of a button the held view took.
    /// </summary>
    static bool PointerUpPrefix(MouseButton? physical)
    {
        try
        {
            if (physical is not { } button || !Eaten.Remove(button) || HostAccess.Session is not { } session) return true;

            session.Input.SetMouseButton(button, false);
            return false;
        }
        catch (Exception e)
        {
            Fault(e);
            return true;
        }
    }

    /// <summary>
    /// Postfix of <c>InteriorView.UpdateInteriorView()</c>: in first person every building in the interior view shows all its floors (the storeys
    /// above and below the character, the roof over it) and no character indoors is hidden for standing on another floor.
    /// </summary>
#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void InteriorPostfix(object __instance)
    {
        try
        {
            if (Pilot.Current is not { RevealFloors: true }) return;

            if (AccessTools.Field(__instance.GetType(), "InteriorFloors")?.GetValue(__instance) is Dictionary<string, int> floors)
            {
                foreach (string key in floors.Keys.ToList()) floors[key] = 3;
            }

            var hiddenField = AccessTools.Field(__instance.GetType(), "HiddenIndoors");
            if (hiddenField?.GetValue(__instance) is HashSet<Simulation.CharacterId> hidden) hidden.Clear();
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }
#pragma warning restore IDE1006

    static void Fault(Exception e)
    {
        if (Interlocked.Exchange(ref s_faulted, 1) == 0) Pilot.Current?.LogError($"direct control patch failed, the game's own input kept: {e}");
    }
}
