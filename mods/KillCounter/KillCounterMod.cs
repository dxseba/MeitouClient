using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.KillCounter;

/// <summary>Counts each character's kills and knockouts and shows the selected character's tally on the HUD.</summary>
public sealed class KillCounterMod : IMod
{
    public void Initialize(ModContext context)
    {
        context.AddSystem("kills", _ => new KillCounterSystem(), SystemAnchor.AfterPart(StandardParts.Combat));
        HudAdapter.Install(context);
        context.AddUi(new KillCounterUi());
        context.Log("Kill Counter initialized");
    }
}
