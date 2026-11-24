using System.Globalization;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Ui;
using Meitou.Game.Ui;
using Meitou.Modding;

namespace Meitou.BestMinimap;

/// <summary>
/// What a mod's image node cannot ask for: a turned picture and a picture of the game's own image sets. A Harmony postfix on the private
/// <c>Meitou.Game.Ui.UiHost.Control</c> (which makes each laid-out node's control; host thread; large, not inlined) adds properties the painter
/// already reads to the controls of nodes this mod marked: <c>ImageAngle</c> (radians clockwise about the box's centre, as the big map's camera
/// marker) for the map turning with the camera, the camera arrow and the facing noses; <c>ImageResource</c>/<c>ImageGroup</c>/<c>ImageName</c>
/// (an image set group, e.g. <c>Kenshi_MapMarkers</c>' Town) for the big map's own town icons, in place of the node's image file. A failure is
/// logged once and leaves the patch off (plain pictures, north up).
/// </summary>
static class ImagePatch
{
    sealed class Extra
    {
        public float Angle;
        public string? Group;
    }

    static readonly ConditionalWeakTable<UiNode, Extra> Marked = [];
    static Action<string>? s_log;

    /// <summary>The patch is in place (else the map never turns and town icons are diamonds).</summary>
    public static bool Active { get; private set; }

    public static void Install(ModContext context)
    {
        s_log = context.Log;
        var target = AccessTools.Method(typeof(UiHost), "Control",
            [typeof(UiNode), typeof(UiRect), typeof(UiRect), typeof(bool), typeof(float), typeof(string)]);
        if (target is null)
        {
            context.Log("UiHost.Control not found: the minimap does not turn and town icons are diamonds");
            return;
        }

        context.Harmony.Patch(target, postfix: new HarmonyMethod(typeof(ImagePatch), nameof(Postfix)));
        Active = true;
    }

    /// <summary>Draws <paramref name="node"/>'s picture turned by <paramref name="angle"/> radians clockwise.</summary>
    public static UiNode Turned(UiNode node, float angle)
    {
        if (angle != 0) Marked.AddOrUpdate(node, new Extra { Angle = angle, Group = Marked.TryGetValue(node, out var e) ? e.Group : null });
        return node;
    }

    /// <summary>Draws <paramref name="group"/> of the game's map marker set in place of <paramref name="node"/>'s image file.</summary>
    public static UiNode MapIcon(UiNode node, string group)
    {
        Marked.AddOrUpdate(node, new Extra { Group = group, Angle = Marked.TryGetValue(node, out var e) ? e.Angle : 0 });
        return node;
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void Postfix(UiNode node, UiControl __result)
    {
        if (!Active || node is null || __result is null || !Marked.TryGetValue(node, out var extra)) return;

        try
        {
            if (__result.Properties is not Dictionary<string, string> properties) return;

            if (extra.Angle != 0) properties["ImageAngle"] = extra.Angle.ToString(CultureInfo.InvariantCulture);
            if (extra.Group is { } group)
            {
                properties.Remove("ImageTexture");
                properties["ImageResource"] = "Kenshi_MapMarkers";
                properties["ImageGroup"] = group;
                properties["ImageName"] = group;
            }
        }
        catch (Exception e)
        {
            Active = false;
            s_log?.Invoke($"image patch failed, left off for the session: {e.Message}");
        }
    }
#pragma warning restore IDE1006
}
