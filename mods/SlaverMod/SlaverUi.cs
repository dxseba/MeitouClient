using Meitou.Game.Ui;
using Meitou.Modding;

namespace Meitou.SlaverMod;

/// <summary>
/// The original's order-menu rows (select one of your members, right-click the target): Enslave on someone down or caged; Free, Punish and Reward
/// on your slaves. And the selected slave's submission on the character panel (the original's bar over the slave). Interface thread only.
/// </summary>
sealed class SlaverUi(ModContext context, SlaverSettings settings) : IUiExtension
{
    public string Id => "dxseba.slavermod.ui";

    public void Register(UiRegistration registration)
    {
        registration.AddCharacterMenuVerbs(Rows);
        registration.AddCharacterPanelLines(Lines);
    }

    IEnumerable<UiMenuVerb> Rows(UiMenuContext menu)
    {
        if (!settings.Enabled || menu.Selected.Count == 0) yield break;

        var owner = menu.Selected[0];
        var target = menu.Target;
        if (owner == target.Id) yield break;

        if (SlaverSystem.Shown.ContainsKey(target.Id))
        {
            yield return Row(menu, "Free", t => new SlaveOrder(owner, target.Id, SlaveAct.Free) { Tick = t });
            if (settings.PunishEnabled) yield return Row(menu, "Punish", t => new SlaveOrder(owner, target.Id, SlaveAct.Punish) { Tick = t });
            if (settings.RewardEnabled)
                yield return Row(menu, $"Reward ({settings.RewardCost:0} cats)", t => new SlaveOrder(owner, target.Id, SlaveAct.Reward) { Tick = t });

            var act = target.Imprisoned ? SlaveAct.LetOut : SlaveAct.Cage;
            string cage = target.Imprisoned ? "Let out of the cage" : "Lock in a cage";
            yield return Row(menu, cage, t => new SlaveOrder(owner, target.Id, act) { Tick = t });
            yield break;
        }

        if (!target.IsPlayer && target.IsTrader && SlaveTrader(target.Faction))
        {
            yield return Row(menu, "I'm looking to buy slaves", t => new SlaveTradeOrder(owner, target.Id, Selling: false) { Tick = t });
            yield return Row(menu, "I want to sell some slaves", t => new SlaveTradeOrder(owner, target.Id, Selling: true) { Tick = t });
        }

        if (target.IsPlayer || target.Body is not { Dead: false } body || !(body.Unconscious || target.Imprisoned)) yield break;

        if (settings.SkeletonThrall && body.Unconscious && target.Appearance?.Race.GetBool("is robot") == true)
            yield return Row(menu, "Reprogram", t => new SlaveOrder(owner, target.Id, SlaveAct.Reprogram) { Tick = t });
        else
            yield return Row(menu, "Enslave", t => new EnslaveOrder(owner, target.Id) { Tick = t });
    }

    /// <summary>A faction whose name says it trades slaves (the Slave Traders, slavers' camps).</summary>
    bool SlaveTrader(int faction)
    {
        var relations = context.Population.Relations;
        return faction >= 0 && faction < relations.Count && relations.Factions[faction].Name.Contains("Slave", StringComparison.OrdinalIgnoreCase);
    }

    UiMenuVerb Row(UiMenuContext menu, string caption, Func<long, Simulation.SimCommand> command) =>
        new(context.Tr(caption), () => menu.Enqueue(command(menu.Tick)));

    IEnumerable<UiCharacterLine> Lines(UiCharacterContext panel)
    {
        if (!settings.Enabled || !SlaverSystem.Shown.TryGetValue(panel.Id, out float s)) yield break;

        yield return new UiCharacterLine(context.Tr("Submission"), $"{context.Tr(Word(s))} {s:0}", s / 100);
    }

    /// <summary>0-24 Defiant, 25-49 Restless, 50-74 Obedient, 75-100 Loyal.</summary>
    static string Word(float s) => s switch { < 25 => "Defiant", < 50 => "Restless", < 75 => "Obedient", _ => "Loyal" };
}
