using System.Globalization;
using System.Numerics;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.PoshDamageNumbers;

/// <summary>
/// Port of Posh Damage Numbers (workshop 3821781631; rules from its KenshiLua script <c>damage_numbers.lua</c> and settings <c>dn_settings.lua</c>):
/// a "-N" number over a character hit near your people, coloured by the hit's share of the part's health (warm white, gold, orange; red for your
/// own), popping in with a white flash, floating up in an arc and fading; hits within the merge window add up; KNOCKOUT, SEVERED and KILLED words.
/// Health lost and regained comes from the engine's health changes (<see cref="HitWatch"/>), numbers are animated on the host once a frame
/// (<see cref="Floaters"/>) and drawn in the HUD overlay (<see cref="NumbersUi"/>).
/// </summary>
public sealed class PoshDamageNumbersMod : IMod
{
    internal static readonly DamageSettings Settings = new();

    public void Initialize(ModContext context)
    {
        context.AddSettings(Settings);
        context.AddSystem("hits", _ => new HitWatch(Settings), SystemAnchor.End);
        context.AddFrame(Floaters.Frame);
        context.AddUi(new NumbersUi());
        context.Log("Posh Damage Numbers initialized");
    }
}

/// <summary>The original's Mod Config Menu settings that apply to hits (<b>Verified</b>, <c>dn_settings.lua</c>: keys, ranges and defaults).</summary>
public sealed class DamageSettings
{
    [Setting("Enabled", Hint = "Enabled: floating damage numbers. Turn the game's own damage floaters off to see one set.")]
    public bool Enabled { get; set; } = true;

    [Setting("Own squad damage", Hint = "ShowOwnSquadDamage: numbers when your own people are hurt (red).")]
    public bool ShowOwnSquadDamage { get; set; } = true;

    [Setting("Fight range (m)", Hint = "FightRange: hits farther than this from your people get no numbers, unless your people are in the fight.",
        Min = 10, Max = 1000, Step = 10)]
    public int FightRange { get; set; } = 100;

    [Setting("Show words", Hint = "ShowWords: KNOCKOUT / SEVERED / KILLED.")]
    public bool ShowWords { get; set; } = true;

    [Setting("Show healing", Hint = "ShowHealing: green +N when a character heals a lot at once.")]
    public bool ShowHealing { get; set; }

    [Setting("Healing threshold", Hint = "HealingThreshold: minimum health regained within one second to show +N.", Min = 1, Max = 50)]
    public int HealingThreshold { get; set; } = 5;

    [Setting("Minimum damage", Hint = "MinDamage: smaller hits are ignored.", Min = 1, Max = 50)]
    public int MinDamage { get; set; } = 1;

    [Setting("Number size", Hint = "NumberSize: number height as a share of the character's height on screen.", Min = 0.1, Max = 0.6, Step = 0.05)]
    public float NumberSize { get; set; } = 0.3f;

    [Setting("Smallest number (px)", Hint = "MinNumberPx: at 1080p, scaled with the screen height.", Min = 10, Max = 80)]
    public int MinNumberPx { get; set; } = 22;

    [Setting("Largest number (px)", Hint = "MaxNumberPx: at 1080p, scaled with the screen height.", Min = 20, Max = 140)]
    public int MaxNumberPx { get; set; } = 50;

    [Setting("Duration", Hint = "Duration: how long numbers stay (1 = normal).", Min = 0.5, Max = 2.5, Step = 0.1)]
    public float Duration { get; set; } = 1;

    [Setting("Rise", Hint = "Rise: how high numbers float (0 = in place).", Min = 0, Max = 2.5, Step = 0.1)]
    public float Rise { get; set; } = 1;

    [Setting("Flash", Hint = "Flash: white flash when a number appears.")]
    public bool Flash { get; set; } = true;

    [Setting("Shake", Hint = "Shake: big hits shake for a moment.")]
    public bool Shake { get; set; } = true;

    [Setting("Merge window (s)", Hint = "MergeWindow: hits closer than this on one target add up.", Min = 0, Max = 1, Step = 0.02)]
    public float MergeWindow { get; set; } = 0.22f;

    [Setting("Light hits colour", Hint = "ColourLight: #RRGGBB for small hits; an invalid value draws #FFF5DB.", MaxLength = 7, SubGroup = "Colours")]
    public string ColourLight { get; set; } = "#FFF5DB";

    [Setting("Medium hits colour", Hint = "ColourMedium: #RRGGBB for solid hits; an invalid value draws #FFD140.", MaxLength = 7, SubGroup = "Colours")]
    public string ColourMedium { get; set; } = "#FFD140";

    [Setting("Heavy hits colour", Hint = "ColourHeavy: #RRGGBB for heavy hits; an invalid value draws #FF7A1F.", MaxLength = 7, SubGroup = "Colours")]
    public string ColourHeavy { get; set; } = "#FF7A1F";

    [Setting("Your people colour", Hint = "ColourSquad: #RRGGBB for damage to your own people; an invalid value draws #FF4040.", MaxLength = 7, SubGroup = "Colours")]
    public string ColourSquad { get; set; } = "#FF4040";

    [Setting("Healing colour", Hint = "ColourHeal: #RRGGBB for +N healing; an invalid value draws #6EE678.", MaxLength = 7, SubGroup = "Colours")]
    public string ColourHeal { get; set; } = "#6EE678";

    [Setting("KNOCKOUT colour", Hint = "ColourKO: #RRGGBB for the KNOCKOUT word; an invalid value draws #96BFFF.", MaxLength = 7, SubGroup = "Colours")]
    public string ColourKo { get; set; } = "#96BFFF";

    [Setting("SEVERED colour", Hint = "ColourLimb: #RRGGBB for the SEVERED word; an invalid value draws #FF593D.", MaxLength = 7, SubGroup = "Colours")]
    public string ColourLimb { get; set; } = "#FF593D";

    [Setting("KILLED colour", Hint = "ColourDead: #RRGGBB for the KILLED word; an invalid value draws #D91A1A.", MaxLength = 7, SubGroup = "Colours")]
    public string ColourDead { get; set; } = "#D91A1A";

    [Setting("Numbers on screen", Hint = "MaxActive: numbers on screen at once (the oldest is recycled).", Min = 10, Max = 150)]
    public int MaxActive { get; set; } = 60;

    internal Vector3 Light => Rgb(ColourLight, 0xFFF5DB);
    internal Vector3 Medium => Rgb(ColourMedium, 0xFFD140);
    internal Vector3 Heavy => Rgb(ColourHeavy, 0xFF7A1F);
    internal Vector3 Squad => Rgb(ColourSquad, 0xFF4040);
    internal Vector3 Heal => Rgb(ColourHeal, 0x6EE678);
    internal Vector3 Ko => Rgb(ColourKo, 0x96BFFF);
    internal Vector3 Limb => Rgb(ColourLimb, 0xFF593D);
    internal Vector3 Dead => Rgb(ColourDead, 0xD91A1A);

    /// <summary>A colour setting as rgb 0..1; an invalid value is its default (the original's <c>colour</c> kind).</summary>
    static Vector3 Rgb(string hex, uint fallback)
    {
        string digits = hex.Trim().TrimStart('#');
        bool valid = digits.Length == 6 && uint.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _);
        uint rgb = valid ? uint.Parse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) : fallback;
        return new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
    }
}
