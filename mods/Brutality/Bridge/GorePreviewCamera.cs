using System.Numerics;
using HarmonyLib;
using Meitou.Modding;
using Meitou.Engine;
using Meitou.Rendering;
using Meitou.Brutality.Physics;
using Meitou.Brutality.Rendering;

namespace Meitou.Brutality;

internal static class GorePreviewCamera
{
    internal static void Install(ModContext context)
    {
        if (Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_FOCUS") != "1") return;
        if (AccessTools.TypeByName("Meitou.Game.GameHost") is { } host)
        {
            context.Harmony.Patch(AccessTools.Method(host, "SandboxSpawn"), postfix: new HarmonyMethod(typeof(GorePreviewCamera), nameof(Focus)));
            context.Harmony.Patch(AccessTools.Method(host, "DrawWorld"), prefix: new HarmonyMethod(typeof(GorePreviewCamera), nameof(FollowHeads)));
        }
    }
    static void FollowHeads(object __instance)
    {
        if (!(Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_PREVIEW") ?? "").StartsWith("head", StringComparison.OrdinalIgnoreCase)) return;
        if (AccessTools.Field(__instance.GetType(), "session")?.GetValue(__instance) is not WorldSession session) return;
        var frame = session.World.Systems.OfType<FragmentPhysicsSystem>().FirstOrDefault()?.FrameAt(session.ShownTick);
        if (frame is null) return;
        var pieces = frame.Characters.Values.Where(actor => actor.State.Head is not null)
            .SelectMany(actor => actor.Detached).Where(piece => piece.StableKey is 2 or 3).ToArray();
        if (pieces.Length == 0) return;
        var centre = pieces.Aggregate(Vector3.Zero, (sum, piece) => sum + piece.Position) / pieces.Length;
        session.Camera.Floors = (_, _) => centre.Y;
        session.Camera.Place(new(centre.X, centre.Z), session.Camera.Strategy.Yaw, session.Camera.Strategy.Pitch, session.Camera.Strategy.Distance);
        AccessTools.Method(__instance.GetType(), "ApplyCamera")?.Invoke(__instance, new object[] { session.Camera.Current });
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
