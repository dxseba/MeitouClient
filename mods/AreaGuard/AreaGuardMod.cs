using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Aggro;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Dialogue;

namespace Meitou.AreaGuard;

/// <summary>
/// Port of Area Guard (workshop 3769985477): once one character blocks a melee blow, the same blow (attacker, swing, blow index, tick) does not land
/// on the bodies its sweep reaches after it. The <see cref="AreaGuardSystem"/> hears blocks as a defence observer and spares the later bodies
/// as a hit filter.
/// </summary>
public sealed class AreaGuardMod : IMod
{
    /// <summary>
    /// The mod's settings (the original's <c>AreaGuard.ini</c>).
    /// </summary>
    internal static readonly AreaGuardSettings Settings = new();

    /// <summary>
    /// The mod's log, for <see cref="AreaGuardSettings.DebugLogging"/>.
    /// </summary>
    static Action<string> s_log = _ => { };

    public void Initialize(ModContext context)
    {
        s_log = context.Log;
        context.AddSettings(Settings);
        context.AddSystem("area-guard", Build, SystemAnchor.End);
        context.Log("Area Guard initialized");
    }

    /// <summary>
    /// The world's Area Guard system.
    /// </summary>
    static AreaGuardSystem Build(SystemBuildContext build) =>
        new(Settings, build.Find<CombatSystem>(), build.Find<AggroSystem>(), build.Find<DialogueSystem>(), s_log);
}
