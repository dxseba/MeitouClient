using Meitou.Modding;

namespace Meitou.Mercenarie;

/// <summary>The original's options the port carries (<c>Options.ini</c> and its in-game options page).</summary>
public sealed class GuildSettings
{
    [Setting("Contract rewards", Hint = "Scales Cats rewards only (the original's reward setting, 50 to 150 %); Guild XP and reputation stay.",
        Min = 50, Max = 150, Step = 5)]
    public int RewardPercent { get; set; } = 100;

    [Setting("Guild House clients", Hint = "Clients walk into an operational Guild House with work to offer.")]
    public bool HouseClients { get; set; } = true;
}
