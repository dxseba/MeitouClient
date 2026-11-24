using Meitou.Modding;

namespace Meitou.GuardBreakBash;

/// <summary>
/// The original's <c>GuardBreakBash.ini</c> as shipped (release candidate), under Mod settings: same keys and defaults. The curve values of an
/// effect are used only with the Custom preset (as the ini says); the weights only with the Weighted reference.
/// </summary>
public sealed class GuardBreakSettings
{
    public const string Custom = "Custom", Recommended = "Recommended", Growth = "Growth", Vanilla = "Vanilla";
    public const string MaxAll = "Max of STR, DEX, TGH", MaxStrDex = "Max of STR, DEX", Weighted = "Weighted average";

    internal static readonly string[] Presets = [Custom, Recommended, Growth, Vanilla];
    internal static readonly string[] References = [MaxAll, MaxStrDex, Weighted];

    [Setting("Enabled")]
    public bool Enabled { get; set; } = true;

    [Setting("Balance preset", Hint = "Recommended, Growth and Vanilla share 17.5 % at equal stats, 2.5 % minimum and 47.5 % maximum.",
        Options = [Custom, Recommended, Growth, Vanilla])]
    public string BalancePreset { get; set; } = Recommended;

    [Setting("Allow both effects", Hint = "Off: Guard Break is skipped when Bash was applied on the same block.")]
    public bool AllowBothEffects { get; set; } = true;

    [Setting("Effective stats", Hint = "On: current stats with equipment and injuries. Off: base stats.")]
    public bool EffectiveStats { get; set; } = true;

    [Setting("Ignore already stumbling", Hint = "A character already stumbling is not staggered again.")]
    public bool IgnoreAlreadyStumbling { get; set; } = true;

    [Setting("Template max age (ms)", Hint = "How old the last real stagger of a character (or of anyone) may be to serve as the model.", Min = 1,
        Max = 600000, Step = 1000)]
    public int TemplateMaxAgeMs { get; set; } = 10000;

    [Setting("Minimum template observations", Min = 1, Max = 100)]
    public int MinimumTemplateObservations { get; set; } = 1;

    [Setting("Log rolls", Hint = "Writes each roll to the mod log.")]
    public bool LogRolls { get; set; }

    [Setting("Log stagger", Hint = "Writes each applied or skipped stagger to the mod log.")]
    public bool LogStagger { get; set; }

    [Setting("Max log lines", Min = 10, Max = 100000, Step = 10)]
    public int MaxLines { get; set; } = 1000;

    public bool IsCustom => BalancePreset == Custom;

    #region Bash

    [Setting("Bash", Hint = "On a successful block, the attacker may stumble.", SubGroup = "Bash")]
    public bool BashEnabled { get; set; } = true;

    [Setting("Base chance (%)", Min = 0, Max = 100, Step = 0.5, SubGroup = "Bash", VisibleWhen = nameof(IsCustom))]
    public float BashBaseChance { get; set; } = 17.5f;

    [Setting("Minimum chance (%)", Min = 0, Max = 100, Step = 0.5, SubGroup = "Bash", VisibleWhen = nameof(IsCustom))]
    public float BashMinimumChance { get; set; } = 2.5f;

    [Setting("Maximum chance (%)", Min = 0, Max = 100, Step = 0.5, SubGroup = "Bash", VisibleWhen = nameof(IsCustom))]
    public float BashMaximumChance { get; set; } = 47.5f;

    [Setting("Negative mid difference", Min = 0, Max = 200, Step = 1, SubGroup = "Bash", VisibleWhen = nameof(IsCustom))]
    public float BashNegativeMidDifference { get; set; } = 30;

    [Setting("Negative mid chance (%)", Min = 0, Max = 100, Step = 0.5, SubGroup = "Bash", VisibleWhen = nameof(IsCustom))]
    public float BashNegativeMidChance { get; set; } = 7.5f;

    [Setting("Negative cap difference", Min = 0, Max = 200, Step = 1, SubGroup = "Bash", VisibleWhen = nameof(IsCustom))]
    public float BashNegativeCapDifference { get; set; } = 60;

    [Setting("Positive mid difference", Min = 0, Max = 200, Step = 1, SubGroup = "Bash", VisibleWhen = nameof(IsCustom))]
    public float BashPositiveMidDifference { get; set; } = 30;

    [Setting("Positive mid chance (%)", Min = 0, Max = 100, Step = 0.5, SubGroup = "Bash", VisibleWhen = nameof(IsCustom))]
    public float BashPositiveMidChance { get; set; } = 35;

    [Setting("Positive cap difference", Min = 0, Max = 200, Step = 1, SubGroup = "Bash", VisibleWhen = nameof(IsCustom))]
    public float BashPositiveCapDifference { get; set; } = 60;

    [Setting("Cooldown (ms)", Min = 0, Max = 60000, Step = 100, SubGroup = "Bash")]
    public int BashCooldownMs { get; set; } = 1500;

    [Setting("Force multiplier", Min = 0, Max = 10, Step = 0.1, SubGroup = "Bash")]
    public float BashForceMultiplier { get; set; } = 1;

    [Setting("Minimum force", Min = 0, Max = 1000, Step = 1, SubGroup = "Bash")]
    public float BashMinimumForce { get; set; } = 10;

    [Setting("Blocker force", Options = [MaxAll, MaxStrDex, Weighted], SubGroup = "Bash")]
    public string BashBlockerForce { get; set; } = MaxAll;

    [Setting("Blocker strength weight", Min = 0, Max = 10, Step = 0.1, SubGroup = "Bash")]
    public float BashBlockerStrengthWeight { get; set; } = 1;

