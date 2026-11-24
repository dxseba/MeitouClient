using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Data;
using Meitou.Data.World;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.KathunLightsabers;

/// <summary>A lit saber: its holder, its light (placed each frame) and where along the drawn weapon frame the light sits (0 hilt, 1 tip).</summary>
sealed record SaberLamp(CharacterId Holder, WorldLight Light, float Along);

/// <summary>
/// The sabers' coloured lights. Meitou has no mod light, so a Harmony postfix on the character renderer's lamp gathering
/// (<c>Meitou.Game.Rendering.Characters.CharacterRenderer.CollectLamps</c>, which adds the carried lanterns to the frame's moving lamps) adds one
/// point light per lit saber, at its blade as the renderer just drew it (<c>HeldBladeFrame</c>), night-only as the game's carried lights. Display
/// only: the simulation never sees it. Any failure logs once and leaves the vanilla lamps alone.
/// </summary>
static class SaberLamps
{
    const string Renderer = "Meitou.Game.Rendering.Characters.CharacterRenderer";

    static volatile SaberLamp[] s_lamps = [];
    static volatile Func<CharacterId, long>? s_drawKey;
    static MethodInfo? s_bladeFrame;
    static PropertyInfo? s_base, s_turn, s_length;
    static Action<string>? s_log;
    static int s_failed;

    /// <summary>Patches the gathering; false (logged) when it is not found.</summary>
    public static bool Patch(ModContext context)
    {
        s_log = context.LogError;
        var type = AccessTools.TypeByName(Renderer);
        var collect = type is null ? null : AccessTools.Method(type, "CollectLamps", [typeof(List<WorldLight>)]);
        s_bladeFrame = type is null ? null : AccessTools.Method(type, "HeldBladeFrame", [typeof(long)]);
        if (collect is null || s_bladeFrame is null)
        {
            context.LogError($"{Renderer}.CollectLamps or HeldBladeFrame not found: sabers give no light");
            return false;
        }

        context.Harmony.Patch(collect, postfix: new HarmonyMethod(typeof(SaberLamps), nameof(Postfix)));
        return true;
    }

    /// <summary>Takes the host's draw-key lookup from the frame once (host thread).</summary>
    public static void Bind(ModFrame frame)
    {
        if (s_drawKey is not null) return;

        var host = AccessTools.Field(typeof(ModFrame), "_host")?.GetValue(frame);
        var method = host is null ? null : AccessTools.Method(host.GetType(), "DrawKey", [typeof(CharacterId)]);
        if (method is null) s_log?.Invoke("GameHost.DrawKey not found: sabers give no light");
        s_drawKey = method is null ? _ => long.MinValue : method.CreateDelegate<Func<CharacterId, long>>(host);
    }

    /// <summary>Hands the lit sabers to the next gatherings (host thread).</summary>
    public static void Publish(SaberLamp[] lamps) => s_lamps = lamps;

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void Postfix(object __instance, List<WorldLight> into)
    {
        if (s_failed != 0 || s_drawKey is not { } drawKey || s_lamps is not { Length: > 0 } lamps) return;

        try
        {
            foreach (var lamp in lamps)
            {
                if (Place(__instance, drawKey(lamp.Holder), lamp.Along) is { } at) into.Add(lamp.Light with { Position = at });
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            if (Interlocked.Exchange(ref s_failed, 1) == 0) s_log?.Invoke($"saber lights off ({e.GetType().Name}: {e.Message})");
        }
    }
#pragma warning restore IDE1006

    /// <summary>
    /// A colour's light: its LIGHT record's colour, radius, brightness (no roll: the records' variance is 0) and type, lit only at night as the
    /// game's carried lights.
    /// </summary>
    public static WorldLight Template(GameRecord light) =>
        WorldLights.FromRecord(light, Vector3.Zero, -Vector3.UnitY, light.GetFloat("brightness"), WorldLightRule.Night, "saber", light.StringId);

    /// <summary>The point <paramref name="along"/> the character's drawn blade as last drawn; null when it holds none drawn or was not drawn.</summary>
    static Vector3? Place(object renderer, long key, float along)
    {
        if (s_bladeFrame!.Invoke(renderer, [key]) is not { } frame) return null;

        var type = frame.GetType();
        s_base ??= type.GetProperty("Base");
        s_turn ??= type.GetProperty("Turn");
        s_length ??= type.GetProperty("Length");
        var at = (Vector3)s_base!.GetValue(frame)!;
        var turn = (Quaternion)s_turn!.GetValue(frame)!;
        float length = (float)s_length!.GetValue(frame)!;
        return at + Vector3.Transform(Vector3.UnitY, turn) * (along * length);
    }
}
