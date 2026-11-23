using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.KillCounter;

/// <summary>Counts every character's kills, knockouts and limbs removed and shows them in the character stats window.</summary>
public sealed class KillCounterMod : IMod
{
    public void Initialize(ModContext context)
    {
        context.AddSystem("kills", _ => new KillCounterSystem(), SystemAnchor.AfterPart(StandardParts.Combat));
        context.AddFrame(frame => KillCounterUi.UiScale = frame.UiScale);
        context.AddUi(new KillCounterUi());
        context.Log("Kill Counter initialized");
    }
}