    [Setting("Blocker dexterity weight", Min = 0, Max = 10, Step = 0.1, SubGroup = "Bash")]
    public float BashBlockerDexterityWeight { get; set; } = 1;

    [Setting("Blocker toughness weight", Min = 0, Max = 10, Step = 0.1, SubGroup = "Bash")]
    public float BashBlockerToughnessWeight { get; set; } = 1;

    [Setting("Attacker resistance", Options = [MaxAll, MaxStrDex, Weighted], SubGroup = "Bash")]
    public string BashAttackerResistance { get; set; } = MaxAll;

    [Setting("Attacker strength weight", Min = 0, Max = 10, Step = 0.1, SubGroup = "Bash")]
    public float BashAttackerStrengthWeight { get; set; } = 1;

    [Setting("Attacker dexterity weight", Min = 0, Max = 10, Step = 0.1, SubGroup = "Bash")]
    public float BashAttackerDexterityWeight { get; set; } = 1;

    [Setting("Attacker toughness weight", Min = 0, Max = 10, Step = 0.1, SubGroup = "Bash")]
    public float BashAttackerToughnessWeight { get; set; } = 1;

    #endregion

    #region Guard break

    [Setting("Guard Break", Hint = "On a successful block, the blocker may stumble.", SubGroup = "Guard Break")]
    public bool GuardBreakEnabled { get; set; } = true;

    [Setting("Base chance (%)", Min = 0, Max = 100, Step = 0.5, SubGroup = "Guard Break", VisibleWhen = nameof(IsCustom))]
    public float GuardBreakBaseChance { get; set; } = 17.5f;

    [Setting("Minimum chance (%)", Min = 0, Max = 100, Step = 0.5, SubGroup = "Guard Break", VisibleWhen = nameof(IsCustom))]
    public float GuardBreakMinimumChance { get; set; } = 2.5f;

    [Setting("Maximum chance (%)", Min = 0, Max = 100, Step = 0.5, SubGroup = "Guard Break", VisibleWhen = nameof(IsCustom))]
    public float GuardBreakMaximumChance { get; set; } = 47.5f;

    [Setting("Negative mid difference", Min = 0, Max = 200, Step = 1, SubGroup = "Guard Break", VisibleWhen = nameof(IsCustom))]
    public float GuardBreakNegativeMidDifference { get; set; } = 30;

    [Setting("Negative mid chance (%)", Min = 0, Max = 100, Step = 0.5, SubGroup = "Guard Break", VisibleWhen = nameof(IsCustom))]
    public float GuardBreakNegativeMidChance { get; set; } = 7.5f;

    [Setting("Negative cap difference", Min = 0, Max = 200, Step = 1, SubGroup = "Guard Break", VisibleWhen = nameof(IsCustom))]
    public float GuardBreakNegativeCapDifference { get; set; } = 60;

    [Setting("Positive mid difference", Min = 0, Max = 200, Step = 1, SubGroup = "Guard Break", VisibleWhen = nameof(IsCustom))]
    public float GuardBreakPositiveMidDifference { get; set; } = 30;

    [Setting("Positive mid chance (%)", Min = 0, Max = 100, Step = 0.5, SubGroup = "Guard Break", VisibleWhen = nameof(IsCustom))]
    public float GuardBreakPositiveMidChance { get; set; } = 35;

    [Setting("Positive cap difference", Min = 0, Max = 200, Step = 1, SubGroup = "Guard Break", VisibleWhen = nameof(IsCustom))]
    public float GuardBreakPositiveCapDifference { get; set; } = 60;

    [Setting("Cooldown (ms)", Min = 0, Max = 60000, Step = 100, SubGroup = "Guard Break")]
    public int GuardBreakCooldownMs { get; set; } = 1500;

    [Setting("Force multiplier", Min = 0, Max = 10, Step = 0.1, SubGroup = "Guard Break")]
    public float GuardBreakForceMultiplier { get; set; } = 1;

    [Setting("Minimum force", Min = 0, Max = 1000, Step = 1, SubGroup = "Guard Break")]
    public float GuardBreakMinimumForce { get; set; } = 10;

    [Setting("Attacker force", Options = [MaxAll, MaxStrDex, Weighted], SubGroup = "Guard Break")]
    public string GuardBreakAttackerForce { get; set; } = MaxStrDex;

    [Setting("Attacker strength weight", Min = 0, Max = 10, Step = 0.1, SubGroup = "Guard Break")]
    public float GuardBreakAttackerStrengthWeight { get; set; } = 1;

    [Setting("Attacker dexterity weight", Min = 0, Max = 10, Step = 0.1, SubGroup = "Guard Break")]
    public float GuardBreakAttackerDexterityWeight { get; set; } = 1;

    [Setting("Attacker toughness weight", Min = 0, Max = 10, Step = 0.1, SubGroup = "Guard Break")]
    public float GuardBreakAttackerToughnessWeight { get; set; }

    [Setting("Blocker resistance", Options = [MaxAll, MaxStrDex, Weighted], SubGroup = "Guard Break")]
    public string GuardBreakBlockerResistance { get; set; } = MaxAll;

    [Setting("Blocker strength weight", Min = 0, Max = 10, Step = 0.1, SubGroup = "Guard Break")]
    public float GuardBreakBlockerStrengthWeight { get; set; } = 1;

    [Setting("Blocker dexterity weight", Min = 0, Max = 10, Step = 0.1, SubGroup = "Guard Break")]
    public float GuardBreakBlockerDexterityWeight { get; set; } = 1;

    [Setting("Blocker toughness weight", Min = 0, Max = 10, Step = 0.1, SubGroup = "Guard Break")]
    public float GuardBreakBlockerToughnessWeight { get; set; } = 1;

    #endregion
}
