using Meitou.Modding;

namespace Meitou.Disarm;

/// <summary>
/// The original's Options tab, under Mod settings; same labels, names and defaults (dafitime/disarm README "Settings"), the labels translated
/// from the original's DLL (<see cref="OriginalLabels"/>).
/// </summary>
public sealed class DisarmSettings
{
    [Setting("Enable Disarm", Hint = "Master toggle for Disarm, Kick and the knockout weapon drop.")]
    public bool Enabled { get; set; } = true;

    [Setting("Chance Multiplier", Hint = "Scales the Disarm success chance on sleeping or unaware targets.", Min = 0, Max = 3, Step = 0.05)]
    public float ChanceMultiplier { get; set; } = 1;

    [Setting("Destroy Instead Of Drop", Hint = "Delete the weapon instead of dropping it.")]
    public bool Destroy { get; set; }

    [Setting("Enable Kick", Hint = "Right-click a loose item on the ground: Pick up, or Kick it away.")]
    public bool Kick { get; set; } = true;

    [Setting("Kick Distance", Hint = "Base distance a kicked item travels (plus up to 6 at random).", Min = 5, Max = 100, Step = 1)]
    public float KickDistance { get; set; } = 25;

    [Setting("Enable KO Weapon Drop", Hint = "A character knocked out may drop the weapon it was wielding.")]
    public bool KnockoutDrop { get; set; } = true;

    [Setting("KO Drop Chance Multiplier", Hint = "Scales the knockout drop chance.", Min = 0, Max = 3, Step = 0.05)]
    public float KnockoutDropMultiplier { get; set; } = 1;

    [Setting("Allow Stolen Flag", Hint = "Dropped weapons stay owned by the target's faction, so picking them up can count as theft.")]
    public bool StolenFlag { get; set; } = true;

    [Setting("Allow On Own Squad", Hint = "Lets Disarm and the knockout drop apply to your own characters too.")]
    public bool OwnSquad { get; set; }

    [Setting("Allow AI Rearm (Beta)", Hint = "A character you disarmed walks to the nearest weapon on the ground once it is back on its feet.")]
    public bool Rearm { get; set; }
}
