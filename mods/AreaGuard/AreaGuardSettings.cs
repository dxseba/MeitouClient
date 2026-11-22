using Meitou.Modding;

namespace Meitou.AreaGuard;

/// <summary>Which later bodies of a blocked sweep are spared (the original's <c>ProtectionMode</c> 1..3).</summary>
public enum ProtectionMode
{
    /// <summary>Every later body of the same blow.</summary>
    All,

    /// <summary>Only those the blocker counts as allies.</summary>
    Friendly,

    /// <summary>Allies and neutrals: those the blocker does not count as enemies.</summary>
    NonEnemy,
}

/// <summary>How a spared body takes the blow (the original's <c>ProtectedReactionMode</c> 1..2).</summary>
public enum ReactionMode
{
    /// <summary>The blow misses it: no damage, no hit reaction.</summary>
    NoReaction,

    /// <summary>The blow lands with an empty damage packet, so the hit reaction may still play.</summary>
    HitReaction,
}

/// <summary>The original's <c>AreaGuard.ini</c>, under Mod settings; same names and defaults.</summary>
public sealed class AreaGuardSettings
{
    [Setting("Enable Area Guard", Hint = "A blow one character blocks does not land on the others its sweep reaches afterwards.")]
    public bool Enabled { get; set; } = true;

    [Setting("Protection mode", Hint = "All: every later body. Friendly: the blocker's allies. NonEnemy: anyone not its enemy.")]
    public ProtectionMode Protection { get; set; } = ProtectionMode.All;

    [Setting("Protected reaction", Hint = "NoReaction: the blow misses. HitReaction: no damage, but the hit reaction may play.")]
    public ReactionMode Reaction { get; set; } = ReactionMode.NoReaction;

    [Setting("Factor in disguises", Hint = "Friendly and NonEnemy read a disguised victim's uniform faction while the disguise holds.")]
    public bool FactorInDisguises { get; set; } = true;

    [Setting("Debug logging", Hint = "Writes a log line for every blow Area Guard spares.")]
    public bool DebugLogging { get; set; }
}
