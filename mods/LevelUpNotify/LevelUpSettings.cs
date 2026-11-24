using System.Reflection;
using Meitou.Modding;

namespace Meitou.LevelUpNotify;

/// <summary>The original's F10 panel and ini settings, with its defaults (LevelUpNotify.ini) and ranges.</summary>
public sealed class LevelUpSettings
{
    public static readonly string[] Anchors =
        ["Top Left", "Top Center", "Top Right", "Middle Left", "Center", "Middle Right", "Bottom Left", "Bottom Center", "Bottom Right"];

    [Setting("Notifications", Hint = "Turns level up notifications on or off")]
    public bool Enabled { get; set; } = true;

    [Setting("Time Shown (s)", Hint = "How long each notification stays on screen, both fades included", Min = 0.25, Max = 60, Step = 0.25)]
    public float Duration { get; set; } = 3;

    [Setting("Fade (s)", Hint = "Fade in and fade out length, each (at most half the time shown)", Min = 0.01, Max = 30, Step = 0.01)]
    public float Fade { get; set; } = 0.3f;

    [Setting("Every N levels", Hint = "Only every Nth level. 1 = all, 5 = levels 5, 10, 15...", Min = 1, Max = 100)]
    public int Every { get; set; } = 1;

    [Setting("Position", Hint = "Which part of the screen the notifications appear in", Options =
        ["Top Left", "Top Center", "Top Right", "Middle Left", "Center", "Middle Right", "Bottom Left", "Bottom Center", "Bottom Right"])]
    public string Anchor { get; set; } = "Top Right";

    [Setting("Offset X", Hint = "Distance in pixels from the screen edge (at the reference height); ignored when centred", Min = -4000, Max = 4000)]
    public int OffsetX { get; set; } = 40;

    [Setting("Offset Y", Hint = "Distance in pixels from the screen edge (at the reference height); ignored when centred", Min = -4000, Max = 4000)]
    public int OffsetY { get; set; } = 20;

    [Setting("Color", Hint = "Color of the level up text", Options =
        ["Yellow", "White", "Green", "Cyan", "Orange", "Red", "Purple", "Gray", "Custom"])]
    public string Colour { get; set; } = "Yellow";

    [Setting("Custom Color (ARGB)", Hint = "Any text color as #AARRGGBB hex, as the original's textColor", MaxLength = 9,
        VisibleWhen = nameof(CustomColour))]
    public string TextColor { get; set; } = "#FFFFFF00";

    /// <summary>Whether the custom colour is in use (shows its text box).</summary>
    public bool CustomColour => Colour == "Custom";

    [Setting("Opacity (%)", Hint = "How see-through the text is. 100 = solid", Min = 5, Max = 100)]
    public int Opacity { get; set; } = 100;

    [Setting("UI Scale", Hint = "Scales everything up or down. 1.0 = normal", Min = 0.25, Max = 4, Step = 0.05)]
    public float Scale { get; set; } = 1;

    [Setting("Font Size", Hint = "Text height in pixels at the reference height, before the UI scale", Min = 8, Max = 200)]
    public float FontSize { get; set; } = 36;

    [Setting("Hide with game UI", Hint = "Hide notifications while the game interface is hidden")]
    public bool HideWithGameUi { get; set; } = true;

    [Setting("Whole squad (off = selected only)", Hint = "Everyone in your squad, or only the selected character")]
    public bool WholeSquad { get; set; } = true;

    [Setting("Most on screen", Hint = "Most notifications on screen at once; the oldest is removed first", Min = 1, Max = 64)]
    public int MaxShown { get; set; } = 8;

    [Setting("Check every (ms)", Hint = "How often character stats are checked", Min = 50, Max = 10000, Step = 50)]
    public int PollMs { get; set; } = 500;

    /// <summary>
    /// The F10 panel's "Show Preview" button as a switch: turning it on shows "Preview: Athletics Lv.42" and turns it off again.
    /// </summary>
    [Setting("Show Preview", Hint = "Shows a sample notification with the current settings")]
    public bool ShowPreview { get; set; }

    const string Layout = "Layout (ini only in the original)";

    /// <summary>
    /// <c>autoScale</c>: every pixel value times screen height / <see cref="ReferenceHeight"/>.
    /// </summary>
    [Setting("Scale with screen height", Hint = "Scales every size by screen height / reference height", SubGroup = Layout)]
    public bool AutoScale { get; set; } = true;

    /// <summary>
    /// <c>referenceHeight</c> (at least 1; the original puts 2160 back for less).
    /// </summary>
    [Setting("Reference height", Hint = "Screen height the pixel sizes are given at", Min = 1, Max = 8192, SubGroup = Layout,
        VisibleWhen = nameof(AutoScale))]
    public int ReferenceHeight { get; set; } = 2160;

    /// <summary>
    /// <c>toastWidth</c> (1 to 8192).
    /// </summary>
    [Setting("Box width", Hint = "Notification box width in pixels at the reference height", Min = 1, Max = 8192, SubGroup = Layout)]
    public int ToastWidth { get; set; } = 600;

    /// <summary>
    /// <c>toastHeight</c> (1 to 8192).
    /// </summary>
    [Setting("Box height", Hint = "Notification box height in pixels at the reference height", Min = 1, Max = 8192, SubGroup = Layout)]
    public int ToastHeight { get; set; } = 52;

