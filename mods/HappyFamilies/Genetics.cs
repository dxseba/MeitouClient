using System.Numerics;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation.Bodies;

namespace Meitou.HappyFamilies;

/// <summary>
/// The original's numbers and tables (README "Facts"): times in game days, the races that have children, the stats a child inherits, the
/// sliders it mixes from its parents and the slider values it starts at as a child.
/// </summary>
static class Genetics
{
    public const double PregnancyDays = 0.5, ChildhoodDays = 10;
    public const double RetryMin = 0.1, RetryMax = 0.2;
    public const float PregnancyChance = 0.3f, ChildHealth = 0.3f, BellyGrowth = 200, StomachDefault = 100;

    /// <summary>
    /// The races that may have children, by RACE string id, with the name their <c>HF_Random&lt;name&gt;</c> template carries.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Races = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["17-gamedata.quack"] = "Greenlander",
        ["18019-gamedata.base"] = "Scorchlander",
        ["5276-chareditor.mod"] = "Shek",
        ["2603-gamedata.base"] = "Cannibal",
        ["50523-rebirth.mod"] = "CannibalSkav",
    };

    public const string TemplatePrefix = "HF_Random";

    /// <summary>
    /// The stats a child gets as a quarter of its parents' sum (half their average): the script's names that are KenshiLib <c>CharStats</c> fields
    /// with a Meitou stat. Its <c>strength</c>, <c>dexterity</c>, <c>athletics</c>, <c>meleeAttack</c> and <c>meleeDefence</c> are not fields (they are
    /// <c>_strength</c>, <c>_dexterity</c>, <c>_athletics</c>, <c>__meleeAttack</c>, <c>_meleeDefence</c>), so those are not inherited; nor are the
    /// fields with no stat storage (fitness = save key <c>endurance</c>, survival, arrow defence, bluff, tracking, climbing, doctor, the spirit and
    /// alignment values). Toughness is commented out in the script; Stealth is not listed.
    /// </summary>
    public static readonly StatsEnumerated[] InheritedStats =
    [
        StatsEnumerated.Perception, StatsEnumerated.Medic, StatsEnumerated.MassCombat, StatsEnumerated.Swimming, StatsEnumerated.Thieving,
        StatsEnumerated.Lockpicking, StatsEnumerated.Assassination, StatsEnumerated.Engineering,
        StatsEnumerated.SmithingWeapon, StatsEnumerated.SmithingArmour, StatsEnumerated.SmithingBow, StatsEnumerated.Robotics,
        StatsEnumerated.Science, StatsEnumerated.Labouring, StatsEnumerated.Farming, StatsEnumerated.Cooking, StatsEnumerated.Dodge,
        StatsEnumerated.FriendlyFire, StatsEnumerated.Katanas, StatsEnumerated.Sabres, StatsEnumerated.Hackers, StatsEnumerated.Blunt,
        StatsEnumerated.HeavyWeapons, StatsEnumerated.MartialArts, StatsEnumerated.Crossbows, StatsEnumerated.Turrets, StatsEnumerated.Polearms,
    ];

    /// <summary>
    /// The CHARACTER_APPEARANCE floats a child takes between its parents' (each its own random point between them).
    /// </summary>
    static readonly string[] MixedSliders = [.. new[]
    {
        "wide_cheekbones", "narrow_cheekbones", "big_mouth", "wide_mouth", "wide_nose", "long_nose", "arch_nose", "high_nose", "tiltup_nose",
        "tiltdown_nose", "high_brow", "low_brow", "tiltdown_brow", "tiltup_brow", "shallow_eyes", "narrow_eyes", "close_eyes", "tiltup_eyes",
        "tiltdown_eyes", "high_eyes", "big_eyes", "overbite", "underbite", "Head size", "Head shape", "Neck", "Neck width", "Neck length",
        "Height", "Frame", "Posture", "Shoulder set", "Neck position", "Leg length", "Shoulders", "Arm bulk", "Hands", "Chest", "Stomach",
        "Waist", "Hips", "Legs bulk", "Legs shape", "Feet", "wide_cheeks", "Cheeks", "high_brows", "low_brows", "wide_jaw", "narrow_jaw",
        "down_chin", "up_chin", "wide_chin", "Breast size", "Breast height", "Breast spacing", "bone_wide_cheekbones", "bone_narrow_cheekbones",
        "bone_big_mouth", "bone_wide_mouth", "bone_wide_nose", "bone_long_nose", "bone_arch_nose", "bone_high_nose", "bone_tiltup_nose",
        "bone_tiltdown_nose", "bone_tiltdown_brow", "bone_tiltup_brow", "bone_high_brow", "bone_low_brow", "bone_shallow_eyes",
        "bone_narrow_eyes", "bone_close_eyes", "bone_tiltup_eyes", "bone_tiltdown_eyes", "bone_high_eyes", "bone_overbite", "bone_underbite",
        "bone_horns_curved", "bone_horns_top_short", "bone_horns_bottom_short", "bone_horns_thin", "bone_horns_thick", "bone_horns_body_short",
        "bone_wide_cheeks", "bone_high_brows", "bone_low_brows", "bone_big_eyes", "bone_wide_jaw", "bone_narrow_jaw", "bone_outward_chin",
        "bone_inward_chin", "bone_down_chin", "bone_up_chin", "bone_wide_chin", "outward_chin", "inward_chin", "Eyes shallowness",
    }.Distinct()];

    /// <summary>
    /// The body sliders shared by both child tables.
    /// </summary>
    static readonly (string, float)[] ChildBody =
    [
        ("Frame", 80), ("Posture", 20), ("Neck position", 30), ("Shoulders", 90), ("Chest", 90), ("Breast size", 25), ("Breast height", 100),
        ("Waist", 100), ("Stomach", 90), ("Hips", 75), ("Legs bulk", 90), ("Legs shape", 100), ("Feet", 110), ("Neck", 80), ("Neck length", 90),
    ];

    /// <summary>
    /// A Shek child's slider values (its face sliders are the <c>bone_</c> ones).
    /// </summary>
    static readonly Dictionary<string, float> ShekChild = Table(
    [
        ("Height", 60), ("Leg length", 85), ("Arm bulk", 100), ("Head size", 110), ("Head shape", 120),
        ("bone_narrow_jaw", 0.33f), ("bone_wide_jaw", 0), ("bone_narrow_cheekbones", 0), ("bone_wide_cheekbones", 0), ("bone_up_chin", 0.92f),
        ("bone_down_chin", 0), ("bone_long_nose", 0), ("bone_arch_nose", -0.29f), ("bone_high_nose", -0.6f), ("bone_tiltup_nose", 0.88f),
        ("bone_tiltdown_nose", 0), ("bone_high_brows", 0.13f), ("bone_big_eyes", 1), ("bone_high_eyes", -1.04f), ("bone_underbite", 0),
        ("bone_overbite", 0.17f), ("bone_wide_cheeks", 0.97f), ("bone_wide_chin", -0.26f),
    ]);

    /// <summary>
    /// Any other race's child slider values.
    /// </summary>
    static readonly Dictionary<string, float> OtherChild = Table(
    [
        ("Height", 70), ("Leg length", 90), ("Arm bulk", 70), ("Head size", 90), ("Head shape", 110),
        ("narrow_jaw", 0.33f), ("wide_jaw", 0), ("narrow_cheekbones", 0), ("wide_cheekbones", 0), ("up_chin", 0.92f), ("down_chin", 0),
        ("long_nose", 0), ("arch_nose", -0.29f), ("high_nose", -0.6f), ("tiltup_nose", 0.88f), ("tiltdown_nose", 0), ("high_brows", 0.13f),
        ("big_eyes", 1.1f), ("high_eyes", -1.2f), ("underbite", 0.5f), ("overbite", 0), ("wide_cheeks", 0.97f), ("wide_chin", -0.26f),
    ]);

    static Dictionary<string, float> Table((string, float)[] own)
    {
        var table = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var (key, value) in ChildBody.Concat(own)) table[key] = value;
        return table;
    }

    /// <summary>
    /// The child slider table of a race (by its name, as the original tells a Shek).
    /// </summary>
    public static IReadOnlyDictionary<string, float> ChildSlidersOf(RaceData? race) => race?.Name == "Shek" ? ShekChild : OtherChild;

    static float Lerp(float a, float b, float t) => a + t * (b - a);

    /// <summary>
    /// Mixes the parents' looks into the child's record: the skin tone at one random point between theirs, each mixed slider either has at its
    /// own random point, and no beard. <paramref name="roll"/> gives numbers in [0, 1).
    /// </summary>
    public static void MixLook(FcsRecord mother, FcsRecord father, FcsRecord child, Func<float> roll)
    {
        if (mother.Vector3S.TryGetValue("Skin Tone", out var m) && father.Vector3S.TryGetValue("Skin Tone", out var f))
            child.Vector3S["Skin Tone"] = Vector3.Lerp(m, f, roll());

        foreach (string key in MixedSliders)
        {
            bool hasM = mother.Floats.TryGetValue(key, out float vm), hasF = father.Floats.TryGetValue(key, out float vf);
            if (!hasM && !hasF) continue;

            child.Floats[key] = Lerp(hasF ? vf : vm, hasM ? vm : vf, roll());
        }

        child.Strings["beard"] = "";
    }

    /// <summary>
    /// Starts the child's sliders at the child table's values and returns the values it will grow to: its mixed value where it has one (a
    /// slider it lacks stays at the child value).
    /// </summary>
    public static Dictionary<string, float> SetChildSliders(FcsRecord child, IReadOnlyDictionary<string, float> table)
    {
        var adult = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var (key, value) in table)
        {
            if (child.Floats.TryGetValue(key, out float grown)) adult[key] = grown;
            child.Floats[key] = value;
        }

        return adult;
    }

    /// <summary>
    /// The child's sliders at growth <paramref name="t"/> (0 a newborn, 1 an adult).
    /// </summary>
    public static void Grow(FcsRecord child, Lineage lineage, float t)
    {
        foreach (var (key, adult) in lineage.AdultSliders)
        {
            if (lineage.ChildSliders.TryGetValue(key, out float young)) child.Floats[key] = Lerp(young, adult, t);
        }
    }

    /// <summary>
    /// The child's inherited stats: a quarter of its parents' stored values summed.
    /// </summary>
    public static void MixStats(CharacterStats mother, CharacterStats father, CharacterStats child)
    {
        foreach (var stat in InheritedStats) child[stat] = (mother.Stored(stat) + father.Stored(stat)) / 4;
    }

    /// <summary>
    /// The father's surname (what follows his first space), or null when his name has none.
    /// </summary>
    public static string? Surname(string father)
    {
        int space = father.IndexOf(' ');
        return space >= 0 ? father[(space + 1)..] : null;
    }

    /// <summary>
    /// A body part's maximum at growth <paramref name="t"/>.
    /// </summary>
    public static float PartMax(float adult, float t) => adult * Lerp(ChildHealth, 1, t);
}
