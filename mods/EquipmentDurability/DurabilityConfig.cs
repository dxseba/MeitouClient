using System.Globalization;

namespace Meitou.EquipmentDurability;

/// <summary>
/// A rolled range: a new item rolls between <see cref="Min"/> and <see cref="Max"/>; <see cref="Cap"/> is its maximum (tooltip, repair limit).
/// </summary>
readonly record struct DurabilityRange(int Min, int Max, int Cap);

/// <summary>
/// The settings, read once at start from <c>Durability.cfg</c> in the mod folder (written with the defaults when missing), under the original's keys
/// and defaults (its <c>LoadConfig</c>), so a cfg from the original works unchanged.
/// </summary>
sealed class DurabilityConfig
{
    #region Values

    /// <summary>
    /// The file name, the original's.
    /// </summary>
    public const string FileName = "Durability.cfg";

    /// <summary>
    /// Master switch (<c>Enabled</c>).
    /// </summary>
    public bool Enabled { get; private set; } = true;

    /// <summary>
    /// <c>DurabilityRange</c>: any weapon whose class has no range of its own.
    /// </summary>
    public DurabilityRange Default { get; private set; } = new(100, 1000, 1000);

    /// <summary>
    /// The class ranges by <see cref="GearClass"/> (null: the class uses <see cref="Default"/>). The original sets the ranged and armour ones in code
    /// and the six melee classes only from the cfg lines its default file holds.
    /// </summary>
    public DurabilityRange?[] Classes { get; } = new DurabilityRange?[10];

    /// <summary>
    /// Points lost per player hit, shot, block and armour hit (<c>DamagePerHit</c>, <c>DamagePerShot</c>, <c>DamagePerBlock</c>, <c>DamagePerArmourHit</c>).
    /// </summary>
    public int PerHit { get; private set; } = 1;

    /// <summary>
    /// See the property above.
    /// </summary>
    public int PerShot { get; private set; } = 1;

    /// <summary>
    /// See the property above.
    /// </summary>
    public int PerBlock { get; private set; } = 1;

    /// <summary>
    /// See the property above.
    /// </summary>
    public int PerArmourHit { get; private set; } = 1;

    /// <summary>
    /// The weapon bonus (<c>BonusEnabled</c>, <c>BonusStartPercent</c>, <c>BonusDamagePercent</c>, <c>ReloadBonusSeconds</c>).
    /// </summary>
    public bool Bonus { get; private set; } = true;

    /// <summary>
    /// See the property above.
    /// </summary>
    public float BonusStart { get; private set; } = 90;

    /// <summary>
    /// See the property above.
    /// </summary>
    public float BonusDamage { get; private set; } = 7;

    /// <summary>
    /// See the property above.
    /// </summary>
    public float ReloadBonus { get; private set; } = 0.8f;

    /// <summary>
    /// The weapon debuff (<c>DebuffEnabled</c>, <c>DebuffStartPercent</c>, <c>MinDamagePercent</c>, <c>ReloadPenaltySeconds</c>).
    /// </summary>
    public bool Debuff { get; private set; } = true;

    /// <summary>
    /// See the property above.
    /// </summary>
    public float DebuffStart { get; private set; } = 39;

    /// <summary>
    /// See the property above.
    /// </summary>
    public float MinDamage { get; private set; } = 30;

    /// <summary>
    /// See the property above.
    /// </summary>
    public float ReloadPenalty { get; private set; } = 10;

    /// <summary>
    /// <c>BreakWeapons</c>: gear reaching 0 is destroyed (weapons and armour alike).
    /// </summary>
    public bool Break { get; private set; }

    /// <summary>
    /// <c>CraftedFullDurability</c>: gear the player crafted starts at its maximum.
    /// </summary>
    public bool CraftedFull { get; private set; } = true;

