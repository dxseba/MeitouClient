using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Aggro;
using Meitou.Simulation.Combat;

namespace Meitou.AreaGuard;

/// <summary>
/// Port of Area Guard (workshop 3769985477): once one character blocks a melee blow, the same blow (attacker, swing, blow index, tick) does not land
/// on the bodies its sweep reaches after it. A Harmony postfix on the combat's block records the block (<see cref="BlockHooks"/>), and the
/// <see cref="AreaGuardSystem"/> hit filter spares the later bodies.
/// </summary>
public sealed class AreaGuardMod : IMod
{
    internal static readonly AreaGuardSettings Settings = new();

    public void Initialize(ModContext context)
    {
        context.AddSettings(Settings);
        context.AddSystem("area-guard", Build, SystemAnchor.End);
        BlockHooks.Install(context.Harmony);
        context.Log("Area Guard initialized");
    }

    static AreaGuardSystem Build(SystemBuildContext build)
    {
        var combat = build.Find<CombatSystem>();
        var system = new AreaGuardSystem(Settings, combat, build.Find<AggroSystem>());
        if (combat is not null) BlockHooks.Bind(combat, system);

        return system;
    }
}
