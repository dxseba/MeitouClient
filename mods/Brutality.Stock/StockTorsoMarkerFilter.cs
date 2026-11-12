using Meitou.Game.Host;
using Meitou.Game.Rendering.Overlays;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
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

    static IEnumerable<CodeInstruction> Filter(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var getter = AccessTools.PropertyGetter(typeof(CharacterSnapshot), nameof(CharacterSnapshot.Appearance))
            ?? throw new MissingMethodException(nameof(CharacterSnapshot.Appearance));
        var tests = code.Select((instruction, index) => (instruction, index)).Where(item => item.instruction.Calls(getter)).ToArray();
        if (tests.Length != 1 || tests[0].index + 1 >= code.Count || tests[0].instruction.blocks.Count != 0)
            throw new InvalidOperationException("The stock missing-model marker condition changed.");

        var (original, at) = tests[0];
        var branch = code[at + 1].opcode;
        if (branch != OpCodes.Brtrue && branch != OpCodes.Brtrue_S)
            throw new InvalidOperationException("The stock missing-model marker branch changed.");
        // The stock branch skips the marker on a non-null look. A boolean preserves the same branch.
        var host = new CodeInstruction(OpCodes.Ldarg_0);
        host.labels.AddRange(original.labels);
        code[at] = host;
        code.Insert(at + 1, new(OpCodes.Call, AccessTools.Method(typeof(StockTorsoMarkerFilter), nameof(SkipMarker))));
        return code;
    }

    static bool SkipMarker(CharacterSnapshot actor, object host) => actor.Appearance is not null
        || session?.GetValue(host) is WorldSession shown && StockTorsoProxies.For(shown.World)?.Frame.TryOwner(actor.Id, out _) == true;
}
