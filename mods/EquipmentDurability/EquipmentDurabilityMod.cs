using System.Globalization;
using Meitou.Game.Ui;
using Meitou.Localisation;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.EquipmentDurability;

/// <summary>
/// Port of Equipment Durability (workshop 3834344700, "Durability" 1.2.0), from its shipped source's behaviour and README: every weapon, crossbow and
/// covering armour piece rolls a durability in its class's range, player characters' use wears it (better grades slower), worn gear hits or protects
/// less and gear in good condition more, the tooltip shows it, and Repair Weapon / Repair Armour rows repair it with Tools. Settings come from
/// <c>Durability.cfg</c> in the mod folder, under the original's keys.
/// </summary>
public sealed class EquipmentDurabilityMod : IMod
{
    /// <summary>
    /// The settings, read at start.
    /// </summary>
    internal static DurabilityConfig Config { get; private set; } = new();

    /// <summary>
    /// The running world's system, for the interface; null before a world starts.
    /// </summary>
    internal static volatile DurabilitySystem? Running;

    public void Initialize(ModContext context)
    {
        Config = DurabilityConfig.Load(context.Folder, context.Log);
        if (!Config.Enabled)
        {
            context.Log("Enabled = 0 in " + DurabilityConfig.FileName + ": doing nothing");
            return;
        }

        context.AddSystem("durability", _ => Running = new DurabilitySystem(Config, context.Population, context.RngPurpose, context.Log),
            SystemAnchor.End);
        if (Config.Repair) context.AddUi(new RepairMenu());
        if (Config.ShowTooltip) context.AddUi(new DurabilityTooltip());
    }
}

/// <summary>
/// The original's own texts in the game's language (its <c>UiText</c> table: English, Portuguese, German, French, Russian, Spanish).
/// </summary>
readonly record struct UiText(string Label, string Broken, string Damage, string Resist, string Reload, string RepairWeapon, string RepairArmour)
{
    static readonly UiText English = new("Durability", "BROKEN", "dmg", "resist", "reload", "Repair Weapon", "Repair Armour");

    static readonly Dictionary<string, UiText> ByLanguage = new()
    {
        ["pt"] = new("Durabilidade", "QUEBRADO", "dano", "resist.", "recarga", "Reparar Arma", "Reparar Armadura"),
        ["de"] = new("Haltbarkeit", "DEFEKT", "Schaden", "Widerstand", "Nachladen", "Waffe reparieren", "Rüstung reparieren"),
        ["fr"] = new("Durabilité", "CASSÉ", "dégâts", "résistance", "rechargement", "Réparer l'arme", "Réparer l'armure"),
        ["ru"] = new("Прочность", "СЛОМАНО", "урон", "сопр.", "перезарядка", "Починить оружие", "Починить броню"),
        ["es"] = new("Durabilidad", "ROTO", "daño", "resist.", "recarga", "Reparar arma", "Reparar armadura"),
    };

    /// <summary>
    /// The texts of the game's language (English for any other).
    /// </summary>
    public static UiText Current
    {
        get
        {
            string code = Loc.Language.Code;
            string language = code.Length >= 2 ? code[..2].ToLowerInvariant() : "";
            return ByLanguage.GetValueOrDefault(language, English);
        }
    }
}

/// <summary>
/// Repair Weapon and Repair Armour on a player character's menu, when there is a repairer (the original's <c>PickRepairer</c>): with
/// <c>RepairOthers</c> and another character selected, that one if it is a player character carrying the repair item (else no rows); otherwise the
/// clicked character itself if it carries one.
/// </summary>
sealed class RepairMenu : IUiExtension
{
    public string Id => "dxseba.equipmentdurability.menu";

    public void Register(UiRegistration registration) => registration.AddCharacterMenuVerbs(Verbs);

