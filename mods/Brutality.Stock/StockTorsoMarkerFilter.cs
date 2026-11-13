using Meitou.Game.Host;
using Meitou.Game.Rendering.Overlays;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Meitou.Data.Characters;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.Brutality;

/// <summary>Hides the stock missing-model square for a carry proxy whose mesh is drawn separately.</summary>
internal static class StockTorsoMarkerFilter
{
    static FieldInfo? session;

    internal static void Install(ModContext context)
    {
        try
        {
            var host = AccessTools.TypeByName("Meitou.Game.GameHost") ?? throw new TypeLoadException("GameHost");
            session = StockHostParts.Field(host, "Session");
            var draw = AccessTools.Method(host, "DrawMarkers", [typeof(DebugOverlay), typeof(int), typeof(int)])
                ?? throw new MissingMethodException(host.FullName, "DrawMarkers");
            context.Harmony.Patch(draw, transpiler: new(typeof(StockTorsoMarkerFilter), nameof(Filter)));
        }
        catch (Exception error)
        {
            context.Log($"Brutality torso proxy marker filter unavailable: {error.GetBaseException().Message}");
        }
    }

    /// <summary>
    /// Every look the marker loop reads goes through <see cref="MarkerLook"/>: the stock loop skips a character with a look the
    /// renderer has not failed, so a proxy's stand-in look skips it as well, whichever form the condition takes.
    /// </summary>
    static IEnumerable<CodeInstruction> Filter(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var getter = AccessTools.PropertyGetter(typeof(CharacterSnapshot), nameof(CharacterSnapshot.Appearance))
            ?? throw new MissingMethodException(nameof(CharacterSnapshot.Appearance));
        var reads = code.Select((instruction, index) => (instruction, index)).Where(item => item.instruction.Calls(getter)).ToArray();
        if (reads.Length == 0 || reads.Any(read => read.instruction.blocks.Count != 0))
            throw new InvalidOperationException("The stock missing-model marker condition changed.");

        var replace = AccessTools.Method(typeof(StockTorsoMarkerFilter), nameof(MarkerLook));
        foreach (var (original, at) in reads.Reverse())
        {
            var host = new CodeInstruction(OpCodes.Ldarg_0);
            host.labels.AddRange(original.labels);
            code[at] = new CodeInstruction(OpCodes.Call, replace);
            code.Insert(at, host);
        }
        return code;
    }

    /// <summary>A stand-in no renderer has built: it reads as not failed, so the marker is skipped.</summary>
    static readonly CharacterAppearance ProxyLook =
        (CharacterAppearance)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(CharacterAppearance));

    static CharacterAppearance? MarkerLook(CharacterSnapshot actor, object host) => actor.Appearance
        ?? (session?.GetValue(host) is WorldSession shown && StockTorsoProxies.For(shown.World)?.Frame.TryOwner(actor.Id, out _) == true
            ? ProxyLook : null);
}
