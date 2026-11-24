using System.Globalization;

namespace Meitou.AdvancedMedicAi;

/// <summary>
/// The original's <c>AdvancedMedicAI.ini</c>-only values (no in-game control), read once at start from the installed original's ini; a key the
/// ini lacks, or a value that does not parse, keeps the DLL's default (README "Facts"). Keys are matched ignoring case, as the original lowercases
/// them; <c>#</c> and <c>;</c> start comments.
/// </summary>
public sealed record MedicTuning
{
    public bool AutoPulse { get; init; } = true;
    public float PulseSeconds { get; init; } = 15;
    public float OrderCooldownSeconds { get; init; } = 15;
    public float UnitsPerMeter { get; init; } = 10;
    public float RecentTargetSeconds { get; init; } = 45;
    public float PerTargetSeconds { get; init; } = 45;
    public float ActorCooldownSeconds { get; init; } = 5;
    public bool RequireMedicJob { get; init; } = true;
    public float MinOrganicNeed { get; init; } = 0.1f;
    public float MinRobotNeed { get; init; }
    public bool SkipForeignBuildings { get; init; } = true;
    public bool AllowPlayerBuildings { get; init; } = true;
    public bool SkipUnknownBuildings { get; init; }
    public bool MultiMedic { get; init; } = true;
    public int MaxOrdersPerPulse { get; init; }
    public bool KitFallback { get; init; }
    public bool DoorBlocking { get; init; } = true;
    public float DoorBlockSeconds { get; init; } = 300;
    public float DoorWindowSeconds { get; init; } = 20;
    public bool StuckBlocking { get; init; } = true;
    public float StuckBlockSeconds { get; init; } = 300;
    public float StuckCheckSeconds { get; init; } = 5;
    public float MinProgressSq { get; init; } = 1000;
    public float MinProgressRatio { get; init; } = 0.05f;
    public float MinStuckDistanceSq { get; init; } = 10000;
    public bool Debug { get; init; }

    /// <summary>
    /// The cooldown of a treated patient: the larger of <c>recentTargetCooldownSeconds</c> and <c>perTargetCooldownSeconds</c>.
    /// </summary>
    public float TargetCooldownSeconds => MathF.Max(RecentTargetSeconds, PerTargetSeconds);

    /// <summary>
    /// The orders one pulse may give: all its medics in multi-medic mode with no limit, the limit when set, else one.
    /// </summary>
    public int OrdersPerPulse(int medics) => !MultiMedic ? 1 : MaxOrdersPerPulse > 0 ? MaxOrdersPerPulse : medics;

    public static MedicTuning Parse(IEnumerable<string> lines)
    {
        var v = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in lines)
        {
            string line = raw.Split('#', ';')[0];
            int eq = line.IndexOf('=');
            if (eq > 0) v[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }

        var d = new MedicTuning();
        return new MedicTuning
        {
            AutoPulse = Bool(v, "autoPulseEnabled", d.AutoPulse),
            PulseSeconds = AtLeastOne(Float(v, "autoPulseIntervalSeconds", d.PulseSeconds)),
            OrderCooldownSeconds = AtLeastOne(Float(v, "orderCooldownSeconds", d.OrderCooldownSeconds)),
            UnitsPerMeter = Float(v, "unitsPerMeter", d.UnitsPerMeter) is > 0 and var units ? units : d.UnitsPerMeter,
            RecentTargetSeconds = Float(v, "recentTargetCooldownSeconds", d.RecentTargetSeconds),
            PerTargetSeconds = Float(v, "perTargetCooldownSeconds", d.PerTargetSeconds),
            ActorCooldownSeconds = Float(v, "perActorCooldownSeconds", d.ActorCooldownSeconds),
            RequireMedicJob = Bool(v, "requireMedicJob", d.RequireMedicJob),
            MinOrganicNeed = Float(v, "minFleshyFirstAidNeedScore", d.MinOrganicNeed),
            MinRobotNeed = Float(v, "minRobotRepairNeedScore", d.MinRobotNeed),
            SkipForeignBuildings = Bool(v, "skipTargetsInsideForeignBuildings", d.SkipForeignBuildings),
            AllowPlayerBuildings = Bool(v, "allowTargetsInsidePlayerBuildings", d.AllowPlayerBuildings),
            SkipUnknownBuildings = Bool(v, "skipTargetsInsideUnknownBuildings", d.SkipUnknownBuildings),
            MultiMedic = Bool(v, "enableMultiMedicMode", d.MultiMedic),
            MaxOrdersPerPulse = (int)Math.Max(0, Float(v, "maxOrdersPerPulse", d.MaxOrdersPerPulse)),
            KitFallback = Bool(v, "allowMedicJobKitFallback", d.KitFallback),
            DoorBlocking = Bool(v, "enableDoorBlockedTargetCooldown", d.DoorBlocking),
            DoorBlockSeconds = Float(v, "doorBlockedTargetCooldownSeconds", d.DoorBlockSeconds),
            DoorWindowSeconds = Float(v, "doorInteractionDetectionWindowSeconds", d.DoorWindowSeconds),
            StuckBlocking = Bool(v, "enableStuckTargetCooldown", d.StuckBlocking),
            StuckBlockSeconds = Float(v, "stuckTargetCooldownSeconds", d.StuckBlockSeconds),
            StuckCheckSeconds = Float(v, "afterOrderCheckSeconds", d.StuckCheckSeconds),
            MinProgressSq = Float(v, "minApproachProgressSq", d.MinProgressSq),
            MinProgressRatio = Float(v, "minApproachProgressRatio", d.MinProgressRatio),
            MinStuckDistanceSq = Float(v, "minDistanceSqForStuckCheck", d.MinStuckDistanceSq),
            Debug = Bool(v, "debugLogging", d.Debug),
        };
    }

    /// <summary>
    /// The DLL's rule for the two intervals: below 1 s means 15 s.
    /// </summary>
    static float AtLeastOne(float seconds) => seconds < 1 ? 15 : seconds;

    static float Float(Dictionary<string, string> v, string key, float fallback) =>
        v.TryGetValue(key, out var s) && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) && float.IsFinite(f)
            ? f
            : fallback;

    static bool Bool(Dictionary<string, string> v, string key, bool fallback) =>
        v.TryGetValue(key, out var s) ? s.ToLowerInvariant() switch
        {
            "true" or "1" or "yes" or "on" => true,
            "false" or "0" or "no" or "off" => false,
            _ => fallback,
        } : fallback;
}
