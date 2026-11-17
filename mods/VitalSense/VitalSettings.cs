using Meitou.Modding;

namespace Meitou.VitalSense;

/// <summary>The original's mod-config.json keys that Meitou can honour, same defaults (Vital Sense README "Config").</summary>
public sealed class VitalSettings
{
    [Setting("Vital Sense", Hint = "Markers over downed characters (ZZ, RC, DY, PD, DE) and a bounty symbol.")]
    public bool Enabled { get; set; } = true;

    [Setting("Always on", Hint = "Show the markers without holding the Highlight key (Alt).")]
    public bool AlwaysOn { get; set; }

    [Setting("Max distance", Hint = "World units from the camera's centre, measured flat.", Min = 100, Max = 10000, Step = 100)]
    public float MaxDistance { get; set; } = 3500;

    [Setting("Marker budget", Hint = "At most this many markers, nearest first.", Min = 8, Max = 256, Step = 8)]
    public int Budget { get; set; } = 64;

    [Setting("State text", Hint = "Show the state letters.")]
    public bool ShowText { get; set; } = true;

    [Setting("Unconscious (ZZ)")]
    public bool Unconscious { get; set; } = true;

    [Setting("Recovery coma (RC)")]
    public bool RecoveryComa { get; set; } = true;

    [Setting("Dying (DY)")]
    public bool Dying { get; set; } = true;

    [Setting("Playing dead (PD)")]
    public bool PlayingDead { get; set; } = true;

    [Setting("Dead (DE)")]
    public bool Dead { get; set; } = true;

    [Setting("Bounty symbol", Hint = "A $ before the state, coloured by the character's total bounty.")]
    public bool ShowBounty { get; set; } = true;

    [Setting("Bounty on everyone", Hint = "Also show the $ over standing characters with a bounty.")]
    public bool BountyOnAll { get; set; } = true;

    [Setting("Outline downed", Hint = "While Alt is held, add downed characters to the game's hover outline (needs Settings: Hover outline).")]
    public bool Outline { get; set; } = true;

    /// <summary>Whether a marker of <paramref name="state"/> is switched on.</summary>
    public bool Shows(VitalState state) => state switch
    {
        VitalState.Unconscious => Unconscious,
        VitalState.RecoveryComa => RecoveryComa,
        VitalState.Dying => Dying,
        VitalState.PlayingDead => PlayingDead,
        VitalState.Dead => Dead,
        _ => false,
    };
}
