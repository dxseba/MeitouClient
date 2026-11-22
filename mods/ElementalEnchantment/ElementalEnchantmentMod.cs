using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.ElementalEnchantment;

/// <summary>
/// Port of Elemental Enchantment (workshop 3832978749), rebuilt from its description: each character picks an element (fire, lightning, wind,
/// water, ice) that adds to its blows and grows with use as a skill of its own in the stats window; from level 50 a second element joins at a share.
/// </summary>
public sealed class ElementalEnchantmentMod : IMod
{
    internal static readonly EnchantSettings Settings = new();

    internal static volatile ElementSystem? Running;

    public void Initialize(ModContext context)
    {
        context.AddSettings(Settings);
        var skills = Elements.All.ToDictionary(e => e, e => context.AddSkill(Elements.Skill(e)));
        context.AddSystem("elements", _ => Running = new ElementSystem(Settings, skills), SystemAnchor.End);
        context.AddUi(new ElementMenu());
        context.Log("Elemental Enchantment initialized");
    }
}

public sealed class EnchantSettings
{
    [Setting("Enable Elemental Enchantment", Hint = "Elements add to blows and train with use.")]
    public bool Enabled { get; set; } = true;

    [Setting("Elemental XP", Hint = "Scales how fast elements train.", Min = 0, Max = 20, Step = 0.1)]
    public float XpMultiplier { get; set; } = 1;

    [Setting("Effect strength", Hint = "Scales what elements add (0: none).", Min = 0, Max = 5, Step = 0.1)]
    public float Strength { get; set; } = 1;

    [Setting("All aptitudes at 100%", Hint = "Every character trains every element at the same rate.")]
    public bool FlatAptitude { get; set; }

    /// <summary>The training amount per landed hit, before the curve, aptitude and multiplier.</summary>
    public float XpPerHit { get; set; } = 2;
}

/// <summary>The "Element: X (level)" row on your own character: a choice cycles to the next element (and none).</summary>
sealed class ElementMenu : IUiExtension
{
    public string Id => "dxseba.elementalenchantment.menu";

    public void Register(UiRegistration registration) => registration.AddCharacterMenuVerbs(Verbs);

    static IEnumerable<UiMenuVerb> Verbs(UiMenuContext menu)
    {
        var target = menu.Target;
        if (!ElementalEnchantmentMod.Settings.Enabled || !target.IsPlayer || target.Body is not { Dead: false }) return [];
        if (ElementalEnchantmentMod.Running?.View.TryGetValue(target.Id, out var view) != true) return [];

        string caption = view.Element == Element.None ? "Element: none" : $"Element: {view.Element} ({view.Level})";
        return [new UiMenuVerb(caption, () => menu.Enqueue(new CycleElementOrder(target.Id) { Tick = menu.Tick }))];
    }
}

/// <summary><paramref name="Character"/> switches to the next element.</summary>
public sealed record CycleElementOrder(CharacterId Character) : SimCommand;
