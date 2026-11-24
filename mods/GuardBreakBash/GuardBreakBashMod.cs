using Meitou.Data.Gameplay.Combat;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.GuardBreakBash;

/// <summary>
/// Port of Guard Break &amp; Bash (Tata-Gari, Workshop 3775510993, GPLv3): on a blocked melee blow a stat roll may stagger the attacker (Bash)
/// and, independently, the blocker (Guard Break). The game has no hook for a blocked blow's stagger, so a Harmony postfix on
/// <c>CombatSystem.ApplyBlocked</c> runs the rolls (<see cref="CombatAccess"/>). The rules follow the original's DLL and ini (README "Facts").
/// </summary>
public sealed class GuardBreakBashMod : IMod
{
    internal static readonly GuardBreakSettings Settings = new();

    public void Initialize(ModContext context)
    {
        context.AddSettings(Settings);
        if (!CombatAccess.Install(context.Harmony, context.Log)) return;

        var constants = CombatConstants.FromDatabase(context.Data);
        context.AddSystem("guard-break-bash", _ => new GuardBreakSystem(constants, context.RngPurpose, context.Log), SystemAnchor.End);
        context.Log("Guard Break & Bash initialized");
    }
}
