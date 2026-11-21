using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.KenshiAffixes;

/// <summary>
/// Port of Kenshi Affixes (workshop 3821983877, GPL-3.0-or-later; rebuilt from its description): persistent random affixes on melee weapons and
/// armour, by item level; stat affixes work while the gear is equipped, weapon affixes raise its damage; "Show affixes" lists a character's.
/// </summary>
public sealed class KenshiAffixesMod : IMod
{
    internal static readonly AffixSettings Settings = new();

    public void Initialize(ModContext context)
    {
        context.AddSettings(Settings);
        context.AddSystem("affixes", _ => new AffixSystem(Settings, context.RngPurpose), SystemAnchor.End);
        context.AddUi(new AffixMenu());
        context.Log("Kenshi Affixes initialized");
    }
}

public sealed class AffixSettings
{
    [Setting("Enable Kenshi Affixes", Hint = "Roll and apply affixes on weapons and armour.")]
    public bool Enabled { get; set; } = true;

    [Setting("Affix chance", Hint = "Scales how often gear rolls affixes (at item level 100 the first one rolls with this chance).", Min = 0, Max = 1.5,
        Step = 0.05)]
    public float AffixChance { get; set; } = 0.9f;
}

/// <summary>The "Show affixes" row on any living character the player can see.</summary>
sealed class AffixMenu : IUiExtension
{
    public string Id => "dxseba.kenshiaffixes.menu";

    public void Register(UiRegistration registration) => registration.AddCharacterMenuVerbs(Verbs);

    static IEnumerable<UiMenuVerb> Verbs(UiMenuContext menu)
    {
        if (!KenshiAffixesMod.Settings.Enabled || menu.Target.Body is not { Dead: false }) return [];

        var target = menu.Target.Id;
        return [new UiMenuVerb("Show affixes", () => menu.Enqueue(new ShowAffixesOrder(target) { Tick = menu.Tick }))];
    }
}

/// <summary>Posts the affixes of <paramref name="Character"/>'s equipped gear.</summary>
public sealed record ShowAffixesOrder(CharacterId Character) : SimCommand;
