using System.Numerics;
using HarmonyLib;
using Meitou.Modding;
using Meitou.Engine;

namespace Meitou.Brutality;

internal static class GorePreviewCamera
{
    internal static void Install(ModContext context)
    {
        if (Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_FOCUS") != "1") return;
        if (AccessTools.TypeByName("Meitou.Game.GameHost") is { } host)
            context.Harmony.Patch(AccessTools.Method(host, "SandboxSpawn"), postfix: new HarmonyMethod(typeof(GorePreviewCamera), nameof(Focus)));
    }
    static void Focus(object __instance, ref Vector3 __result)
    {
        var profile = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_PREVIEW") ?? "";
        __result.X += 15;
        __result.Y += profile.StartsWith("severe", StringComparison.OrdinalIgnoreCase) ? 14
            : profile.Contains("carry", StringComparison.OrdinalIgnoreCase) ? 12 : 2;
        float height = __result.Y;
        if (AccessTools.Field(__instance.GetType(), "session")?.GetValue(__instance) is WorldSession session)
            session.Camera.Floors = (_, _) => height;
    }
}
