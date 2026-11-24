using System.Globalization;

namespace Meitou.Struggle;

/// <summary>What the player is told (<see cref="Texts"/> turns it into the original's English or Chinese line).</summary>
enum Message
{
    Success,
    Slapped,
    Failed,
    Cooldown,
    Unconscious,
    HeadDown,
    Unavailable,
    NotReleased,
    PunishOn,
    PunishOff,
    NpcOn,
    NpcOff,
}

/// <summary>
/// The original's interface texts (<b>Verified</b>, <c>Struggle.dll</c> strings), in English and Chinese; the language is the setting's, Auto the
/// game's (Chinese for a <c>zh</c> locale).
/// </summary>
static class Texts
{
    internal static string GameLanguage { get; set; } = "en_GB";

    static bool Chinese
    {
        get
        {
            string lang = StruggleMod.Settings.Lang;
            if (lang == StruggleSettings.Auto) return GameLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

            return lang == StruggleSettings.Chinese;
        }
    }

    /// <summary>The menu row: the chance, or the cooldown left.</summary>
    internal static string Row(float chance, int cooldown)
    {
        if (cooldown > 0) return Chinese ? $"挣扎 · 冷却 {cooldown} 秒" : $"Struggle  Cooldown {cooldown}s";

        string c = chance.ToString("0", CultureInfo.InvariantCulture);
        return Chinese ? $"挣扎 (成功率 {c}%)" : $"Struggle (Chance {c}%)";
    }

    internal static string Of(Message message, float chance, int seconds)
    {
        string c = chance.ToString("0", CultureInfo.InvariantCulture);
        return message switch
        {
            Message.Success => Chinese ? $"挣扎成功！你挣脱了搬运 (成功率 {c}%)" : $"Struggle SUCCESS! Broke free (chance {c}%)",
            Message.Slapped => Chinese ? "挣扎失败，敌人愤怒的给了你一巴掌！" : "Struggle failed! The enemy angrily slapped you!",
            Message.Failed => Chinese ? "挣扎失败…" : "Struggle failed...",
            Message.Cooldown => Chinese ? $"挣扎冷却中：剩余 {seconds} 秒" : $"Struggle on cooldown: {seconds}s left",
            Message.Unconscious => Chinese ? "昏迷中无法挣扎" : "Unconscious - cannot struggle",
            Message.HeadDown => Chinese ? "头部血量低于0，无法挣扎" : "Head health below zero - cannot struggle",
            Message.Unavailable => Chinese ? "当前无法挣扎：状态或搬运关系无法确认" : "Cannot struggle: state or carrier unavailable",
            Message.NotReleased => Chinese ? "挣脱未完成，请稍后重试" : "Release could not be confirmed - try later",
            Message.PunishOn => Chinese ? "挣扎失败惩罚：开启" : "Failure punishment: ON",
            Message.PunishOff => Chinese ? "挣扎失败惩罚：关闭" : "Failure punishment: OFF",
            Message.NpcOn => Chinese ? "NPC 挣扎：开启" : "NPC Struggle: ON",
            _ => Chinese ? "NPC 挣扎：关闭" : "NPC Struggle: OFF",
        };
    }
}
