using System.Globalization;
using System.Linq.Expressions;
using HarmonyLib;
using Meitou.Game.Ui;
using Meitou.Modding;

namespace Meitou.ItemMarkers;

/// <summary>
/// The Meitou class's colour in its weapons' tooltips: the original hooks the weapon tooltip functions and swaps the <c>ItemGrade5</c> colour of
/// its grade lines for <c>ItemGradeMeitou</c> on Meitou-class weapons only. A Harmony postfix on the internal
/// <c>Meitou.Game.Ui.TooltipRuntime.ItemLines</c> gives the rows in the top grade's tone (<see cref="TooltipTone.Grade4"/>: the model line under
/// the name and <c>[Model # ...]</c>) of an item of material <see cref="Grades.MeitouMaterial"/> that colour (<see cref="Grades.MeitouColour"/>:
/// the vanilla palette has neither key, so it stays grade 4's yellow). A failure is logged once and leaves the patch off.
/// </summary>
static class TooltipPatch
{
    static Func<object, string>? s_material;
    static Action<string>? s_log;
    static bool s_failed;

    public static void Install(ModContext context)
    {
        s_log = context.Log;
        var runtime = AccessTools.TypeByName("Meitou.Game.Ui.TooltipRuntime");
        var tip = runtime is null ? null : AccessTools.Inner(runtime, "ItemTip");
        var target = runtime is null ? null : AccessTools.Method(runtime, "ItemLines");
        if (tip is null || target is null)
        {
            context.Log("TooltipRuntime.ItemLines not found: the Meitou tooltip line keeps the game's colour");
            return;
        }

        var p = Expression.Parameter(typeof(object));
        s_material = Expression.Lambda<Func<object, string>>(Expression.Property(Expression.Convert(p, tip), "Material"), p).Compile();
        context.Harmony.Patch(target, postfix: new HarmonyMethod(typeof(TooltipPatch), nameof(Postfix)));
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void Postfix(object t, ref IReadOnlyList<TooltipLine> __result)
    {
        if (s_failed || t is null || __result is null || s_material is null) return;

        try
        {
            if (s_material(t) == Grades.MeitouMaterial) __result = Recoloured(__result, Grades.MeitouColour());
        }
        catch (Exception e)
        {
            s_failed = true;
            s_log?.Invoke($"tooltip recolour failed, left off for the session: {e.Message}");
        }
    }
#pragma warning restore IDE1006

    static List<TooltipLine> Recoloured(IReadOnlyList<TooltipLine> lines, System.Numerics.Vector4 colour)
    {
        string rgb = string.Create(CultureInfo.InvariantCulture, $"{colour.X:0.###} {colour.Y:0.###} {colour.Z:0.###}");
        return [.. lines.Select(l => l.KeyTone == TooltipTone.Grade4 && l.KeyColour is null ? l with { KeyColour = rgb } : l)];
    }
}