    /// <summary>
    /// <c>IgnoreWeapons</c> and <c>IgnoreArmour</c>: lower-case words; gear whose name, material or maker holds one has no durability.
    /// </summary>
    public IReadOnlyList<string> IgnoreWeapons { get; private set; } = ["meitou"];

    /// <summary>
    /// See the property above.
    /// </summary>
    public IReadOnlyList<string> IgnoreArmour { get; private set; } = [];

    /// <summary>
    /// The tooltip row (<c>ShowTooltip</c>, <c>TooltipLabel</c>; the default label is translated).
    /// </summary>
    public bool ShowTooltip { get; private set; } = true;

    /// <summary>
    /// See the property above.
    /// </summary>
    public string TooltipLabel { get; private set; } = "Durability";

    /// <summary>
    /// Repairs (<c>RepairEnabled</c>, <c>RepairOthers</c>, <c>RepairReach</c> held to 4..30, <c>RepairAmount</c>, <c>RepairAmountAtZero</c>,
    /// <c>SmithingBonusPerLevel</c>, <c>RepairItem</c> names split by <c>|</c>, <c>RepairItemID</c>, <c>RepairSeconds</c>, <c>RepairAnimation</c>).
    /// </summary>
    public bool Repair { get; private set; } = true;

    /// <summary>
    /// See the property above.
    /// </summary>
    public bool RepairOthers { get; private set; } = true;

    /// <summary>
    /// See the property above.
    /// </summary>
    public float Reach { get; private set; } = 10;

    /// <summary>
    /// See the property above.
    /// </summary>
    public float SmithPerLevel { get; private set; } = 2;

    /// <summary>
    /// See the property above.
    /// </summary>
    public float RepairSeconds { get; private set; } = 1.2f;

    /// <summary>
    /// See the property above.
    /// </summary>
    public int RepairAmount { get; private set; } = 500;

    /// <summary>
    /// See the property above.
    /// </summary>
    public int RepairAtZero { get; private set; } = 50;

    /// <summary>
    /// See the property above.
    /// </summary>
    public IReadOnlyList<string> RepairItems { get; private set; } = ["Tools"];

    /// <summary>
    /// See the property above.
    /// </summary>
    public string RepairItemId { get; private set; } = "";

    /// <summary>
    /// See the property above.
    /// </summary>
    public string RepairAnimation { get; private set; } = "auto";

    /// <summary>
    /// The menu rows' texts (<c>RepairMenuText</c>, <c>RepairArmourText</c>; the defaults are translated).
    /// </summary>
    public string RepairWeaponText { get; private set; } = "Repair Weapon";

    /// <summary>
    /// See the property above.
    /// </summary>
    public string RepairArmourText { get; private set; } = "Repair Armour";

    /// <summary>
    /// Armour (<c>ArmourEnabled</c>; <c>ArmourDebuffEnabled</c>, <c>ArmourDebuffStartPercent</c>, <c>ArmourMinResistPercent</c>;
    /// <c>ArmourBonusEnabled</c>, <c>ArmourBonusStartPercent</c>, <c>ArmourBonusPercent</c>).
    /// </summary>
    public bool Armour { get; private set; } = true;

    /// <summary>
    /// See the property above.
    /// </summary>
    public bool ArmourDebuff { get; private set; } = true;

    /// <summary>
    /// See the property above.
    /// </summary>
    public bool ArmourBonus { get; private set; } = true;

    /// <summary>
    /// See the property above.
    /// </summary>
    public float ArmourDebuffStart { get; private set; } = 39;

    /// <summary>
    /// See the property above.
    /// </summary>
    public float ArmourMinResist { get; private set; } = 30;

    /// <summary>
    /// See the property above.
    /// </summary>
    public float ArmourBonusStart { get; private set; } = 90;

    /// <summary>
    /// See the property above.
    /// </summary>
    public float ArmourBonusPercent { get; private set; } = 7;

