using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.WeaponDodgeGuard;

/// <summary>
/// Port of Weapon Dodge &amp; Guard (Tata-Gari, workshop 3767056805, GPLv3): a fighter holding a weapon that blocks may dodge first, with the
/// unarmed dodge chance (the game's own formula) scaled and clamped, then guard as usual. A postfix on the engine's defence choice
/// (<c>CombatSystem.ChooseDefence</c>, the original's <c>CharStats::chooseBlock</c> hook) replaces its standing block with a dodge technique.
/// </summary>
public sealed class WeaponDodgeGuardMod : IMod
{
    internal static readonly DodgeSettings Settings = new();

    internal static RngPurpose Purpose { get; private set; }

    public void Initialize(ModContext context)
    {
        context.AddSettings(Settings);
        ReadLists(Path.Combine(context.Folder, "WeaponDodgeGuard.ini"));
        Purpose = context.RngPurpose;
        DodgeHook.Install(context);
        context.Log("Weapon Dodge & Guard initialized");
    }

    /// <summary>
    /// The <c>[Animations]</c> lists of an ini in the original's format, when the user puts one in the mod's folder.
    /// </summary>
    static void ReadLists(string path)
    {
        if (!File.Exists(path)) return;

        string section = "";
        foreach (var raw in File.ReadLines(path))
        {
            string line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']')) section = line[1..^1];
            if (section != "Animations" || line.Split('=', 2) is not [var key, var value]) continue;

            if (key.Trim() == "AllowedAnimations") Settings.Allowed = value.Trim();
            if (key.Trim() == "BlockedAnimations") Settings.Blocked = value.Trim();
        }
    }
}

/// <summary>
/// The original's <c>[Animations] AnimationMode</c>.
/// </summary>
public enum AnimationMode
{
    /// <summary>
    /// 0: only the vanilla dodge back.
    /// </summary>
    VanillaOnly,

    /// <summary>
    /// 1: every standing dodge technique (the allow and block lists apply), else the dodge back.
    /// </summary>
    Compatible,

    /// <summary>
    /// 2: only the standing dodges named in Allowed animations.
    /// </summary>
    CustomAllowList,
}

/// <summary>
/// The original's <c>WeaponDodgeGuard.ini</c> (v1.2.0-rc1) keys and shipped defaults.
/// </summary>
public sealed class DodgeSettings
{
    /// <summary>
    /// <c>[General] Mode</c> 1 (0 observes only).
    /// </summary>
    [Setting("Enable weapon dodge", Hint = "Armed fighters may dodge before they guard (Mode=1).")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// <c>[Dodge] ChanceScale</c>, 1.0.
    /// </summary>
    [Setting("Chance scale", Hint = "Times the vanilla dodge chance (ChanceScale).", Min = 0, Max = 5, Step = 0.05)]
    public float ChanceScale { get; set; } = 1;

    /// <summary>
    /// <c>[Dodge] MinimumChance</c>, 0.
    /// </summary>
    [Setting("Minimum chance", Hint = "The scaled dodge chance is at least this, in per cent (MinimumChance).", Min = 0, Max = 100, Step = 1)]
    public float MinimumChance { get; set; }

    /// <summary>
    /// <c>[Dodge] MaximumChance</c>, 95.
    /// </summary>
    [Setting("Maximum chance", Hint = "The scaled dodge chance is at most this, in per cent (MaximumChance).", Min = 0, Max = 100, Step = 1)]
    public float MaximumChance { get; set; } = 95;

    /// <summary>
    /// <c>[Priority] AdaptivePriority</c>, 0.
    /// </summary>
    [Setting("Adaptive priority", Hint = "On: a fighter dodges first only while its effective dodge is at least its effective melee defence.")]
    public bool Adaptive { get; set; }

    /// <summary>
    /// <c>[Restrictions] AllowInDefensiveMode</c>, 1.
    /// </summary>
    [Setting("Allow in defensive mode", Hint = "Off: a fighter in defensive (Block) mode only guards.")]
    public bool AllowInDefensiveMode { get; set; } = true;

    /// <summary>
    /// <c>[Restrictions] RequireNormalProneState</c>, 1.
    /// </summary>
    [Setting("Require standing", Hint = "On: only a fighter standing normally (not staying low or crawling) dodges.")]
    public bool RequireStanding { get; set; } = true;

    /// <summary>
    /// <c>[Restrictions] DodgeCooldownMs</c>, 0; in game time here.
    /// </summary>
    [Setting("Dodge cooldown (ms)", Hint = "Game milliseconds after a weapon dodge before the next may come (0: none).", Min = 0, Max = 10000, Step = 50)]
    public int CooldownMs { get; set; }

    /// <summary>
    /// <c>[Animations] AnimationMode</c>, 1 (Compatible).
    /// </summary>
    [Setting("Animation mode", Hint = "VanillaOnly: the dodge back; Compatible: any standing dodge technique; CustomAllowList: only the allowed ones.")]
    public AnimationMode Animations { get; set; } = AnimationMode.Compatible;

    /// <summary>
    /// <c>[Animations] AllowedAnimations</c>: comma-separated technique names, read from <c>WeaponDodgeGuard.ini</c> in the mod's folder (text is not
    /// a mod setting).
    /// </summary>
    public string Allowed { get; set; } = "";

    /// <summary>
    /// <c>[Animations] BlockedAnimations</c>, read as <see cref="Allowed"/>.
    /// </summary>
    public string Blocked { get; set; } = "";
}
