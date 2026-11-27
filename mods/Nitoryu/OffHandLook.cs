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
/// weapon's bare blade (<c>bare sword</c>) sits in the left hand with a fixed grip (<see cref="OffHandGrip"/>) and its sheath stays where it is
/// worn, empty, as the main weapon's does. Harmony: a postfix on <c>Meitou.Game.Host.HiddenHatLooks.WorldLook</c> (the look a character is drawn
/// with in the world, on the host's thread). The look is made once per worn look and side and drawn once its meshes are prepared
/// (<c>CharacterRenderer.IsPrepared</c>, or <c>CharacterContent.Fetched</c>); until then the plain look shows. A failure logs once and draws the
/// plain look.
/// </summary>
static class OffHandLook
{
    /// <summary>The attachment point the port adds for the left hand.</summary>
    const string LeftPoint = "nitoryu left hand";

    /// <summary>The bone and offset the blade is held at (<see cref="OffHandGrip"/>); set at start from the original's ini.</summary>
    public static OffHandGrip Grip { get; set; } = OffHandGrip.Default;

    static readonly ConditionalWeakTable<CharacterAppearance, Dictionary<(string Weapon, bool Back), CharacterAppearance>> Looks = [];
    static readonly MethodInfo? Clone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
    static FieldInfo? s_parts, s_points, s_host;
    static MethodInfo? s_prepared, s_warm, s_fetched;
    static FieldInfo? s_content;
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
        s_content = renderer is null ? null : AccessTools.Field(renderer, "_content");
        var content = AccessTools.TypeByName("Meitou.Game.Rendering.Characters.CharacterContent");
        s_fetched = content is null ? null : AccessTools.Method(content, "Fetched", [typeof(CharacterAppearance), typeof(bool)]);
        if (world is null || s_host is null || s_parts is null || s_points is null || s_prepared is null || s_warm is null || Clone is null)
        {
            context.LogError("HiddenHatLooks.WorldLook or the look's members not found: the second blade is drawn sheathed");
            return;
        }

        context.Harmony.Patch(world, postfix: new HarmonyMethod(typeof(OffHandLook), nameof(Postfix)));
        PatchSway(context, content);
    }

    /// <summary>
    /// Keeps the held blade out of the optional gear sway: <c>CharacterContent.AddSway</c> swings every weapon part on a point other than
    /// <c>hands</c> as gear hanging at the hip or on the back, and a blade in the hand swung from its grip, point down (a player's report).
    /// </summary>
    static void PatchSway(ModContext context, Type? content)
    {
        var sway = content is null ? null : AccessTools.Method(content, "AddSway");
        if (sway is null)
        {
            context.Log("CharacterContent.AddSway not found: with gear sway on, the held second blade may swing");
            return;
        }

        context.Harmony.Patch(sway, prefix: new HarmonyMethod(typeof(OffHandLook), nameof(SwayPrefix)));
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    /// <summary>The held blade's part never joins a sway group (false skips <c>AddSway</c>).</summary>
    static bool SwayPrefix(CharacterPart part) => part.Point != LeftPoint;

    static void Postfix(object __instance, CharacterSnapshot c, ref CharacterAppearance __result)
    {
        if (s_failed != 0 || !NitoryuMod.Settings.ShowBlade || !c.WeaponDrawn || c.RangedDrawn) return;
        if (!NitoryuSystem.Drawn.TryGetValue(c.Id, out var second)) return;

        try
        {
            var look = DualLook(__result, second);
            if (ReferenceEquals(look, __result)) return;

            if (Ready(__instance, look))
            {
                __result = look;
                Diag.Log("blade-drawn", 3, () => $"second blade drawn in the left hand ('{second.Weapon}')");
            }
            else
            {
                Diag.Log("blade-wait", 5, () => $"second blade look not prepared yet ('{second.Weapon}')");
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            if (Interlocked.Exchange(ref s_failed, 1) == 0) s_context?.LogError($"second blade look failed ({e.GetType().Name}: {e.Message})");
        }
    }
#pragma warning restore IDE1006

    /// <summary>
    /// Whether <paramref name="look"/> can be drawn now: uploaded already, or its meshes prepared so the renderer builds it the first frame it is
    /// drawn (<c>CharacterContent.Fetched</c>, which starts the preparation when it is not). The renderer uploads a look only once it is drawn, so
    /// waiting for the upload alone would never end.
    /// </summary>
    static bool Ready(object looks, CharacterAppearance look)
    {
        var host = s_host!.GetValue(looks);
        var gpu = host is null ? null : Traverse.Create(host).Field("Gpu").GetValue();
        var characters = gpu is null ? null : Traverse.Create(gpu).Field("Characters").GetValue();
        if (characters is null) return false;
        if (s_prepared!.Invoke(characters, [look]) is true) return true;

        if (s_content?.GetValue(characters) is { } content && s_fetched is not null) return s_fetched.Invoke(content, [look, true]) is true;

        s_warm!.Invoke(characters, [look, false, false]);
        return false;
    }

    /// <summary>
    /// The look with the second weapon's blade in the left hand, made once per look and side; the look itself when it has no such parts.
    /// </summary>
    static CharacterAppearance DualLook(CharacterAppearance look, (string Weapon, bool Back) second)
    {
        var made = Looks.GetValue(look, static _ => []);
        lock (made)
        {
            if (made.TryGetValue(second, out var dual)) return dual;

            var built = Make(look, second.Weapon, second.Back);
            if (built is null) LogParts(look, second.Weapon);
            else s_context?.Log($"second blade look made for '{second.Weapon}' ({(second.Back ? "back" : "hip")})");

            dual = built ?? look;
            made[second] = dual;
            return dual;
        }
    }

    /// <summary>Logs the parts the look has for <paramref name="weapon"/> when no left-hand blade could be made from them (support only).</summary>
    static void LogParts(CharacterAppearance look, string weapon)
    {
        var mine = look.Parts.Where(p => p.Record.StringId == weapon).Select(p => $"{p.Slot}/{p.Field}/{p.Shown}");
        s_context?.Log($"no left-hand blade for '{weapon}': its parts in the look are [{string.Join(", ", mine)}]");
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
            [LeftPoint] = new AttachmentPoint(LeftPoint, Grip.Bone, Grip.Position, Grip.Rotation),
        };
        s_parts!.SetValue(copy, parts);
        s_points!.SetValue(copy, points);
        return copy;
    }
}
