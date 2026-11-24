using Meitou.Modding;

namespace Meitou.Struggle;

/// <summary>
/// The original's <c>Struggle.ini</c> (<c>[Struggle]</c>, written on first start with these defaults, <b>Verified</b> decompile of
/// <c>Struggle.dll</c>), under Mod settings, with the original's clamps as the sliders' ranges.
/// </summary>
public sealed class StruggleSettings
{
    public const string Auto = "Auto", Chinese = "Chinese", English = "English";

    [Setting("Enabled")]
    public bool Enabled { get; set; } = true;

    [Setting("Chance per stat point (%)", Hint = "Each point of Strength + Toughness above the carrier's adds this much to the 50 % base.",
        Min = 0.001, Max = 1000, Step = 0.1)]
    public float StepPerPoint { get; set; } = 1;

    [Setting("Minimum chance (%)", Min = 0, Max = 100, Step = 1, SubGroup = "Your characters")]
    public float MinChance { get; set; } = 5;

    [Setting("Maximum chance (%)", Min = 0, Max = 100, Step = 1, SubGroup = "Your characters")]
    public float MaxChance { get; set; } = 100;

    [Setting("Cooldown after a failure (s)", Min = 0, Max = 86400, Step = 1, SubGroup = "Your characters")]
    public int BaseCooldownSec { get; set; } = 10;

    [Setting("Extra cooldown after a failure (s)", Min = 0, Max = 86400, Step = 1, SubGroup = "Your characters")]
    public int FailExtraSec { get; set; }

    [Setting("Your characters only", Hint = "Off: the Struggle row is offered for any carried character.", SubGroup = "Your characters")]
    public bool PlayerOnly { get; set; } = true;

    [Setting("NPC struggle", Hint = "Carried NPCs try to break free on their own (Alt+7 by default).", SubGroup = "NPCs")]
    public bool NpcStruggle { get; set; }

    [Setting("NPC base cooldown (s)", Min = 5, Max = 86400, Step = 1, SubGroup = "NPCs")]
    public int NpcCooldownSec { get; set; } = 90;

    [Setting("NPC minimum chance (%)", Min = 0, Max = 100, Step = 1, SubGroup = "NPCs")]
    public float NpcMinChance { get; set; } = 5;

    [Setting("NPC maximum chance (%)", Min = 0, Max = 100, Step = 1, SubGroup = "NPCs")]
    public float NpcMaxChance { get; set; } = 100;

    [Setting("Failure punishment", Hint = "A failed struggle is slapped and knocked out (Alt+6 by default).", SubGroup = "Failure")]
    public bool FailPunish { get; set; } = true;

    [Setting("Slap damage (head)", Min = 0, Max = 100000, Step = 1, SubGroup = "Failure")]
    public float SlapDamage { get; set; } = 10;

    [Setting("Shortest knock-out (s)", Min = 0, Max = 30, Step = 1, SubGroup = "Failure")]
    public float KoMinSec { get; set; } = 5;

    [Setting("Longest knock-out (s)", Min = 0, Max = 30, Step = 1, SubGroup = "Failure")]
    public float KoMaxSec { get; set; } = 30;

    [Setting("Message seconds", Min = 1, Max = 60, Step = 1)]
    public int ToastSec { get; set; } = 4;

    [Setting("Language", Hint = "Auto follows the game's language.", Options = [Auto, Chinese, English])]
    public string Lang { get; set; } = Auto;
}
