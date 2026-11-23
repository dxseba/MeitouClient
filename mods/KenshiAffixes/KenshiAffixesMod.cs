using System.Globalization;
using System.Numerics;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.KenshiAffixes;

/// <summary>
/// Port of Kenshi Affixes (workshop 3821983877, GPL-3.0-or-later): persistent random affixes on melee weapons and armour by item level (NPC gear,
/// shop stock), authored ones on unique NPCs and Meitou weapons; stat affixes work while the gear is equipped, weapon affixes raise its damage,
/// rare ones give critical hits and stun leech, armour resistance affixes raise its protection; the item tooltip lists an item's affixes.
/// </summary>
public sealed class KenshiAffixesMod : IMod
{
    internal static readonly AffixSettings Settings = new();

    public void Initialize(ModContext context)
    {
        context.AddSettings(Settings);
        var profiles = Profiles.Load(context);
        context.AddSystem("affixes", build => new AffixSystem(Settings, context.RngPurpose, build.Data.Combat, profiles), SystemAnchor.End);
        context.AddUi(new AffixMenu());
        context.AddUi(new AffixTooltip());
        context.Log("Kenshi Affixes initialized");
    }
}

/// <summary>The original's KenshiAffixes.ini keys that apply here, with its defaults.</summary>
public sealed class AffixSettings
{
    [Setting("Enable Kenshi Affixes", Hint = "Roll and apply affixes on weapons and armour.")]
    public bool Enabled { get; set; } = true;

    [Setting("NPC gear", Hint = "Roll affixes on the gear NPCs wear (AutoNPC).")]
    public bool AutoNpc { get; set; } = true;

    [Setting("Shop stock", Hint = "Roll affixes on the gear shops sell (AutoShops).")]
    public bool AutoShops { get; set; } = true;

    [Setting("Container loot", Hint = "Roll affixes on new loot in containers and camps (AutoContainers).")]
    public bool AutoContainers { get; set; } = true;

    [Setting("Crafted gear", Hint = "Roll affixes on gear your characters craft, from their smithing skill (AutoCrafting).")]
    public bool AutoCrafting { get; set; } = true;

    [Setting("Affix prices", Hint = "Affixed gear is worth more (AffixPrices).")]
    public bool AffixPrices { get; set; } = true;

    [Setting("Value % per affix", Hint = "Price premium per affix at item level 0 (ValueBasePercentPerAffix).", Min = 0, Max = 100)]
    public int ValueBasePercent { get; set; } = 8;

    [Setting("Value % per affix at iLvl 100", Hint = "Premium per affix added up to item level 100 (ValueLevelPercentPerAffix).", Min = 0,
        Max = 100)]
    public int ValueLevelPercent { get; set; } = 32;

    [Setting("Shop base level", Hint = "Item level of the lowest quality band's shop stock (ShopBaseLevel).", Min = 1, Max = 100)]
    public int ShopBaseLevel { get; set; } = 12;

    [Setting("Shop level per band", Hint = "Item levels added per quality band (ShopLevelPerBand).", Min = 0, Max = 20)]
    public int ShopLevelPerBand { get; set; } = 16;
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

/// <summary>
/// The original's tooltip block on rolled gear: "Affixes [FIXED] iLvl 50 / T3" (the tier (iLvl - 1) / 20 + 1), "No affixes (rolled once)" for an
/// empty roll, "Affix value +x%" with prices on, then each affix with its effect; the header and affixes in the item level's rank colour.
/// </summary>
sealed class AffixTooltip : IUiExtension
{
    public string Id => "dxseba.kenshiaffixes.tooltip";

    public void Register(UiRegistration registration) => registration.AddItemTooltipLines(Lines);

    static IEnumerable<UiItemTooltipLine> Lines(UiItemTooltipContext item)
    {
        if (!KenshiAffixesMod.Settings.Enabled || AffixTag.Level(item.ModTag) is not { } level) return [];

        var settings = KenshiAffixesMod.Settings;
        var rank = new UiLineStyle(Label: Rank(level));
        string flag = AffixTag.Profile(item.ModTag) is null ? "" : "[FIXED] ";
        string tier = string.Create(CultureInfo.InvariantCulture, $"iLvl {level} / T{(level - 1) / 20 + 1}");
        var lines = new List<UiItemTooltipLine> { new("Affixes " + flag + tier) { Style = rank } };

        var affixes = AffixTag.Read(item.ModTag);
        if (affixes.Count == 0) lines.Add(new("No affixes (rolled once)"));
        if (settings.AffixPrices && affixes.Count > 0)
        {
            double premium = (AffixSystem.ValueMultiplier(item.ModTag, settings.ValueBasePercent, settings.ValueLevelPercent) - 1) * 100;
            lines.Add(new("Affix value +" + premium.ToString("0.0", CultureInfo.InvariantCulture) + "%"));
        }

        lines.AddRange(affixes.Select(a => new UiItemTooltipLine(a.Text, a.Kind.Effect) { Style = rank }));
        return lines;
    }

    /// <summary>The original's rank colours by item level (#999999 below 15 up to #FF6262 from 90).</summary>
    static Vector4 Rank(int level) => level switch
    {
        < 15 => Rgb(0x999999),
        < 30 => Rgb(0xFFFFFF),
        < 45 => Rgb(0x50D878),
        < 60 => Rgb(0x53A7FF),
        < 75 => Rgb(0xBC83FF),
        < 90 => Rgb(0xFFA84A),
        _ => Rgb(0xFF6262),
    };

    static Vector4 Rgb(uint rgb) => new(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, 1);
}

/// <summary>Posts the affixes of <paramref name="Character"/>'s equipped gear.</summary>
public sealed record ShowAffixesOrder(CharacterId Character) : SimCommand;
