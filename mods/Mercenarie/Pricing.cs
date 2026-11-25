namespace Meitou.Mercenarie;

/// <summary>
/// The original's reward, XP and level tables (README "Facts": <c>FUN_180033860</c> prices, <c>FUN_180017860</c> XP, <c>FUN_180017410</c> levels,
/// <c>FUN_180027670</c> the reward setting; constants read from the DLL's data).
/// </summary>
static class Pricing
{
    /// <summary>XP needed for levels 1 to 10 (level 0 below 300).</summary>
    static readonly int[] LevelXp = [0, 300, 800, 1550, 2550, 3850, 5500, 7500, 9900, 12750, 16100];

    public static int MaxLevelXp => LevelXp[^1];

    public static int LevelOf(int xp)
    {
        int level = 0;
        while (level < 10 && LevelXp[level + 1] <= xp) level++;

        return level;
    }

    public static int XpFor(int level) => LevelXp[Math.Clamp(level, 0, 10)];

    /// <summary>The reward before negotiation and the reward setting.</summary>
    public static int Reward(ContractKind kind, float km, int danger, int tier, int group, bool prestigious) =>
        kind is ContractKind.Expedition or ContractKind.Bounty
            ? Legacy(kind, km, danger, tier, prestigious)
            : Price85(kind, km, danger, group, prestigious);

    /// <summary>
    /// Escort, caravan and mail (the "PRICE85" offers): base 2500 / 3000 / 1500 plus 130 / 90 / 75 Cats a km (at most 100000), plus 5 % a group
    /// member past the first (at most 25 %, not for mail), x the danger's 100 / 115 / 135 / 160 / 200 %, +20 % for a prestigious contract.
    /// </summary>
    static int Price85(ContractKind kind, float km, int danger, int group, bool prestigious)
    {
        int basePay = kind switch { ContractKind.Mail => 1500, ContractKind.Escort => 2500, _ => 3000 };
        int rate = kind switch { ContractKind.Mail => 75, ContractKind.Escort => 130, _ => 90 };
        int subtotal = basePay + (int)Math.Min(rate * (double)Math.Max(0, km) + 0.5, 100000);
        if (kind != ContractKind.Mail) subtotal += Math.Min(25, group * 5 - 5) * subtotal / 100;

        int[] dangerPct = [100, 100, 115, 135, 160, 200];
        int total = (int)((dangerPct[Math.Clamp(danger, 1, 5)] * (long)subtotal + 50) / 100);
        return Cap(total + (prestigious ? total * 20 / 100 : 0));
    }

    /// <summary>
    /// Expedition and bounty (the older pricing): base 3750 plus 50 Cats a km, x the danger's 1 / 1.25 / 1.6 / 2.1 / 2.8, x the tier's
    /// 1 / 1.25 / 1.6, +20 % prestigious; at least 3000 (expedition) or 1000 (bounty).
    /// </summary>
    static int Legacy(ContractKind kind, float km, int danger, int tier, bool prestigious)
    {
        float[] dangerF = [1, 1, 1.25f, 1.6f, 2.1f, 2.8f];
        float[] tierF = [1, 1.25f, 1.6f, 2];
        int sum = 3750 + (int)(50 * Math.Max(0, km));
        int total = (int)(sum * dangerF[Math.Clamp(danger, 1, 5)] + 0.5f);
        total = (int)(total * tierF[Math.Clamp(tier, 0, 3)] + 0.5f);
        total += prestigious ? total * 20 / 100 : 0;
        return Math.Max(Cap(total), kind == ContractKind.Expedition ? 3000 : 1000);
    }

    /// <summary>A negotiated price: the percentage within -20..+25, at most 100000.</summary>
    public static int Negotiated(int reward, int percent) => Cap(reward + Math.Clamp(percent, -20, 25) * reward / 100);

    /// <summary>The paid total under the reward setting (50 to 150 %, rounded).</summary>
    public static int Paid(int reward, int settingPercent) =>
        reward < 1 ? 0 : (int)Math.Min(int.MaxValue - 1, (Math.Clamp(settingPercent, 50, 150) * (long)reward + 50) / 100);

    /// <summary>
    /// Guild XP of a completed contract: base 35 / 45 / 60 / 35 / 25 (escort, caravan, expedition, bounty, mail) plus a km travelled / 5 (40 from
    /// 200 km), x the danger's 1 / 1.1 / 1.25 / 1.5 / 1.75, +10 with no losses, rounded.
    /// </summary>
    public static int Xp(ContractKind kind, float kmTravelled, int danger, bool noLosses)
    {
        int basis = kind switch { ContractKind.Caravan => 45, ContractKind.Expedition => 60, ContractKind.Mail => 25, _ => 35 };
        int distance = kmTravelled < 0 ? 0 : kmTravelled < 200 ? (int)(kmTravelled / 5) : 40;
        double[] mult = [1, 1, 1.1, 1.25, 1.5, 1.75];
        return (int)Math.Floor((basis + distance) * mult[Math.Clamp(danger, 1, 5)] + (noLosses ? 10 : 0) + 0.5);
    }

    /// <summary>The Cats to XP exchange: 120 Guild XP for each whole 10000 Cats.</summary>
    public static int ExchangeXp(long cats) => (int)Math.Min(int.MaxValue / 2, cats / 10000 * 120);

    static int Cap(int total) => Math.Clamp(total, 0, 100000);
}
