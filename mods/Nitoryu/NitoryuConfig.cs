using System.Globalization;
using Meitou.Data;
using Meitou.Data.Fcs;

namespace Meitou.Nitoryu;

/// <summary>
/// The original's settings: its about 135 <c>nitoryu ...</c> fields on GLOBAL CONSTANTS (the data, which a later mod may change), each
/// overridden by the matching uncommented key of the original's <c>Nitoryu.ini</c> (README "Settings": "remove the ; and your value wins").
/// Only the keys the port uses are read.
/// </summary>
sealed class NitoryuConfig
{
    /// <summary>The ini keys (<c>section.key</c>, lower case) the port reads, each with the constants field it overrides.</summary>
    static readonly Dictionary<string, string> IniToField = new(StringComparer.OrdinalIgnoreCase)
    {
        ["main.only_my_strikes"] = "only my strikes",
        ["npc.dual"] = "npc dual",
        ["npc.max_tier"] = "npc max tier",
        ["weapons.categories"] = "weapon categories",
        ["speed.dual_speed_bonus"] = "speed bonus",
        ["speed.dual_attack_cap"] = "attack speed cap",
        ["speed.dual_block_cap"] = "block speed cap",
        ["guard.guard"] = "guard",
        ["guard.cooldown_ms"] = "guard cooldown ms",
        ["guard.arc_deg"] = "guard arc deg",
        ["guard.back_min_tier"] = "back guard min tier",
        ["guard.back_share"] = "back guard share",
        ["guard.counter"] = "counter",
        ["guard.counter_window_ms"] = "counter window ms",
        ["flow.flow"] = "flow",
        ["flow.attacks"] = "flow attacks",
        ["flow.window_ms"] = "flow window ms",
        ["flow.duration_ms"] = "flow duration ms",
        ["flow.cooldown_ms"] = "flow cooldown ms",
        ["flow.dex_bonus"] = "flow dexterity bonus",
        ["flow.speed"] = "flow speed",
        ["flow.cap_bonus"] = "flow attack cap bonus",
        ["mugen.mugen"] = "mugen",
        ["mugen.cooldown_ms"] = "curve mugen cooldown ms",
        ["moves.double_hit_cooldown_ms"] = "double hit cooldown ms",
        ["moves.no_repeat"] = "no repeat",
        ["moves.main_move_share"] = "main move share",
        ["moves.cross_riposte_chance"] = "cross riposte chance",
        ["moves.cross_riposte_window_ms"] = "cross riposte window ms",
        ["pair.pair"] = "pair bonuses",
        ["pair.short_ratio"] = "pair short ratio",
        ["pair.equal_ratio"] = "pair equal ratio",
        ["pair.long_short_parry"] = "pair long short parry",
        ["pair.equal_twin_share"] = "pair equal twin share",
        ["tuning.xp_rate"] = "ambidexterity xp share",
        ["tuning.armour_heavy_cut"] = "armour heavy cut",
        ["tuning.armour_medium_cut"] = "armour medium cut",
        ["tuning.armour_floor"] = "armour floor",
        ["tuning.ambidexterity_seed"] = "ambidexterity seed",
        ["tuning.clumsiness"] = "clumsiness base",
        ["tuning.base_strike_weight_high"] = "base strike weight high",
        ["balance.off_hand_share"] = "curve off hand share",
        ["balance.twin_share"] = "curve twin share",
        ["balance.off_hand_damage"] = "curve off hand damage",
        ["balance.off_hand_guard"] = "curve off hand guard",
        ["balance.counter_chance"] = "curve counter chance",
        ["balance.divided_guard"] = "divided guard",
        ["balance.dex_bonus"] = "dexterity bonus",
        ["balance.pressure_points"] = "curve pressure points",
        ["pressure.pressure"] = "pressure",
        ["pressure.max_stacks"] = "pressure max stacks",
        ["pressure.decay_ms"] = "pressure decay ms",
        ["scissor.scissor"] = "scissor",
        ["scissor.chance"] = "curve scissor chance",
        ["split.split"] = "split",
        ["split.chance"] = "curve split chance",
        ["split.arc_deg"] = "split arc deg",
        ["split.reach"] = "split reach",
        ["split.attacker_ms"] = "split attacker ms",
        ["mugen.chain"] = "curve mugen chain",
        ["leftHand.enabled"] = "left hand",
        ["leftHand.min_ambidexterity"] = "left hand min ambidexterity",
        ["leftHand.damage_at_min"] = "left hand damage at min",
        ["leftHand.speed_at_min"] = "left hand speed at min",
        ["leftHand.block_penalty_at_min"] = "left hand block penalty at min",
        ["training.pair_dummy"] = "pair dummy training",
        ["animations.run_stances"] = "run stances",
        ["marks.marks"] = "marks",
        ["compat.addons"] = "addons",
    };

