using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.VitalSense;

/// <summary>
/// The stand-in for the original's shader tint: a postfix on <c>PointerView.HighlightTargets</c> (the outlines the held Highlight key draws, within
/// 500 of the camera's focus) adds the marked downed characters beyond that, up to the marker distance and budget, in their relation's colour.
/// </summary>
static class VitalOutline
{
    static readonly Vector4[] Colours = [new(.71f, .71f, .71f, .9f), new(.87f, .91f, .35f, .9f), new(1, .2f, .2f, .9f), new(.25f, 1, .25f, .9f)];

    static FieldInfo? s_playerField;

    public static void Install(ModContext context)
    {
        var player = AccessTools.TypeByName("Meitou.Game.PlayerInterface") ?? throw new TypeLoadException("Meitou.Game.PlayerInterface");
        var pointer = AccessTools.TypeByName("Meitou.Game.Ui.PointerView") ?? throw new TypeLoadException("Meitou.Game.Ui.PointerView");
        s_playerField = pointer.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .SingleOrDefault(f => f.FieldType == player)
            ?? throw new MissingFieldException(pointer.FullName, "PlayerInterface");

        var targets = AccessTools.Method(pointer, "HighlightTargets") ?? throw new MissingMethodException(pointer.FullName, "HighlightTargets");
        context.Harmony.Patch(targets, postfix: new HarmonyMethod(typeof(VitalOutline), nameof(AddDowned)));
    }

    #pragma warning disable IDE1006 // Harmony binds __result / __instance by name
    static void AddDowned(object __instance, Vector3 centre, List<(CharacterId Id, Vector4 Colour)> __result)
    #pragma warning restore IDE1006
    {
        var settings = VitalSenseMod.Settings;
        if (!settings.Enabled || !settings.Outline || s_playerField?.GetValue(__instance) is not { } player) return;
        if (VitalMarkers.Session(player) is not { } session) return;

        var listed = __result.Select(t => t.Id).ToHashSet();
        foreach (var target in VitalMarkers.Targets(session, centre, settings.Budget))
        {
            if (target.Vital.State == VitalState.None || !settings.Shows(target.Vital.State) || !listed.Add(target.Character.Id)) continue;

            __result.Add((target.Character.Id, Colours[(int)target.Relation]));
        }
    }
}
