using System.Numerics;
using HarmonyLib;
using Meitou.Modding;

namespace Meitou.CharacterEditTweaks;

/// <summary>
/// The editor camera: wider zoom limits (the original writes its camera's zoom table: whole body -23 to 24, head -23 to 16, against the boom's
/// 30.15, where vanilla keeps -9 to 7 and -23 to -17) and a height offset of the camera rig (the CAMERA slider, -10 to 10 units). The zoom is a
/// prefix and postfix on <c>CharacterEditorPanel.EditorWheel</c> that redo the wheel step inside the wider limits; the height a postfix on
/// <c>CharacterRenderer.StageCamera</c> that lifts the eye and the pivot together, as the original moves the rig's node. A fault is logged once
/// and the camera works as without the mod.
/// </summary>
static class CameraPatches
{
    /// <summary>The boom's own length and the original's new zoom limits for the body and the head.</summary>
    const float Boom = 30.15f, BodyNear = Boom - 23, BodyFar = Boom + 24, HeadNear = Boom - 23, HeadFar = Boom + 16;

    /// <summary>Meitou's wheel steps (body, head) and the head's vanilla far limit, past which the head zooms at the body's step.</summary>
    const float BodyStep = 1.5f, HeadStep = 0.5f, HeadVanillaFar = Boom - 17;

    /// <summary>The camera rig's lift, set by the CAMERA slider on the host's thread and read by the renderer.</summary>
    static float s_height;

    static ModContext? s_context;
    static int s_faulted;

    public static float Height
    {
        get => Volatile.Read(ref s_height);
        set => Volatile.Write(ref s_height, value);
    }

    public static void Install(ModContext context)
    {
        s_context = context;
        if (EditorAccess.EditorWheel is { } wheel && EditorAccess.OrbitReady)
        {
            context.Harmony.Patch(wheel, new HarmonyMethod(typeof(CameraPatches), nameof(WheelBefore)),
                new HarmonyMethod(typeof(CameraPatches), nameof(WheelAfter)));
        }
        else context.LogError("CharacterEditorPanel.EditorWheel or the orbit not found: the editor's zoom keeps its limits");

        var renderer = AccessTools.TypeByName("Meitou.Game.Rendering.Characters.CharacterRenderer");
        if (renderer is not null && AccessTools.Method(renderer, "StageCamera") is { } stage)
        {
            context.Harmony.Patch(stage, postfix: new HarmonyMethod(typeof(CameraPatches), nameof(StageAfter)));
        }
        else context.LogError("CharacterRenderer.StageCamera not found: the camera height slider does nothing");
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void WheelBefore(object __instance, out float __state)
    {
        __state = float.NaN;
        try
        {
            if (EditorAccess.OrbitOf(__instance) is { } orbit) __state = orbit.Distance;
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    static void WheelAfter(object __instance, float steps, bool __result, float __state)
    {
        try
        {
            if (!__result || float.IsNaN(__state) || EditorAccess.OrbitOf(__instance) is not { } orbit) return;

            EditorAccess.SetDistance(__instance, Zoomed(orbit.Head, __state, steps));
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    static void StageAfter(ref ValueTuple<Vector3, Vector3> __result)
    {
        float height = Height;
        if (height == 0) return;

        var up = new Vector3(0, height, 0);
        __result = (__result.Item1 + up, __result.Item2 + up);
    }
#pragma warning restore IDE1006

    /// <summary>The distance after <paramref name="steps"/> wheel steps (up: closer) from <paramref name="before"/>, inside the wider limits.</summary>
    static float Zoomed(bool head, float before, float steps)
    {
        if (!head) return Math.Clamp(before - steps * BodyStep, BodyNear, BodyFar);

        float step = before - steps * HeadStep <= HeadVanillaFar && before <= HeadVanillaFar ? HeadStep : BodyStep;
        return Math.Clamp(before - steps * step, HeadNear, HeadFar);
    }

    static void Fault(Exception e)
    {
        if (Interlocked.Exchange(ref s_faulted, 1) == 0) s_context?.LogError($"the editor camera patch failed, the camera works as without it: {e}");
    }
}
