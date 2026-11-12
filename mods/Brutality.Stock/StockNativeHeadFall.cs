using Meitou.Game.Host;
using System.Collections;
using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Modding;

namespace Meitou.Brutality;

/// <summary>Coordinates the stock falling-head clock with the cut mesh and lands heads on stable terrain.</summary>
internal sealed class StockNativeHeadFall
{
    static StockNativeHeadFall? installed;
    readonly FieldInfo session, decapitation, heads, key, tick, start;

    StockNativeHeadFall(Type host, Type falling)
    {
        session = Field(host, "Session");
        decapitation = StockHostParts.Decapitation;
        heads = Field(decapitation.FieldType, "_heads");
        key = Field(falling, "Key");
        tick = Field(falling, "Tick");
        start = Field(falling, "Start");
    }

    internal static void Install(ModContext context, Type host, Type chars)
    {
        if (installed is not null) return;
        try
        {
            var decapitation = StockHostParts.Decapitation.FieldType;
            var falling = AccessTools.Inner(decapitation, "FallingHead") ?? throw new MissingMemberException(decapitation.Name, "FallingHead");
            var hook = new StockNativeHeadFall(host, falling);
            var surface = AccessTools.Method(decapitation, "SurfaceUnder", [chars, typeof(Vector2), falling])
                ?? throw new MissingMethodException(decapitation.Name, "SurfaceUnder");
            context.Harmony.Patch(surface, prefix: new HarmonyMethod(typeof(StockNativeHeadFall), nameof(UseGround)));
            installed = hook;
        }
        catch (Exception error)
        {
            context.Log($"Brutality stock head landing unavailable: {error.GetBaseException().Message}");
        }
    }

    static FieldInfo Field(Type type, string name) =>
        AccessTools.Field(type, name) ?? throw new MissingFieldException(type.Name, name);

    static bool UseGround(object __instance, Vector2 at, object head, ref float __result)
    {
        if (installed is not { } hook || StockHostParts.HostOf(__instance) is not { } host
            || hook.session.GetValue(host) is not WorldSession shown
            || hook.start.GetValue(head) is not Vector3 from) return true;
        // Stock records the first contact height indefinitely. An animated or hidden bone can leave
        // a head suspended above the actual floor after the ragdoll moves.
        float ground = shown.World.Walkability.GroundHeight(at.X, at.Y, from.Y);
        if (!float.IsFinite(ground)) return true;
        __result = ground;
        return false;
    }

    internal static bool StartReplacement(object host, long headKey, double clock)
    {
        if (installed is not { } hook || hook.heads.GetValue(hook.decapitation.GetValue(host)) is not IList falling) return false;
        foreach (object head in falling)
        {
            if (hook.key.GetValue(head) is not long key || key != headKey) continue;
            // The alternate mesh is already visible. The stock readiness check waits for the exact
            // original appearance, which the draw list may never submit after replacing it.
            if (hook.tick.GetValue(head) is double tick && double.IsNaN(tick)) hook.tick.SetValue(head, clock);
            return true;
        }
        return false;
    }
}
