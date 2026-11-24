using System.Reflection.Emit;
using HarmonyLib;
using Meitou.Data.Ui;
using Meitou.Modding;

namespace Meitou.OrganizeTrader;

/// <summary>
/// The two Harmony patches: a postfix on <c>TradeWindows.BindTraderGeometry</c> that shortens the stock's scroll view by
/// <see cref="Shorten"/> pixels (room for the sort row of a narrow window, as the original reserves it), and a transpiler on
/// <c>ScreenOverrides.Ask</c> that lifts the 512-widget cap for this mod's own override sources only (every other source keeps it). Both are
/// display only; a fault leaves the engine's behaviour, logged once.
/// </summary>
static class Patches
{
    const string Ui = "Meitou.Game.Ui.";
    const string View = "scrollview_backpack_content";

    /// <summary>
    /// The most widgets this mod's override sources are asked for per frame.
    /// </summary>
    const int OwnLimit = 8192;

    /// <summary>
    /// The pixels the scroll view should lose at the bottom (0: none); set by the controls, read at the next bind.
    /// </summary>
    public static float Shorten;

    /// <summary>
    /// The pixels the last bind took off, and the content's height they were measured against.
    /// </summary>
    public static float Applied, ContentHeight;

    static ModContext? s_context;
    static string s_owner = "";
    static Func<object, string>? s_ownerOf;
    static int s_faulted;

    public static void Install(ModContext context, string owner)
    {
        s_context = context;
        s_owner = owner;
        var assembly = typeof(Game.Ui.UiHost).Assembly;
        Geometry(context, assembly.GetType(Ui + "TradeWindows"));
        Limit(context, assembly.GetType(Ui + "ScreenOverrides"));
    }

    static void Geometry(ModContext context, Type? windows)
    {
        if (windows is null || AccessTools.Method(windows, "BindTraderGeometry") is not { } bind)
        {
            context.LogError("TradeWindows.BindTraderGeometry not found: a second row lies over the stock");
            return;
        }

        context.Harmony.Patch(bind, postfix: new HarmonyMethod(typeof(Patches), nameof(GeometryPostfix)));
    }

    static void Limit(ModContext context, Type? overrides)
    {
        var entry = overrides?.GetNestedType("Entry", AccessTools.all);
        var ask = overrides is null ? null : AccessTools.Method(overrides, "Ask");
        var owner = entry is null ? null : AccessTools.PropertyGetter(entry, "Owner");
        if (ask is null || owner is null)
        {
            context.LogError("ScreenOverrides.Ask not found: stocks over 512 items are rearranged in part");
            return;
        }

        s_ownerOf = e => owner.Invoke(e, null) as string ?? "";
        context.Harmony.Patch(ask, transpiler: new HarmonyMethod(typeof(Patches), nameof(LimitTranspiler)));
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void GeometryPostfix(UiBindings b)
    {
        try
        {
            Applied = 0;
            if (Shorten <= 0 || ContentHeight <= 0) return;

            var view = b.RelativePositions.Keys.FirstOrDefault(w => w.Name == View);
            if (view is null) return;

            var r = b.RelativePositions[view];
            float cut = Shorten / ContentHeight;
            if (cut >= r.Height) return;

            b.RelativePositions[view] = r with { Height = r.Height - cut };
            Applied = Shorten;
        }
        catch (Exception e)
        {
            Fault("shortening the stock view", e);
        }
    }
#pragma warning restore IDE1006

    /// <summary>
    /// <c>Ask</c> with its widget cap (the constant 512) replaced by <see cref="CapOf"/> of its entry (argument 0).
    /// </summary>
    static IEnumerable<CodeInstruction> LimitTranspiler(IEnumerable<CodeInstruction> code)
    {
        var cap = AccessTools.Method(typeof(Patches), nameof(CapOf));
        foreach (var instruction in code)
        {
            if (instruction.opcode == OpCodes.Ldc_I4 && instruction.operand is int and 512)
            {
                yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction);
                yield return new CodeInstruction(OpCodes.Call, cap);
                continue;
            }

            yield return instruction;
        }
    }

    /// <summary>
    /// The cap for one override source: higher for this mod's own, 512 for any other's (and after a fault).
    /// </summary>
    public static int CapOf(object entry)
    {
        try
        {
            return s_ownerOf is { } of && of(entry) == s_owner ? OwnLimit : 512;
        }
        catch (Exception e)
        {
            Fault("reading an override source's owner", e);
            return 512;
        }
    }

    static void Fault(string what, Exception e)
    {
        if (Interlocked.Exchange(ref s_faulted, 1) == 0) s_context?.LogError($"{what} failed, the engine's behaviour kept: {e}");
    }
}
