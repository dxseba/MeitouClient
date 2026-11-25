using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Meitou.Modding;

namespace Meitou.DesolationSystem;

/// <summary>
/// Call swaps for small Meitou methods the JIT may inline (a property getter, a two-line rate): instead of patching the small method, a
/// transpiler on each caller replaces the call with a static wrapper of the same stack shape (instance first, then the arguments), which calls
/// the original through a delegate and adjusts the answer. A caller without the call is left as it is.
/// </summary>
static class Calls
{
    static readonly Dictionary<(Module, int), MethodInfo> Swaps = [];

    /// <summary>Replaces every call of <paramref name="target"/> in <paramref name="callers"/> with <paramref name="wrapper"/>.</summary>
    internal static void Swap(ModContext context, MethodInfo? target, MethodInfo wrapper, params MethodBase?[] callers)
    {
        if (target is null)
        {
            context.LogError($"{wrapper.Name}: its Meitou method was not found; that perk is off");
            return;
        }

        lock (Swaps) Swaps[(target.Module, target.MetadataToken)] = wrapper;
        foreach (var caller in callers)
        {
            if (caller is null)
            {
                context.LogError($"{wrapper.Name}: a caller of {target.Name} was not found; that caller is left alone");
                continue;
            }

            context.Harmony.Patch(caller, transpiler: new HarmonyMethod(typeof(Calls), nameof(Transpiler)));
        }
    }

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var code in instructions)
        {
            if ((code.opcode == OpCodes.Call || code.opcode == OpCodes.Callvirt) && code.operand is MethodInfo m && Wrapper(m) is { } wrapper)
            {
                code.opcode = OpCodes.Call;
                code.operand = wrapper;
            }

            yield return code;
        }
    }

    static MethodInfo? Wrapper(MethodInfo m)
    {
        lock (Swaps) return Swaps.GetValueOrDefault((m.Module, m.MetadataToken));
    }
}