    /// <summary>
    /// Grade wear (<c>GradeWearEnabled</c>, <c>WeaponGradeWear</c> 14 percentages, <c>ArmourGradeWear</c> and <c>RangedGradeWear</c> 6 each).
    /// </summary>
    public bool GradeWear { get; private set; } = true;

    /// <summary>
    /// See the property above.
    /// </summary>
    public int[] WeaponGrades { get; } = [100, 95, 85, 75, 65, 55, 50, 45, 35, 30, 25, 15, 10, 5];

    /// <summary>
    /// See the property above.
    /// </summary>
    public int[] ArmourGrades { get; } = [100, 75, 50, 25, 15, 5];

    /// <summary>
    /// See the property above.
    /// </summary>
    public int[] RangedGrades { get; } = [100, 75, 50, 25, 15, 5];

    /// <summary>
    /// <c>RepairInstant</c>: the repair needs no timer (<c>RepairSeconds</c> 0 or <c>RepairAnimation = none</c>, as the original).
    /// </summary>
    public bool RepairInstant => RepairSeconds <= 0 || RepairAnimation == "none";

    #endregion

    #region Reading

    /// <summary>
    /// The settings of <paramref name="folder"/>'s cfg, written there with the defaults first when it has none (a failed write keeps the defaults).
    /// </summary>
    public static DurabilityConfig Load(string folder, Action<string> log)
    {
        var config = new DurabilityConfig();
        config.Classes[(int)GearClass.Ranged] = new(15, 267, 500);
        config.Classes[(int)GearClass.LightArmour] = new(100, 300, 600);
        config.Classes[(int)GearClass.MediumArmour] = new(100, 400, 800);
        config.Classes[(int)GearClass.HeavyArmour] = new(100, 600, 1200);

        string path = Path.Combine(folder, FileName);
        try
        {
            if (!File.Exists(path)) File.WriteAllText(path, DefaultFile);

            foreach (string line in File.ReadAllLines(path)) config.Read(line);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log($"{FileName} could not be read or written ({e.Message}); using the defaults");
            config.ReadDefaults();
        }

        return config;
    }

    /// <summary>
    /// Applies the default file's lines (the melee class ranges live only there, as in the original).
    /// </summary>
    void ReadDefaults()
    {
        foreach (string line in DefaultFile.Split('\n')) Read(line);
    }

    /// <summary>
    /// Applies one <c>key = value</c> line; text after <c>#</c> is a comment, an unknown key is ignored.
    /// </summary>
    void Read(string raw)
    {
        int hash = raw.IndexOf('#');
        string line = (hash >= 0 ? raw[..hash] : raw).Trim();
        int eq = line.IndexOf('=');
        if (eq < 0) return;

        string key = line[..eq].Trim();
        string value = line[(eq + 1)..].Trim();
        if (!ReadSwitch(key, value) && !ReadNumber(key, value) && !ReadText(key, value)) ReadRange(key, value);
    }

    bool ReadSwitch(string key, string value)
    {
        bool on = Int(value) != 0;
        switch (key)
        {
            case "Enabled": Enabled = on; break;
            case "BonusEnabled": Bonus = on; break;
            case "DebuffEnabled": Debuff = on; break;
            case "BreakWeapons": Break = on; break;
            case "CraftedFullDurability": CraftedFull = on; break;
            case "ShowTooltip": ShowTooltip = on; break;
            case "RepairEnabled": Repair = on; break;
            case "RepairOthers": RepairOthers = on; break;
            case "ArmourEnabled": Armour = on; break;
            case "ArmourDebuffEnabled": ArmourDebuff = on; break;
            case "ArmourBonusEnabled": ArmourBonus = on; break;
            case "GradeWearEnabled": GradeWear = on; break;
            default: return false;
        }

        return true;
    }

