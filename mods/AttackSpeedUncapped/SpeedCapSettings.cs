using System.Globalization;
using Meitou.Modding;

namespace Meitou.AttackSpeedUncapped;

/// <summary>
/// The original's <c>SpeedCapTuner.ini</c> values (<c>[SpeedCapTuner]</c> AttackCap 4.8, BlockCap 4.0, Lang, Hidden), under Mod settings; the cards
/// set them too.
/// </summary>
public sealed class SpeedCapSettings
{
    public const string English = "English", Chinese = "Chinese", Unlimited = "Unlimited";

    /// <summary>The original's attack cap tiers (the last, 99, shown as unlimited).</summary>
    internal static readonly string[] AttackTiers = ["1.2", "2.4", "4.8", "9.6", Unlimited];

    /// <summary>The original's block cap tiers.</summary>
    internal static readonly string[] BlockTiers = ["4", "8", "12", Unlimited];

    /// <summary>The value an unlimited tier stands for (the original's 99).</summary>
    const float UnlimitedCap = 99;

    [Setting("Attack cap", Hint = "The fastest an attack may play once it reached the game's 1.2 cap.",
        Options = ["1.2", "2.4", "4.8", "9.6", Unlimited])]
    public string AttackCap { get; set; } = "4.8";

    [Setting("Block cap", Hint = "The fastest a block may play once it reached the game's cap.", Options = ["4", "8", "12", Unlimited])]
    public string BlockCap { get; set; } = "4";

    [Setting("Card language", Options = [English, Chinese])]
    public string Language { get; set; } = English;

    [Setting("Hide the cards", Hint = "The toggle key (Ctrl+J by default, Controls page) shows and hides them too.")]
    public bool Hidden { get; set; }

    /// <summary>The attack cap as a number.</summary>
    internal float Attack => Cap(AttackCap);

    /// <summary>The block cap as a number.</summary>
    internal float Block => Cap(BlockCap);

    internal bool InChinese => Language == Chinese;

    static float Cap(string tier) => tier == Unlimited ? UnlimitedCap : float.Parse(tier, CultureInfo.InvariantCulture);
}
