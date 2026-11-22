using System.Globalization;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.KenshiAffixes;

/// <summary>
/// Port of Kenshi Affixes (workshop 3821983877, GPL-3.0-or-later; rebuilt from its description): persistent random affixes on melee weapons and
/// armour, by item level; stat affixes work while the gear is equipped, weapon affixes raise its damage, armour resistance affixes its protection;
/// the item tooltip lists an item's affixes and "Show affixes" a character's.
/// </summary>
public sealed class KenshiAffixesMod : IMod
{
    internal static readonly AffixSettings Settings = new();

    public void Initialize(ModContext context)
    {
        context.AddSettings(Settings);
        context.AddSystem("affixes", _ => new AffixSystem(Settings, context.RngPurpose), SystemAnchor.End);
        context.AddUi(new AffixMenu());
        context.AddUi(new AffixTooltip());
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

/// <summary>The item tooltip's lines on rolled gear: its item level, then one line per affix.</summary>
sealed class AffixTooltip : IUiExtension
{
    public string Id => "dxseba.kenshiaffixes.tooltip";

    public void Register(UiRegistration registration) => registration.AddItemTooltipLines(Lines);

    static IEnumerable<UiItemTooltipLine> Lines(UiItemTooltipContext item)
    {
        if (!KenshiAffixesMod.Settings.Enabled || Affixes.LevelIn(item.ModTag) is not { } level) return [];

        var affixes = Affixes.In(item.ModTag);
        var lines = new List<UiItemTooltipLine> { new("Item level", level.ToString(CultureInfo.InvariantCulture)) };
        if (affixes.Count == 0) lines.Add(new("No affixes"));
        lines.AddRange(affixes.Select(a => new UiItemTooltipLine(a.Text)));
        return lines;
    }
}

/// <summary>Posts the affixes of <paramref name="Character"/>'s equipped gear.</summary>
public sealed record ShowAffixesOrder(CharacterId Character) : SimCommand;
