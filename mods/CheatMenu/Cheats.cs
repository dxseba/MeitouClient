using System.Numerics;
using Meitou.Simulation;

namespace Meitou.CheatMenu;

/// <summary>The original's on/off cheats (its tick buttons), in its order.</summary>
public enum Toggle
{
    GodMode,
    NoHunger,
    NoEncumbrance,
    WeatherImmunity,
    InstantResearch,
    Beasts,
    Weaklings,
    InvincibleNpcs,
    FastNpcs,
    StrongNpcs,
    Chaos,
    Raids,
    PerfectStealth,
    Weightless,
    InfiniteConsumables,
    FreeResearch,
    FreeConstruction,
    Generators,
}

/// <summary>
/// The original's cycling buttons: a caption and a list of values, the first "off" (squad and base multipliers) or "normal" (game rules), and
/// Max squad size (off, 50, 100, 200). <see cref="Tiers.Values"/> holds the original's lists (<b>Verified</b>, its <c>.data</c> tables).
/// </summary>
public enum Tier
{
    MoveSpeed,
    Damage,
    SkillXp,
    Construction,
    Production,
    Research,
    Bleeding,
    BloodRecovery,
    WoundHealing,
    KnockoutTime,
    Starving,
    ItemPrices,
    CarryWeight,
    PrisonTime,
    MaxSquadSize,
}

/// <summary>The tiers' captions and values, as the original's tables.</summary>
static class Tiers
{
    public static readonly int Count = Enum.GetValues<Tier>().Length;

    static readonly float[] Speeds = [1, 2, 5, 10, 50, 100];

    public static float[] Values(Tier tier) => tier switch
    {
        Tier.MoveSpeed or Tier.BloodRecovery or Tier.WoundHealing or Tier.Starving or Tier.CarryWeight => [1, 2, 5, 10],
        Tier.Bleeding => [1, 0.5f, 0.25f, 0.1f, 0],
        Tier.KnockoutTime or Tier.PrisonTime => [1, 0.5f, 0.25f, 0.1f],
        Tier.ItemPrices => [1, 0.5f, 0.25f, 0.1f, 2, 5, 10],
        Tier.MaxSquadSize => [0, 50, 100, 200],
        _ => Speeds,
    };

    public static string Label(Tier tier) => tier switch
    {
        Tier.MoveSpeed => "Move speed (squad)",
        Tier.Damage => "Damage (squad)",
        Tier.SkillXp => "Skill XP (squad)",
        Tier.Construction => "Construction speed",
        Tier.Production => "Production speed",
        Tier.Research => "Research speed",
        Tier.Bleeding => "Bleeding",
        Tier.BloodRecovery => "Blood recovery",
        Tier.WoundHealing => "Wound healing",
        Tier.KnockoutTime => "Knockout time",
        Tier.Starving => "Time until starving",
        Tier.ItemPrices => "Item prices",
        Tier.CarryWeight => "Carry weight",
        Tier.PrisonTime => "Prison time",
        _ => "Max squad size",
    };

    /// <summary>The game rules show "normal" at their first value; the multipliers and Max squad size "off".</summary>
    public static bool IsRule(Tier tier) => tier is >= Tier.Bleeding and <= Tier.PrisonTime;
}

/// <summary>The original's one-shot buttons the cheat system applies itself.</summary>
public enum Deed
{
    SetMoney,
    FillHunger,
    FreeSlaves,
    ToggleHungerOf,
    RemoveLimbs,
    Despawn,
    RevealTowns,
    RandomSkills,
    AllAtWar,
    EveryoneHates,
    RenameCharacter,
    RenameSquad,
    AnimalNextAge,
    Clone,
    Recruit,
    RecruitNewSquad,
    FinishBuildings,
    SpawnSquad,
    StepRelation,
}

/// <summary>Turns <paramref name="Which"/> on or off.</summary>
public sealed record SetToggle(Toggle Which, bool On) : SimCommand, IPausedCommand;

/// <summary>Moves <paramref name="Which"/> to its next value (the last wraps to the first).</summary>
public sealed record CycleTier(Tier Which) : SimCommand, IPausedCommand;

/// <summary>
/// A one-shot cheat on <paramref name="Targets"/> (the selection, the squad or the characters near the camera, as the button names), with a number
/// or a text where the button has a field.
/// </summary>
public sealed record DoDeed(Deed Which, IReadOnlyList<CharacterId> Targets, double Number = 0, string Text = "") : SimCommand, IPausedCommand;

/// <summary>Raids: where the camera looks, so the system can spawn there (sent with the toggle and each second while on).</summary>
public sealed record CameraAt(Vector3 Point) : SimCommand, IPausedCommand;