    static IEnumerable<UiMenuVerb> Verbs(UiMenuContext menu)
    {
        var config = EquipmentDurabilityMod.Config;
        if (EquipmentDurabilityMod.Running is not { } running || !menu.Target.IsPlayer) return [];
        if (Repairer(menu, running.ToolHolders, config.RepairOthers) is not { } repairer) return [];

        var owner = menu.Target.Id;
        var texts = UiText.Current;
        string weapon = config.RepairWeaponText == "Repair Weapon" ? texts.RepairWeapon : config.RepairWeaponText;
        var rows = new List<UiMenuVerb> { new(weapon, () => menu.Enqueue(new RepairOrder(repairer, owner, false) { Tick = menu.Tick })) };
        if (!config.Armour) return rows;

        string armour = config.RepairArmourText == "Repair Armour" ? texts.RepairArmour : config.RepairArmourText;
        rows.Add(new(armour, () => menu.Enqueue(new RepairOrder(repairer, owner, true) { Tick = menu.Tick })));
        return rows;
    }

    static CharacterId? Repairer(UiMenuContext menu, IReadOnlySet<CharacterId> holders, bool others)
    {
        var target = menu.Target.Id;
        if (others && menu.Selected.Count > 0 && menu.Selected[0] != target) return holders.Contains(menu.Selected[0]) ? menu.Selected[0] : null;

        return holders.Contains(target) ? target : null;
    }
}

/// <summary>
/// The tooltip's Durability row (the original's <c>AddTooltipRow</c>): "N / max", or BROKEN at 0, then the effect: "(-NN% dmg)" or "(+NN% dmg)" for a
/// melee weapon, "(-NN% resist)" for armour, "(+N.N s reload)" for a crossbow. Gear not rolled yet shows none.
/// </summary>
sealed class DurabilityTooltip : IUiExtension
{
    public string Id => "dxseba.equipmentdurability.tooltip";

    public void Register(UiRegistration registration) => registration.AddItemTooltipLines(Lines);

    static IEnumerable<UiItemTooltipLine> Lines(UiItemTooltipContext item)
    {
        var config = EquipmentDurabilityMod.Config;
        if (EquipmentDurabilityMod.Running is not { } running || item.ModTag.Length == 0) return [];

        var rules = running.Rules;
        if (running.Type(item.Record) is not { } type || rules.ClassOf(item.Record, type, locked: false) is not { } c) return [];

        int cap = rules.RangeOf(c).Cap;
        if (Condition.InTag(item.ModTag, cap) is not { } dur) return [];

        var texts = UiText.Current;
        string label = config.TooltipLabel == "Durability" ? texts.Label : config.TooltipLabel;
        string shown = dur <= 0 ? texts.Broken : dur.ToString(CultureInfo.InvariantCulture) + " / " + cap.ToString(CultureInfo.InvariantCulture);
        return [new UiItemTooltipLine(label, shown + Effect(rules, c, dur, cap, texts))];
    }

    /// <summary>
    /// The effect after the value; empty when none applies.
    /// </summary>
    static string Effect(Condition rules, GearClass c, int dur, int cap, UiText texts)
    {
        if (c == GearClass.Ranged)
        {
            float delta = rules.ReloadDelta(dur, cap);
            if (delta > 0.05f) return " (+" + delta.ToString("0.0", CultureInfo.InvariantCulture) + " s " + texts.Reload + ")";
            if (delta < -0.05f) return " (" + delta.ToString("0.0", CultureInfo.InvariantCulture) + " s " + texts.Reload + ")";
            return "";
        }

        bool armour = Condition.IsArmour(c);
        float factor = armour ? rules.ResistFactor(dur, cap) : rules.DamageFactor(dur, cap);
        string word = armour ? texts.Resist : texts.Damage;
        if (factor < 0.999f) return " (-" + Percent(1 - factor) + "% " + word + ")";
        if (factor > 1.001f) return " (+" + Percent(factor - 1) + "% " + word + ")";
        return "";
    }

    static string Percent(float share) => ((int)(share * 100 + 0.5f)).ToString(CultureInfo.InvariantCulture);
}