    bool ReadNumber(string key, string value)
    {
        switch (key)
        {
            case "DamagePerHit": PerHit = Int(value); break;
            case "DamagePerShot": PerShot = Int(value); break;
            case "DamagePerBlock": PerBlock = Int(value); break;
            case "DamagePerArmourHit": PerArmourHit = Int(value); break;
            case "BonusStartPercent": BonusStart = Float(value); break;
            case "BonusDamagePercent": BonusDamage = Float(value); break;
            case "ReloadBonusSeconds": ReloadBonus = Float(value); break;
            case "ReloadPenaltySeconds": ReloadPenalty = Float(value); break;
            case "DebuffStartPercent": DebuffStart = Float(value); break;
            case "MinDamagePercent": MinDamage = Float(value); break;
            case "RepairReach": Reach = Math.Clamp(Float(value), 4, 30); break;
            case "RepairAmount": RepairAmount = Int(value); break;
            case "RepairAmountAtZero": RepairAtZero = Int(value); break;
            case "SmithingBonusPerLevel": SmithPerLevel = Float(value); break;
            case "RepairSeconds": RepairSeconds = Float(value); break;
            case "ArmourDebuffStartPercent": ArmourDebuffStart = Float(value); break;
            case "ArmourMinResistPercent": ArmourMinResist = Float(value); break;
            case "ArmourBonusStartPercent": ArmourBonusStart = Float(value); break;
            case "ArmourBonusPercent": ArmourBonusPercent = Float(value); break;
            case "WeaponGradeWear": Grades(value, WeaponGrades); break;
            case "ArmourGradeWear": Grades(value, ArmourGrades); break;
            case "RangedGradeWear": Grades(value, RangedGrades); break;
            default: return false;
        }

        return true;
    }

    bool ReadText(string key, string value)
    {
        switch (key)
        {
            case "IgnoreWeapons": IgnoreWeapons = Words(value); break;
            case "IgnoreArmour": IgnoreArmour = Words(value); break;
            case "TooltipLabel": TooltipLabel = value; break;
            case "RepairItem": RepairItems = [.. value.Split('|').Select(n => n.Trim()).Where(n => n.Length > 0)]; break;
            case "RepairItemID": RepairItemId = value; break;
            case "RepairAnimation": RepairAnimation = value; break;
            case "RepairMenuText": RepairWeaponText = value; break;
            case "RepairArmourText": RepairArmourText = value; break;
            default: return false;
        }

        return true;
    }

    /// <summary>
    /// A range line (<c>min-max</c> or <c>min-max/cap</c>; the cap defaults to max); a malformed one is ignored.
    /// </summary>
    void ReadRange(string key, string value)
    {
        int index = Array.IndexOf(RangeKeys, key);
        if (index < 0 || ParseRange(value) is not { } range) return;

        if (index == 0) Default = range;
        else Classes[index - 1] = range;
    }

    static DurabilityRange? ParseRange(string value)
    {
        int slash = value.IndexOf('/');
        string span = slash >= 0 ? value[..slash] : value;
        int dash = span.Length > 1 ? span.IndexOf('-', 1) : -1;
        if (dash < 0) return null;

        int min = Int(span[..dash]);
        int max = Int(span[(dash + 1)..]);
        int cap = slash >= 0 ? Int(value[(slash + 1)..]) : max;
        if (max < min) (min, max) = (max, min);

        return max <= 0 ? null : new DurabilityRange(Math.Max(0, min), max, Math.Max(cap, 1));
    }

    /// <summary>
    /// The range keys: <c>DurabilityRange</c>, then the classes in <see cref="GearClass"/> order.
    /// </summary>
    static readonly string[] RangeKeys =
    [
        "DurabilityRange", "KatanaRange", "SabreRange", "HackerRange", "HeavyRange", "BluntRange", "PolearmRange", "RangedRange",
        "LightArmourRange", "MediumArmourRange", "HeavyArmourRange",
    ];

