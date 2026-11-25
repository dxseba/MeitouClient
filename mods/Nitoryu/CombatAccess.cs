using System.Reflection;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.Nitoryu;

/// <summary>
/// The engine members the port reads that are not public, bound once by name: <c>CombatSystem.Eff</c> (a stat as the combat formulas read it).
/// Without it the port reads the plain stat (logged).
/// </summary>
static class CombatAccess
{
    static Func<CharacterCold, StatsEnumerated, float>? s_eff;

    public static void Bind(Action<string> log)
    {
        const BindingFlags any = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var eff = typeof(CombatSystem).GetMethod("Eff", any, [typeof(CharacterCold), typeof(StatsEnumerated)]);
        try
        {
            s_eff = eff?.CreateDelegate<Func<CharacterCold, StatsEnumerated, float>>();
        }
        catch (ArgumentException e)
        {
            log($"CombatSystem.Eff changed: stats are read without injuries and gear ({e.Message})");
        }

        if (s_eff is null) log("CombatSystem.Eff not found: stats are read without injuries and gear");
    }

    /// <summary>The stat as combat reads it (gear, injuries, hunger, light).</summary>
    public static float Eff(CharacterCold cold, StatsEnumerated stat) => s_eff?.Invoke(cold, stat) ?? cold.Stats![stat];
}
