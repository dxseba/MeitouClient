using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Modding;

namespace Meitou.Brutality;

/// <summary>Frozen torso states receive new external palettes without a native repose.</summary>
internal static class StockTorsoMotion
{
    static readonly ConditionalWeakTable<object, object> marked = new();
    static readonly object marker = new();

    internal static void Mark(object state) => marked.GetValue(state, static _ => marker);
    static bool HasHistory(object state) => marked.TryGetValue(state, out _);

    internal static bool Install(Type renderer, ModContext context)
    {
        try
        {
            var pose = AccessTools.Method(renderer, "PoseOne") ?? throw new MissingMethodException(renderer.FullName, "PoseOne");
            context.Harmony.Patch(pose, transpiler: new HarmonyMethod(typeof(StockTorsoMotion), nameof(MotionPalettes)));
            return true;
        }
        catch (Exception error)
        {
            context.Log($"Brutality stock torso motion history unavailable: {error.GetBaseException().Message}");
            return false;
        }
    }

    static IEnumerable<CodeInstruction> MotionPalettes(IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase original)
    {
        var live = original.GetParameters()[0].ParameterType.GetElementType()!;
        var repose = AccessTools.Field(live, "Repose");
        var frozen = AccessTools.Field(live, "Frozen");
        var state = AccessTools.Field(live, "State");
        var hasHistory = AccessTools.Method(typeof(StockTorsoMotion), nameof(HasHistory));
        var code = instructions.ToList();
        var reads = Enumerable.Range(0, code.Count)
            .Where(index => code[index].opcode == OpCodes.Ldfld && Equals(code[index].operand, repose)).ToArray();
        if (repose is null || frozen is null || state is null || reads.Length != 3)
            throw new InvalidOperationException("Expected native repose and two motion palette checks in PoseOne.");

        // The first check controls actual posing. Only the two later checks select or retain previous matrices.
        foreach (int index in reads.Skip(1).Reverse())
        {
            var next = generator.DefineLabel();
            code[index + 1].labels.Add(next);
            code.InsertRange(index + 1,
            [
                new(OpCodes.Ldarg_1), new(OpCodes.Ldfld, frozen), new(OpCodes.Brfalse, next),
                new(OpCodes.Ldarg_1), new(OpCodes.Ldfld, state), new(OpCodes.Call, hasHistory), new(OpCodes.Or),
            ]);
        }
        return code;
    }
}
