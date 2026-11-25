using Meitou.Game.Host.Input;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Population;

namespace Meitou.Mercenarie;

/// <summary>
/// Port of The Mercenarie (Workshop 3798671709, its plugin GuildEscortContracts.dll): bartenders and Guild House clients offer the guild's
/// contracts (escorts, caravans, scientific expeditions, bounty hunts, message deliveries), priced and rewarded by the original's tables, paid in
/// Cats, Guild XP (levels 0 to 10, then prestige) and town reputation, tracked in the guild window (J). The original's
/// <c>Guild Escort Contracts.mod</c> (a required item) carries the clients, caravans, scientists, wanted groups and chairs. README "Facts".
/// </summary>
public sealed class MercenarieMod : IMod
{
    public void Initialize(ModContext context)
    {
        var settings = new GuildSettings();
        context.AddSettings(settings);
        context.AddSystem("guild", build => new GuildSystem(context, settings, context.Population, build.Find<PopulationSystem>()), SystemAnchor.End);
        context.AddUi(new GuildWindow(context));
        context.AddKeyAction("guild", "Open the Mercenarie guild window", _ => GuildWindow.Toggle(), Binding.Of(Key.J));
        if (context.Data.Find(GuildSystem.CaravanTemplate) is null)
            context.Log("the original's Guild Escort Contracts.mod is not enabled: contracts cannot place their groups");

        if (!GuildSystem.EstateAvailable) context.Log("FixtureRegistry members not found: leases are off (the port needs an update)");

        context.Log("The Mercenarie initialized");
    }
}