    readonly GameRecord? _constants;
    readonly Dictionary<string, string> _ini;

    NitoryuConfig(GameRecord? constants, Dictionary<string, string> ini)
    {
        _constants = constants;
        _ini = ini;
    }

    /// <summary>The settings from <paramref name="db"/>'s GLOBAL CONSTANTS and the ini at <paramref name="iniPath"/> (none: the data alone).</summary>
    public static NitoryuConfig Load(GameDatabase db, string? iniPath, Action<string> log)
    {
        var constants = db.OfType(FcsRecordType.Constants).FirstOrDefault(r => r.Name == "GLOBAL CONSTANTS")
            ?? db.OfType(FcsRecordType.Constants).FirstOrDefault();
        var ini = ReadIni(iniPath, log);
        var config = new NitoryuConfig(constants, ini);
        config.Fill();
        return config;
    }

    /// <summary>The ini's <c>section.key</c> values (lower case), comment lines left out.</summary>
    static Dictionary<string, string> ReadIni(string? path, Action<string> log)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (path is null || !File.Exists(path)) return values;

        try
        {
            string section = "";
            foreach (string raw in File.ReadLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] is ';' or '#') continue;

                if (line[0] == '[' && line.EndsWith(']'))
                {
                    section = line[1..^1].Trim();
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq > 0) values[$"{section}.{line[..eq].Trim()}"] = line[(eq + 1)..].Trim();
            }
        }
        catch (IOException e)
        {
            log($"Nitoryu.ini not read: {e.Message}");
        }

        return values;
    }

    /// <summary>The ini key's value that overrides <paramref name="field"/>, else null.</summary>
    string? Ini(string field)
    {
        foreach (var (key, f) in IniToField)
        {
            if (f == field && _ini.TryGetValue(key, out var value)) return value;
        }

        return null;
    }

    /// <summary>An ini value read straight (keys with no constants field: the NPC lists, weapon lists, the hotkey).</summary>
    public string IniRaw(string sectionKey) => _ini.GetValueOrDefault(sectionKey, "");

    float F(string field, float fallback)
    {
        if (Ini(field) is { } text && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) return v;
        if (_constants is null) return fallback;

        string key = "nitoryu " + field;
        float f = _constants.GetFloat(key, float.NaN);
        if (!float.IsNaN(f)) return f;

        int i = _constants.GetInt(key, int.MinValue);
        return i != int.MinValue ? i : fallback;
    }

    int I(string field, int fallback) => (int)MathF.Round(F(field, fallback));

    bool B(string field, bool fallback)
    {
        if (Ini(field) is { } text) return text.Trim() is "1" or "true" or "on";

        return _constants?.GetBool("nitoryu " + field, fallback) ?? fallback;
    }

    string S(string field, string fallback) => Ini(field) ?? _constants?.GetString("nitoryu " + field, fallback) ?? fallback;

    static float[] Floats(string text) => [.. text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(s => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0)];

    void Fill()
    {
        FillMoves();
        FillPath();
        FillSpeed();
        FillGuard();
        FillFlow();
        FillPair();
        FillNpc();
        FillExtras();
    }

    void FillExtras()
    {
        Pressure = B("pressure", true);
        PressureMinTier = I("pressure min tier", 3);
        PressureMaxStacks = I("pressure max stacks", 5);
        PressureDecayMs = I("pressure decay ms", 3000);
        PressurePoints = Curve.Parse(S("curve pressure points", ""), "60:1,100:1");
        Scissor = B("scissor", true);
        ScissorChance = Curve.Parse(S("curve scissor chance", ""), "59.99:0,60:0.8,75:1.0");
        ScissorWindowMs = I("scissor window ms", 1000);
        Split = B("split", true);
        SplitMinTier = I("split min tier", 4);
        SplitChance = Curve.Parse(S("curve split chance", ""), "75:1.0,100:1.0");
        SplitArcDegrees = F("split arc deg", 180);
        SplitReach = F("split reach", 2.5f);
        SplitAttackerMs = I("split attacker ms", 8000);
        MugenChain = Curve.Parse(S("curve mugen chain", ""), "90:0.25,100:0.5");
        MugenChainWindowMs = I("mugen chain window ms", 2500);
        LeftHand = B("left hand", true);
        LeftHandMinAmbidexterity = F("left hand min ambidexterity", 50);
        LeftHandDamageAtMin = F("left hand damage at min", 0.7f);
        LeftHandSpeedAtMin = F("left hand speed at min", 0.85f);
        LeftHandBlockPenaltyAtMin = F("left hand block penalty at min", 10);
        PairDummy = B("pair dummy training", true);
        Marks = I("marks", 1);
        AddonsOn = B("addons", true);
        RunStances = [.. Floats(S("run stances", "3")).Select(v => (int)v).Where(v => v is >= 1 and <= 4)];
    }

    public bool Pressure { get; private set; }
    public int PressureMinTier { get; private set; }
    public int PressureMaxStacks { get; private set; }
    public int PressureDecayMs { get; private set; }
    public Curve PressurePoints { get; private set; } = null!;
    public bool Scissor { get; private set; }
    public Curve ScissorChance { get; private set; } = null!;
    public int ScissorWindowMs { get; private set; }
    public bool Split { get; private set; }
    public int SplitMinTier { get; private set; }
    public Curve SplitChance { get; private set; } = null!;
    public float SplitArcDegrees { get; private set; }
    public float SplitReach { get; private set; }
    public int SplitAttackerMs { get; private set; }
    public Curve MugenChain { get; private set; } = null!;
    public int MugenChainWindowMs { get; private set; }
    public bool LeftHand { get; private set; }
    public float LeftHandMinAmbidexterity { get; private set; }
    public float LeftHandDamageAtMin { get; private set; }
    public float LeftHandSpeedAtMin { get; private set; }
    public float LeftHandBlockPenaltyAtMin { get; private set; }
    public bool PairDummy { get; private set; }
    public int Marks { get; private set; }
    public bool AddonsOn { get; private set; }
    public int[] RunStances { get; private set; } = [];

    void FillMoves()
    {
        OnlyMyStrikes = B("only my strikes", true);
        DoubleHitCooldownMs = I("double hit cooldown ms", 5000);
        NoRepeat = B("no repeat", true);
        MainMoveShare = F("main move share", 0.6f);
        CrossRiposteChance = F("cross riposte chance", 0.25f);
        CrossRiposteWindowMs = I("cross riposte window ms", 1500);
        BaseStrikeWeightHigh = F("base strike weight high", 0.5f);
        SpecialMinAmbidexterity = F("special min ambidexterity", 50);
        Mugen = B("mugen", true);
        MugenMinTier = I("mugen min tier", 5);
        MugenCooldown = Curve.Parse(S("curve mugen cooldown ms", ""), "90:3500,100:2000");
        OffHandShare = Curve.Parse(S("curve off hand share", ""), "20:0.20,40:0.23,60:0.26,75:0.30,85:0.40,90:0.50,100:0.55");
        TwinShare = Curve.Parse(S("curve twin share", ""), "59.99:0,60:0.25,100:0.45");
        OffHandDamage = Curve.Parse(S("curve off hand damage", ""), "20:1.04,40:1.14,70:1.30");
        DexterityBonus = F("dexterity bonus", 0.12f);
    }

    void FillPath()
    {
        WeightAmbidexterity = F("dp weight ambidexterity", 0.6f);
        WeightSkill = F("dp weight skill", 0.1f);
        WeightDexterity = F("dp weight dexterity", 0.1f);
        WeightAttack = F("dp weight attack", 0.1f);
        WeightDefence = F("dp weight defence", 0.1f);
        TierPath = Floats(S("tier dp", "20,40,60,75,90"));
        TierAmbidexterity = Floats(S("tier min ambidexterity", "0,0,40,60,80"));
        AmbidexteritySeed = F("ambidexterity seed", 0.25f);
        XpShare = F("ambidexterity xp share", 1);
        ArmourHeavyCut = F("armour heavy cut", 0.07f);
        ArmourMediumCut = F("armour medium cut", 0.04f);
        ArmourFloor = F("armour floor", 0.3f);
        OverloadFloor = F("overload floor", 0.2f);
        ClumsinessBase = F("clumsiness base", 10);
        ClumsinessSlope = F("clumsiness slope", 0.25f);
        DividedGuard = Floats(S("divided guard", "4,4,2,0,0,0"));
    }

    void FillSpeed()
    {
        SpeedAtZero = F("speed at dp 0", 0.85f);
        SpeedFullPath = F("speed full dp", 30);
        SpeedBonus = F("speed bonus", 0.3f);
        SpeedBonusFrom = F("speed bonus from", 30);
        AttackSpeedCap = F("attack speed cap", 1.5f);
        BlockSpeedCap = F("block speed cap", 4);
    }

    void FillGuard()
    {
        Guard = B("guard", true);
        GuardMinTier = I("guard min tier", 2);
        GuardCooldownMs = I("guard cooldown ms", 1000);
        GuardArcDegrees = F("guard arc deg", 110);
        GuardShareCombo = F("guard share combo", 0.5f);
        GuardShareMain = F("guard share main", 0.5f);
        GuardMainMinTier = I("guard main min tier", 3);
        BackGuardMinTier = I("back guard min tier", 3);
        BackGuardShare = F("back guard share", 0.5f);
        OffHandGuard = Curve.Parse(S("curve off hand guard", ""), "39.99:0,40:0.20,60:0.30,75:0.40,90:0.50,100:0.55");
        Counter = B("counter", true);
        CounterMinTier = I("counter min tier", 3);
        CounterWindowMs = I("counter window ms", 1500);
        CounterChance = Curve.Parse(S("curve counter chance", ""), "59.99:0,60:0.30,100:0.60");
    }

    void FillFlow()
    {
        Flow = B("flow", true);
        FlowMinTier = I("flow min tier", 4);
        FlowAttacks = I("flow attacks", 3);
        FlowWindowMs = I("flow window ms", 5000);
        FlowDurationMs = I("flow duration ms", 4000);
        FlowCooldownMs = I("flow cooldown ms", 5000);
        FlowDexterityBonus = F("flow dexterity bonus", 0.12f);
        FlowSpeed = F("flow speed", 0.04f);
        FlowCapBonus = F("flow attack cap bonus", 0.3f);
    }

    void FillPair()
    {
        PairBonuses = B("pair bonuses", true);
        PairShortRatio = F("pair short ratio", 0.8f);
        PairEqualRatio = F("pair equal ratio", 0.9f);
        PairLongShortParry = F("pair long short parry", 1.3f);
        PairEqualTwinShare = F("pair equal twin share", 1.3f);
        OffWeaponBonusShare = F("off weapon bonus share", 0.5f);
        Categories = [.. Floats(S("weapon categories", "0,1")).Select(v => (int)v)];
    }

    void FillNpc()
    {
        NpcDual = S("npc dual", "all").Trim().ToLowerInvariant();
        NpcMaxTier = I("npc max tier", 3);
        NpcMinDexterity = F("npc min dexterity", 30);
        NpcMinSkill = F("npc min skill", 30);
        NpcStrengthShare = F("npc strength share", 0.5f);
    }

    public bool OnlyMyStrikes { get; private set; }
    public int DoubleHitCooldownMs { get; private set; }
    public bool NoRepeat { get; private set; }
    public float MainMoveShare { get; private set; }
    public float CrossRiposteChance { get; private set; }
    public int CrossRiposteWindowMs { get; private set; }
    public float BaseStrikeWeightHigh { get; private set; }
    public float SpecialMinAmbidexterity { get; private set; }
    public bool Mugen { get; private set; }
    public int MugenMinTier { get; private set; }
    public Curve MugenCooldown { get; private set; } = null!;
    public Curve OffHandShare { get; private set; } = null!;
    public Curve TwinShare { get; private set; } = null!;
    public Curve OffHandDamage { get; private set; } = null!;
    public float DexterityBonus { get; private set; }

    public float WeightAmbidexterity { get; private set; }
    public float WeightSkill { get; private set; }
    public float WeightDexterity { get; private set; }
    public float WeightAttack { get; private set; }
    public float WeightDefence { get; private set; }
    public float[] TierPath { get; private set; } = [];
    public float[] TierAmbidexterity { get; private set; } = [];
    public float AmbidexteritySeed { get; private set; }
    public float XpShare { get; private set; }
    public float ArmourHeavyCut { get; private set; }
    public float ArmourMediumCut { get; private set; }
    public float ArmourFloor { get; private set; }
    public float OverloadFloor { get; private set; }
    public float ClumsinessBase { get; private set; }
    public float ClumsinessSlope { get; private set; }
    public float[] DividedGuard { get; private set; } = [];

    public float SpeedAtZero { get; private set; }
    public float SpeedFullPath { get; private set; }
    public float SpeedBonus { get; private set; }
    public float SpeedBonusFrom { get; private set; }
    public float AttackSpeedCap { get; private set; }
    public float BlockSpeedCap { get; private set; }

    public bool Guard { get; private set; }
    public int GuardMinTier { get; private set; }
    public int GuardCooldownMs { get; private set; }
    public float GuardArcDegrees { get; private set; }
    public float GuardShareCombo { get; private set; }
    public float GuardShareMain { get; private set; }
    public int GuardMainMinTier { get; private set; }
    public int BackGuardMinTier { get; private set; }
    public float BackGuardShare { get; private set; }
    public Curve OffHandGuard { get; private set; } = null!;
    public bool Counter { get; private set; }
    public int CounterMinTier { get; private set; }
    public int CounterWindowMs { get; private set; }
    public Curve CounterChance { get; private set; } = null!;

    public bool Flow { get; private set; }
    public int FlowMinTier { get; private set; }
    public int FlowAttacks { get; private set; }
    public int FlowWindowMs { get; private set; }
    public int FlowDurationMs { get; private set; }
    public int FlowCooldownMs { get; private set; }
    public float FlowDexterityBonus { get; private set; }
    public float FlowSpeed { get; private set; }
    public float FlowCapBonus { get; private set; }

    public bool PairBonuses { get; private set; }
    public float PairShortRatio { get; private set; }
    public float PairEqualRatio { get; private set; }
    public float PairLongShortParry { get; private set; }
    public float PairEqualTwinShare { get; private set; }
    public float OffWeaponBonusShare { get; private set; }
    public int[] Categories { get; private set; } = [];

    public string NpcDual { get; private set; } = "all";
    public int NpcMaxTier { get; private set; }
    public float NpcMinDexterity { get; private set; }
    public float NpcMinSkill { get; private set; }
    public float NpcStrengthShare { get; private set; }
}