    /// <summary>
    /// A comma list of percentages into <paramref name="into"/>, each held to 0..100; a short list leaves the rest.
    /// </summary>
    static void Grades(string value, int[] into)
    {
        string[] parts = value.Split(',');
        for (int i = 0; i < into.Length && i < parts.Length; i++) into[i] = Math.Clamp(Int(parts[i]), 0, 100);
    }

    static string[] Words(string value) =>
        [.. value.Split(',').Select(w => w.Trim().ToLowerInvariant()).Where(w => w.Length > 0)];

    /// <summary>
    /// The leading integer of <paramref name="text"/> (0 when none), as C's <c>atoi</c>.
    /// </summary>
    static int Int(string text)
    {
        string t = text.Trim();
        int end = t.Length > 0 && t[0] is '-' or '+' ? 1 : 0;
        while (end < t.Length && char.IsAsciiDigit(t[end])) end++;

        return int.TryParse(t.AsSpan(0, end), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int v) ? v : 0;
    }

    static float Float(string text) =>
        float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) && float.IsFinite(v) ? v : 0;

    #endregion

    /// <summary>
    /// The file written when the mod folder has none: every key at its default (the original's defaults).
    /// </summary>
    const string DefaultFile = """
        # Equipment Durability settings (Meitou port). Read at start; delete this file to get the defaults back.
        # Lines starting with # are comments. Keys and defaults are the original Durability mod's.

        Enabled = 1

        # Rolled range of a new item: min-max, or min-max/cap when the maximum (tooltip, repair limit) is above the roll.
        DurabilityRange = 100-1000
        KatanaRange = 15-267/500
        SabreRange = 35-399/700
        HackerRange = 45-445/800
        HeavyRange = 98-533/1000
        BluntRange = 25-350/600
        PolearmRange = 35-399/700
        RangedRange = 15-267/500
        LightArmourRange = 100-300/600
        MediumArmourRange = 100-400/800
        HeavyArmourRange = 100-600/1200

        # Points lost by player characters' gear: per melee hit, crossbow shot, block, and per armour piece covering a part that is hit.
        DamagePerHit = 1
        DamagePerShot = 1
        DamagePerBlock = 1
        DamagePerArmourHit = 1

        # Better grades wear slower: the percent chance that a use costs a point, per grade.
        GradeWearEnabled = 1
        WeaponGradeWear = 100,95,85,75,65,55,50,45,35,30,25,15,10,5
        ArmourGradeWear = 100,75,50,25,15,5
        RangedGradeWear = 100,75,50,25,15,5

        # Good condition: above BonusStartPercent weapons hit harder.
        BonusEnabled = 1
        BonusStartPercent = 90
        BonusDamagePercent = 7
        ReloadBonusSeconds = 0.8

        # Worn weapons: below DebuffStartPercent damage falls in a straight line to MinDamagePercent at 0.
        DebuffEnabled = 1
        DebuffStartPercent = 39
        MinDamagePercent = 30
        ReloadPenaltySeconds = 10

        # 1 = gear at 0 is destroyed.
        BreakWeapons = 0
        CraftedFullDurability = 1
        IgnoreWeapons = Meitou

        # Armour: worn pieces protect less (cut and blunt) below the start, more above the bonus start.
        ArmourEnabled = 1
        ArmourDebuffEnabled = 1
        ArmourDebuffStartPercent = 39
        ArmourMinResistPercent = 30
        ArmourBonusEnabled = 1
        ArmourBonusStartPercent = 90
        ArmourBonusPercent = 7
        IgnoreArmour =

        ShowTooltip = 1
        TooltipLabel = Durability

        # Repairs from the right-click menu with a Tools item.
        RepairEnabled = 1
        RepairOthers = 1
        RepairReach = 10
        RepairAmount = 500
        RepairAmountAtZero = 50
        SmithingBonusPerLevel = 2
        RepairItem = Tools
        RepairItemID =
        RepairAnimation = auto
        RepairSeconds = 1.2
        RepairMenuText = Repair Weapon
        RepairArmourText = Repair Armour
        """;
}
