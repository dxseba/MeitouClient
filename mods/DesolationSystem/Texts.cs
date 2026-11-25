namespace Meitou.DesolationSystem;

/// <summary>
/// The port's own texts: the perks' short names (the codex's names from the installed original replace them, <see cref="PerkCodex"/>) and the
/// cards' captions in English and Chinese.
/// </summary>
static class Texts
{
    static readonly Dictionary<int, string> Names = new()
    {
        [1] = "Str Pierce", [2] = "Strength Gap Recoil", [3] = "Defense Master", [5] = "Hyper Armor", [6] = "Shockwave", [7] = "Katana Tear",
        [8] = "Saber Guard", [9] = "Hackers Mastery", [10] = "Blunt Impact", [11] = "Heavy Onslaught", [12] = "Polearm Supremacy",
        [13] = "Martial Burst", [15] = "Squad Volley", [16] = "Iron Guard", [17] = "Perceptive Insight", [18] = "Last Light",
        [20] = "Hemostasis Miracle", [21] = "Master Hemopoiesis", [22] = "Field Rations", [23] = "Nano-Repair", [27] = "Master Porter",
        [101] = "Physical Sanctification", [102] = "Universal Weapon Mastery", [103] = "Shadow Sovereign", [104] = "Cybernetic Overlord",
        [105] = "Wilderness Sovereign", [202] = "Field Nutrition", [204] = "Battlefield Healing", [205] = "Evasion Master",
        [206] = "Toughness Reduction", [207] = "Crossbow Mastery", [209] = "Lightning Counter", [210] = "Athletics Momentum",
        [211] = "Aquatic Dragon", [213] = "Blade Unity", [214] = "Steel Body", [216] = "Master Ryu", [220] = "Tough Vitality",
        [221] = "SW Resist", [222] = "Wind-Eroded Wall", [19] = "Afterimage Step", [24] = "Sleight of Hand", [25] = "Silent Touch",
        [26] = "Master Key", [106] = "Heavenly Artifice", [201] = "Prosthetic Tuning", [208] = "Phantom Movement", [212] = "Grand Fletcher",
        [215] = "Turret Barrage", [218] = "Silent Verdict",
    };

    /// <summary>Every perk id the port knows, in the original's order.</summary>
    internal static IReadOnlyList<string> Ids { get; } = [.. Names.Keys.Order().Select(k => k.ToString(System.Globalization.CultureInfo.InvariantCulture))];

    /// <summary>Whether the port knows a perk of that id.</summary>
    internal static bool Known(int id) => Names.ContainsKey(id);

    /// <summary>A perk's name in the chosen language.</summary>
    internal static string Name(string id, bool chinese)
    {
        if (PerkCodex.Entries.TryGetValue(id, out var entry)) return chinese ? entry.ChineseName : entry.Name;

        return int.TryParse(id, out int n) && Names.TryGetValue(n, out var name) ? name : id;
    }

    /// <summary>A perk's "requirement|effect" text, empty without the original.</summary>
    internal static string Description(string id, bool chinese) =>
        PerkCodex.Entries.TryGetValue(id, out var entry) ? (chinese ? entry.ChineseDescription : entry.Description) : "";

    internal static readonly string[] StylesEn = ["Gale", "Balance", "Crush"];
    internal static readonly string[] StylesZh = ["疾风", "中庸", "重击"];
    internal static readonly string[] ScopesEn = ["Global", "Player Only", "NPC Only"];
    internal static readonly string[] ScopesZh = ["全局", "仅玩家", "仅NPC"];

    internal static string Scope(int scope, bool zh) => (zh ? "范围: " : "Scope: ") + (zh ? ScopesZh : ScopesEn)[Math.Clamp(scope, 0, 2)];

    internal static string Cap(float cap, bool zh) => (zh ? "攻速上限 " : "Atk Speed Cap ") + cap.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture) + "x";

    internal static string Realms(bool on, bool zh) => zh ? (on ? "极境: 开" : "极境: 关") : on ? "Supreme Realms: ON" : "Supreme Realms: OFF";

    internal static string Style(int style, bool zh)
    {
        string name = style is >= 0 and <= 2 ? (zh ? StylesZh : StylesEn)[style] : zh ? "无" : "None";
        return (zh ? "流法: " : "Ryu: ") + name;
    }

    internal static string Verdict(string mode, bool zh) => zh
        ? "暗杀: " + mode switch { DesoSettings.VerdictSneak => "偷袭", DesoSettings.VerdictHarvest => "收割", _ => "关" }
        : "Assassinate: " + mode;

    internal static string Config(bool zh) => zh ? "专长配置" : "Perk Config";

    internal static string Codex(bool zh) => zh ? "专长图鉴" : "Perk Codex";

    internal static string Language(bool zh) => zh ? "English" : "中文";

    internal static string Panel(bool zh) => zh ? "荒寂体系 · 已触发专长" : "Desolation System - active perks";

    internal static string NoPerks(bool zh) => zh ? "无已激活专长" : "No active perks";

    internal static string NoCharacter(bool zh) => zh ? "未选择角色" : "No inspected character";
}
