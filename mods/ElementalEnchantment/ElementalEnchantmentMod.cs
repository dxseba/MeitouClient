using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;

namespace Meitou.ElementalEnchantment;

/// <summary>
/// Port of Elemental Enchantment (workshop 3832978749): each character has one elemental level and picks a main element (fire, lightning, wind,
/// water, ice) in its ENCHANTMENT window, and from level 50 a secondary one at a share; they add to its blows and speed, train with landed hits
/// by its aptitude, and show on its blade or hands and on its hits. Bosses listed in the optional bosses.json fight enchanted; a worn ward protects and,
/// with two different elements, resonates.
/// </summary>
public sealed class ElementalEnchantmentMod : IMod
{
    internal static readonly EnchantSettings Settings = new();

    internal static volatile ElementSystem? Running;

    public void Initialize(ModContext context)
    {
        context.AddSettings(Settings);
        var skill = context.AddSkill(Skill());
        context.AddSystem("elements", _ => Running = new ElementSystem(Settings, skill), SystemAnchor.End);
        EnchantData.Load(context.Folder, context.Log);
        if (EnchantData.Recalibrator is { } recalibrator) context.AddItemUse(recalibrator, Recalibrate);

        var window = new EnchantWindow(context, Settings);
        context.AddUi(window);
        context.AddUi(new ElementMenu(window));
        context.AddUi(new WeaponAura(context, Settings));
        context.AddCombatHitFeed(new ElementEffects(context, Settings).Feed);
        context.Log("Elemental Enchantment initialized");
    }

    /// <summary>
    /// The stats window's line for the character's elemental level (the original shows it in its ENCHANTMENT window).
    /// </summary>
    static ModSkill Skill() => new("dxseba.element.level", "Elemental Enchantment", SkillColumn.Combat)
    {
        Description = "One level for every element. It grows by landing hits with a main element chosen in the ENCHANTMENT window.",
        Affects = ["Fire: blunt damage", "Lightning: armour penetration, attack speed", "Wind: attack and block speed", "Water: cut damage",
            "Ice: cut damage and bleeding"],
        Training = ["Landing hits with a main element"],
        Start = 0,
    };

    /// <summary>
    /// The aptitude recalibrator's right click, run by the running world's system.
    /// </summary>
    static bool Recalibrate(ItemUse use) => use.World.Systems.OfType<ElementSystem>().FirstOrDefault()?.Recalibrate(use) ?? false;
}

/// <summary>
/// The original's MCM settings (same names, ranges and defaults), plus this port's blade tint.
/// </summary>
public sealed class EnchantSettings
{
    [Setting("Enable Elemental Enchantment", Hint = "Elements add to blows and train with use.")]
    public bool Enabled { get; set; } = true;

    [Setting("Elemental XP multiplier", Hint = "Adjust Elemental XP gain. Aptitudes still apply; 0 stops XP gain.", Min = 0, Max = 20, Step = 0.1)]
    public float XpMultiplier { get; set; } = 1;

    [Setting("Effect strength (x)", Hint = "0: visuals only; 1: normal; 5: five times. Applies to allies and bosses.", Min = 0, Max = 5, Step = 0.1)]
    public float Strength { get; set; } = 1;

    [Setting("Weapon trails", Hint = "Show weapon trails while attacking (needs an engine trail; not drawn yet).")]
    public bool Trails { get; set; } = true;

    [Setting("Impact effects", Hint = "Show elemental bursts on hits from level 40.")]
    public bool Effects { get; set; } = true;

    [Setting("All elemental aptitudes 100%", Hint = "All player characters gain elemental experience at 100% speed. Original aptitudes return when disabled.")]
    public bool FlatAptitude { get; set; }

    [Setting("Recruit ability chance (%)", Hint = "Chance for newly recruited characters to have elemental ability. Existing characters are unchanged.",
        Min = 0, Max = 100, Step = 10)]
    public int RecruitChance { get; set; } = 100;

    [Setting("Resonance strength (x)", Hint = "Scales resonance bonuses together with the main effect multiplier; combined maximum 5x. 0 disables them.",
        Min = 0, Max = 5, Step = 0.1)]
    public float Resonance { get; set; } = 1;

    [Setting("Device damage reduction (%)", Hint = "A worn ward reduces incoming cut, blunt and pierce damage. 0 disables it, as does effect strength 0.",
        Min = 0, Max = 30, Step = 1)]
    public float DevicePercent { get; set; } = 10;

    [Setting("Device combat effects", Hint = "Show the ward's elemental effect on hits.")]
    public bool DeviceEffects { get; set; } = true;

    [Setting("Enchantment panel scale", Hint = "Resize the enchantment panel and its text. Default: 1.0.", Min = 0.8, Max = 2, Step = 0.1)]
    public float UiScale { get; set; } = 1;

    [Setting("Blade tint", Hint = "Also tints the blade in the element's colour (the original shows particles only).")]
    public bool BladeTint { get; set; }
}

/// <summary>
/// The "Enchantment" row on your own character's menu, which opens its ENCHANTMENT window (the original's button in the stats window).
/// </summary>
sealed class ElementMenu(EnchantWindow window) : IUiExtension
{
    public string Id => "dxseba.elementalenchantment.menu";

    public void Register(UiRegistration registration)
    {
        registration.AddCharacterMenuVerbs(Verbs);
        registration.AddCharacterPanelLines(Lines);
    }

    IEnumerable<UiMenuVerb> Verbs(UiMenuContext menu)
    {
        var target = menu.Target;
        if (!ElementalEnchantmentMod.Settings.Enabled || !target.IsPlayer || target.Body is not { Dead: false }) return [];
        if (ElementalEnchantmentMod.Running?.View.TryGetValue(target.Id, out var view) != true) return [];

        return [new UiMenuVerb($"Enchantment ({Caption(view!)})", () => window.Open(target.Id))];
    }

    /// <summary>
    /// The HUD character panel's ENCHANTMENT line: the element and level; a press opens the window.
    /// </summary>
    IEnumerable<UiCharacterLine> Lines(UiCharacterContext context)
    {
        if (!ElementalEnchantmentMod.Settings.Enabled || !context.Character.IsPlayer) return [];
        if (ElementalEnchantmentMod.Running?.View.TryGetValue(context.Id, out var view) != true) return [];

        return [new UiCharacterLine("ENCHANTMENT", Caption(view!), Press: () => window.Open(context.Id))];
    }

    static string Caption(EnchantView view) =>
        !view.Able ? "No elemental ability" : view.Main == Element.None ? $"none ({view.Level})" : $"{view.Main} {view.Level}";
}
