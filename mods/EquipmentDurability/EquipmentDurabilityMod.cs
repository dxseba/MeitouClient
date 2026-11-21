using System.Globalization;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.EquipmentDurability;

/// <summary>
/// Port of Equipment Durability (workshop 3834344700), rebuilt from its description: weapons and armour wear with use and lose damage below 39%,
/// down to -70% when broken; a "Repair gear" row on your own character mends the gear with Tools by its smithing skills.
/// </summary>
public sealed class EquipmentDurabilityMod : IMod
{
    internal static readonly DurabilitySettings Settings = new();

    /// <summary>The running world's system, for the menu; null before a world starts.</summary>
    internal static volatile DurabilitySystem? Running;

    public void Initialize(ModContext context)
    {
        context.AddSettings(Settings);
        context.AddSystem("durability", _ => Running = new DurabilitySystem(Settings, context.Population), SystemAnchor.End);
        context.AddUi(new RepairMenu());
        context.Log("Equipment Durability initialized");
    }
}

public sealed class DurabilitySettings
{
    [Setting("Enable Equipment Durability", Hint = "Weapons and armour wear with use.")]
    public bool Enabled { get; set; } = true;

    [Setting("Weapon wear per hit", Hint = "Condition a weapon loses per landed melee hit (1 = new).", Min = 0, Max = 0.05, Step = 0.0005)]
    public float WeaponWear { get; set; } = 0.002f;

    [Setting("Armour wear per hit", Hint = "Condition an armour piece covering the part hit loses, more for more damage stopped.", Min = 0, Max = 0.05,
        Step = 0.0005)]
    public float ArmourWear { get; set; } = 0.002f;

    [Setting("Worn below", Hint = "Gear below this condition starts losing damage.", Min = 0.05, Max = 1, Step = 0.01)]
    public float Threshold { get; set; } = 0.39f;

    [Setting("Broken penalty", Hint = "The damage a broken weapon loses (0.7 = -70%).", Min = 0, Max = 1, Step = 0.05)]
    public float BrokenPenalty { get; set; } = 0.7f;

    [Setting("Delete broken gear", Hint = "On: gear that reaches 0 is destroyed instead of staying broken.")]
    public bool DeleteBroken { get; set; }

    [Setting("Repair base", Hint = "Condition a repair adds at smithing 0.", Min = 0, Max = 1, Step = 0.01)]
    public float RepairBase { get; set; } = 0.15f;

    [Setting("Repair per skill", Hint = "Extra condition a repair adds at smithing 100.", Min = 0, Max = 1, Step = 0.01)]
    public float RepairSkill { get; set; } = 0.35f;

    /// <summary>The ITEM name a repair needs in the inventory.</summary>
    public string ToolName { get; set; } = "Tools";
}

/// <summary>The "Repair gear" row on one of your own characters, its caption showing the weapon's and the most worn armour's condition.</summary>
sealed class RepairMenu : IUiExtension
{
    public string Id => "dxseba.equipmentdurability.menu";

    public void Register(UiRegistration registration) => registration.AddCharacterMenuVerbs(Verbs);

    static IEnumerable<UiMenuVerb> Verbs(UiMenuContext menu)
    {
        var target = menu.Target;
        if (!EquipmentDurabilityMod.Settings.Enabled || !target.IsPlayer || target.Body is not { Dead: false, Unconscious: false }) return [];
        if (EquipmentDurabilityMod.Running?.Gear.TryGetValue(target.Id, out var gear) != true) return [];

        return [new UiMenuVerb($"Repair gear ({Describe(gear)})", () => menu.Enqueue(new RepairOrder(target.Id) { Tick = menu.Tick }))];
    }

    static string Describe(GearView gear)
    {
        string weapon = gear.Weapon is { } w ? "weapon " + Percent(w) : "no weapon";
        string armour = gear.Armour is { } a ? "armour " + Percent(a) : "no armour";
        return weapon + ", " + armour;
    }

    static string Percent(float value) => ((int)MathF.Round(value * 100)).ToString(CultureInfo.InvariantCulture) + "%";
}

/// <summary><paramref name="Character"/> repairs its gear with Tools.</summary>
public sealed record RepairOrder(CharacterId Character) : SimCommand;