    /// <summary>
    /// <c>toastSpacing</c> (0 to 8192).
    /// </summary>
    [Setting("Box gap", Hint = "Gap between stacked boxes in pixels at the reference height", Min = 0, Max = 8192, SubGroup = Layout)]
    public int ToastSpacing { get; set; } = 6;

    const string Muting = "Skills - switch off to mute", Mute = "Switch off to stop notifications for this skill";

    [Setting("Strength", Hint = Mute, SubGroup = Muting)] public bool Strength { get; set; } = true;
    [Setting("Melee Attack", Hint = Mute, SubGroup = Muting)] public bool MeleeAttack { get; set; } = true;
    [Setting("Labouring", Hint = Mute, SubGroup = Muting)] public bool Labouring { get; set; } = true;
    [Setting("Science", Hint = Mute, SubGroup = Muting)] public bool Science { get; set; } = true;
    [Setting("Engineering", Hint = Mute, SubGroup = Muting)] public bool Engineering { get; set; } = true;
    [Setting("Robotics", Hint = Mute, SubGroup = Muting)] public bool Robotics { get; set; } = true;
    [Setting("Weapon Smith", Hint = Mute, SubGroup = Muting)] public bool WeaponSmith { get; set; } = true;
    [Setting("Armour Smith", Hint = Mute, SubGroup = Muting)] public bool ArmourSmith { get; set; } = true;
    [Setting("Field Medic", Hint = Mute, SubGroup = Muting)] public bool FieldMedic { get; set; } = true;
    [Setting("Thievery", Hint = Mute, SubGroup = Muting)] public bool Thievery { get; set; } = true;
    [Setting("Turrets", Hint = Mute, SubGroup = Muting)] public bool Turrets { get; set; } = true;
    [Setting("Farming", Hint = Mute, SubGroup = Muting)] public bool Farming { get; set; } = true;
    [Setting("Cooking", Hint = Mute, SubGroup = Muting)] public bool Cooking { get; set; } = true;
    [Setting("Stealth", Hint = Mute, SubGroup = Muting)] public bool Stealth { get; set; } = true;
    [Setting("Athletics", Hint = Mute, SubGroup = Muting)] public bool Athletics { get; set; } = true;
    [Setting("Dexterity", Hint = Mute, SubGroup = Muting)] public bool Dexterity { get; set; } = true;
    [Setting("Melee Defence", Hint = Mute, SubGroup = Muting)] public bool MeleeDefence { get; set; } = true;
    [Setting("Toughness", Hint = Mute, SubGroup = Muting)] public bool Toughness { get; set; } = true;
    [Setting("Assassination", Hint = Mute, SubGroup = Muting)] public bool Assassination { get; set; } = true;
    [Setting("Swimming", Hint = Mute, SubGroup = Muting)] public bool Swimming { get; set; } = true;
    [Setting("Perception", Hint = Mute, SubGroup = Muting)] public bool Perception { get; set; } = true;
    [Setting("Katanas", Hint = Mute, SubGroup = Muting)] public bool Katanas { get; set; } = true;
    [Setting("Sabres", Hint = Mute, SubGroup = Muting)] public bool Sabres { get; set; } = true;
    [Setting("Hackers", Hint = Mute, SubGroup = Muting)] public bool Hackers { get; set; } = true;
    [Setting("Heavy Weapons", Hint = Mute, SubGroup = Muting)] public bool HeavyWeapons { get; set; } = true;
    [Setting("Blunt", Hint = Mute, SubGroup = Muting)] public bool Blunt { get; set; } = true;
    [Setting("Martial Arts", Hint = Mute, SubGroup = Muting)] public bool MartialArts { get; set; } = true;
    [Setting("Dodge", Hint = Mute, SubGroup = Muting)] public bool Dodge { get; set; } = true;
    [Setting("Polearms", Hint = Mute, SubGroup = Muting)] public bool Polearms { get; set; } = true;
    [Setting("Crossbows", Hint = Mute, SubGroup = Muting)] public bool Crossbows { get; set; } = true;
    [Setting("Friendly Fire", Hint = Mute, SubGroup = Muting)] public bool FriendlyFire { get; set; } = true;
    [Setting("Lockpicking", Hint = Mute, SubGroup = Muting)] public bool Lockpicking { get; set; } = true;
    [Setting("Crossbow Smith", Hint = Mute, SubGroup = Muting)] public bool CrossbowSmith { get; set; } = true;

    /// <summary>The per-skill switches, in <see cref="Skills.Watched"/> order (the property is the skill's name without spaces).</summary>
    readonly PropertyInfo[] _switches =
        [.. Skills.Watched.Select(s => typeof(LevelUpSettings).GetProperty(s.Name.Replace(" ", "", StringComparison.Ordinal))!)];

    /// <summary>Whether skill <paramref name="index"/> of <see cref="Skills.Watched"/> notifies.</summary>
    public bool Notifies(int index) => (bool)_switches[index].GetValue(this)!;

    /// <summary>
    /// Screen pixels per ini pixel: screen height / reference height when scaling with it, else 1, times the UI scale.
    /// </summary>
    public float PixelScale(float screenHeight) => (AutoScale ? screenHeight / Math.Max(1, ReferenceHeight) : 1) * Scale;

    /// <summary>The fade, held to half the time shown and at least a thousandth of a second, as the original's.</summary>
    public float FadeSeconds => Math.Max(0.001f, Math.Min(Fade, Duration / 2));
}
