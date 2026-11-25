using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Characters;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.Nitoryu;

/// <summary>
/// The second blade held and drawn (display only): while a dual fighter has its pair out, the host draws it with a look in which the second
/// weapon's bare blade (<c>bare sword</c>) sits in the left hand's prop bone (<c>Bip01 Prop1</c>, the bone the game's <c>override L arm</c>
/// clips move with the left hand) and its sheath stays where it is worn, empty, as the main weapon's does. Harmony: a postfix on
/// <c>Meitou.Game.Host.HiddenHatLooks.WorldLook</c> (the look a character is drawn with in the world, on the host's thread). The look is made
/// once per worn look and side and drawn once the renderer has prepared it (<c>CharacterRenderer.IsPrepared</c> / <c>Warm</c>); until then
/// the plain look shows. A failure logs once and draws the plain look.
/// </summary>
static class OffHandLook
{
    /// <summary>The attachment point the port adds for the left hand.</summary>
    const string LeftPoint = "nitoryu left hand";

    const string LeftBone = "Bip01 Prop1";

    static readonly ConditionalWeakTable<CharacterAppearance, Dictionary<(string Weapon, bool Back), CharacterAppearance>> Looks = [];
    static readonly MethodInfo? Clone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
    static FieldInfo? s_parts, s_points, s_host;
    static MethodInfo? s_prepared, s_warm;
    static ModContext? s_context;
    static int s_failed;

    public static void Patch(ModContext context)
    {
        s_context = context;
        var looks = AccessTools.TypeByName("Meitou.Game.Host.HiddenHatLooks");
        var world = looks is null ? null : AccessTools.Method(looks, "WorldLook");
        s_host = looks is null ? null : AccessTools.Field(looks, "<host>P");
        s_parts = AccessTools.Field(typeof(CharacterAppearance), "<Parts>k__BackingField");
        s_points = AccessTools.Field(typeof(CharacterAppearance), "<AttachmentPoints>k__BackingField");
        var renderer = AccessTools.TypeByName("Meitou.Game.Rendering.Characters.CharacterRenderer");
        s_prepared = renderer is null ? null : AccessTools.Method(renderer, "IsPrepared");
        s_warm = renderer is null ? null : AccessTools.Method(renderer, "Warm");
        if (world is null || s_host is null || s_parts is null || s_points is null || s_prepared is null || s_warm is null || Clone is null)
        {
            context.LogError("HiddenHatLooks.WorldLook or the look's members not found: the second blade is drawn sheathed");
            return;
        }

        context.Harmony.Patch(world, postfix: new HarmonyMethod(typeof(OffHandLook), nameof(Postfix)));
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void Postfix(object __instance, CharacterSnapshot c, ref CharacterAppearance __result)
    {
        if (s_failed != 0 || !NitoryuMod.Settings.ShowBlade || !c.WeaponDrawn || c.RangedDrawn) return;
        if (!NitoryuSystem.Drawn.TryGetValue(c.Id, out var second)) return;

        try
        {
            var look = DualLook(__result, second);
            if (!ReferenceEquals(look, __result) && Ready(__instance, look)) __result = look;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            if (Interlocked.Exchange(ref s_failed, 1) == 0) s_context?.LogError($"second blade look failed ({e.GetType().Name}: {e.Message})");
        }
    }
#pragma warning restore IDE1006

    /// <summary>Whether the renderer has prepared <paramref name="look"/>; asks it to when not.</summary>
    static bool Ready(object looks, CharacterAppearance look)
    {
        var host = s_host!.GetValue(looks);
        var gpu = host is null ? null : Traverse.Create(host).Field("Gpu").GetValue();
        var characters = gpu is null ? null : Traverse.Create(gpu).Field("Characters").GetValue();
        if (characters is null) return false;
        if (s_prepared!.Invoke(characters, [look]) is true) return true;

        s_warm!.Invoke(characters, [look, false, false]);
        return false;
    }

    /// <summary>The look with the second weapon's blade in the left hand, made once per look and side; the look itself when it has no such parts.</summary>
    static CharacterAppearance DualLook(CharacterAppearance look, (string Weapon, bool Back) second)
    {
        var made = Looks.GetValue(look, static _ => []);
        lock (made)
        {
            if (made.TryGetValue(second, out var dual)) return dual;

            dual = Make(look, second.Weapon, second.Back) ?? look;
            made[second] = dual;
            return dual;
        }
    }

    static CharacterAppearance? Make(CharacterAppearance look, string weapon, bool back)
    {
        var drawn = back ? WeaponShown.BackDrawn : WeaponShown.Drawn;
        var sheathed = back ? WeaponShown.BackSheathed : WeaponShown.Sheathed;
        var parts = new List<CharacterPart>();
        bool blade = false;
        foreach (var part in look.Parts)
        {
            bool mine = part.Record.StringId == weapon && part.Slot == AttachSlot.Weapon;
            if (mine && part.Shown == sheathed) continue;

            if (mine && part.Shown == drawn && part.Field == "bare sword")
            {
                parts.Add(part with { Point = LeftPoint, Shown = WeaponShown.Always });
                blade = true;
            }
            else
            {
                parts.Add(mine && part.Shown == drawn ? part with { Shown = WeaponShown.Always } : part);
            }
        }

        if (!blade) return null;

        var copy = (CharacterAppearance)Clone!.Invoke(look, null)!;
        var points = new Dictionary<string, AttachmentPoint>(look.AttachmentPoints, StringComparer.Ordinal)
        {
            [LeftPoint] = new AttachmentPoint(LeftPoint, LeftBone, Vector3.Zero, Quaternion.Identity),
        };
        s_parts!.SetValue(copy, parts);
        s_points!.SetValue(copy, points);
        return copy;
    }
}
