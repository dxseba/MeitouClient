using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Gameplay.Combat;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.Brutality;

/// <summary>Prefers a descending technique while preserving the stock weapon, reach and fallback choices.</summary>
internal static class StockTargetGroundSwing
{
    static readonly ConditionalWeakTable<IReadOnlyList<CombatTechnique>, Descending> choices = new();

    sealed class Descending(IReadOnlyList<CombatTechnique> source) : IReadOnlyList<CombatTechnique>
    {
        readonly CombatTechnique disabled = source[0] with { Disabled = true };
        public int Count => source.Count;
        public CombatTechnique this[int index]
        {
            get
            {
                var technique = source[index];
                return technique.LowStrike || technique.AttackDirection1 is 1 or 7
                    || technique.AnimName.Contains("down", StringComparison.OrdinalIgnoreCase)
                    || technique.AnimName.Contains("bigchop", StringComparison.OrdinalIgnoreCase) ? technique : disabled;
            }
        }
        public IEnumerator<CombatTechnique> GetEnumerator()
        {
            for (int i = 0; i < Count; i++) yield return this[i];
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal static void Install(ModContext context)
    {
        if (AccessTools.Method(typeof(CombatSystem), "StartAttack") is not { } attack) return;
        var patch = new HarmonyMethod(typeof(StockTargetGroundSwing), nameof(PreferDescending));
        try { context.Harmony.Patch(attack, transpiler: patch); }
        catch (Exception error)
        {
            context.Harmony.Unpatch(attack, patch.method);
            context.Log($"Brutality descending attack preference unavailable: {error.GetBaseException().Message}");
        }
    }

    static IEnumerable<CodeInstruction> PreferDescending(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var native = AccessTools.Method(typeof(TechniqueChooser), nameof(TechniqueChooser.ChooseAttack));
        var matches = Enumerable.Range(0, code.Count).Where(i => code[i].Calls(native)).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException("Unexpected stock attack technique call.");
        int index = matches[0];
        var call = code[index];
        var world = CodeInstruction.LoadArgument(1);
        world.labels.AddRange(call.labels);
        call.labels.Clear();
        code.InsertRange(index, [world, CodeInstruction.LoadArgument(2), CodeInstruction.LoadArgument(3)]);
        call.operand = AccessTools.Method(typeof(StockTargetGroundSwing), nameof(Choose));
        return StockTargetHooks.ExpandBranches(code);
    }

    static int Choose(IReadOnlyList<CombatTechnique> all, in AttackQuery query, float roll, out float reach,
        World world, int attacker, in CombatSlot state)
    {
        if (all.Count > 0 && state.Picked && !query.Animal
            && StockTargetSystem.TryFocus(world.Characters, attacker, state.TargetSlot, out var order, out var focus)
            && StockTargetSystem.Allowed(world.Characters, attacker, order, focus)
            && StockTargetSystem.GroundTarget(world, order.Target))
        {
            // An armed character keeps its weapon; only the usual native fallback may choose a barehanded strike.
            var descending = query.Category == (int)WeaponCategory.Unarmed ? query : query with { UnarmedSkill = float.NegativeInfinity };
            int pick = TechniqueChooser.ChooseAttack(choices.GetValue(all, static techniques => new(techniques)), descending, roll, out reach);
            if (pick >= 0) return pick;
        }
        return TechniqueChooser.ChooseAttack(all, query, roll, out reach);
    }
}
