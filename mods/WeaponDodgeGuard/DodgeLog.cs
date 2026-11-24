using System.Globalization;
using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.WeaponDodgeGuard;

/// <summary>
/// One defence choice the filter made: the technique it answers (null: the game's pick stands), why (the original's decision names), and the
/// effective dodge, scaled chance and roll when it got that far (-1: not reached).
/// </summary>
readonly record struct DodgeDecision(int? Pick, string Reason, float Dodge = -1, float Chance = -1, float Roll = -1);

/// <summary>
/// The original's <c>[Logging]</c> and logging keys, to the mod log: <c>Verbose</c> (the settings at start and the choices the gate turns away
/// before it looks at the fighter), <c>LogSelections</c> (each choice past that), <c>LogDecisions</c> (Adaptive priority),
/// <c>LogDiscoveredTechniques</c> (the standing dodges found, once a world), <c>LogSelectedAnimation</c> (each dodge taken), at most <c>MaxLines</c> lines a session, then one line
/// saying so. Called from the combat phase on any thread; each check is one read while the options are off.
/// </summary>
sealed class DodgeLog(Action<string> write)
{
    int _lines;

    /// <summary>
    /// Whether the standing dodges were listed for this world.
    /// </summary>
    int _listed;

    static DodgeSettings Settings => WeaponDodgeGuardMod.Settings;

    /// <summary>
    /// The settings line (<c>Verbose</c>).
    /// </summary>
    public void Started()
    {
        var s = Settings;
        if (!s.Verbose) return;

        Line(Invariant($"Settings loaded: Mode={B(s.Enabled)}, Verbose=1, LogSelections={B(s.LogSelections)}, ChanceScale={s.ChanceScale}, ")
            + Invariant($"MinChance={s.MinimumChance}, MaxChance={s.MaximumChance}, AllowInDefensiveMode={B(s.AllowInDefensiveMode)}, ")
            + Invariant($"RequireNormalProneState={B(s.RequireStanding)}, DodgeCooldownMs={s.CooldownMs}, AnimationMode={(int)s.Animations}, ")
            + Invariant($"AdaptivePriority={B(s.Adaptive)}, AllowedAnimations={s.Allowed}, BlockedAnimations={s.Blocked}, MaxLines={s.MaxLines}"));
    }

    /// <summary>
    /// The standing dodge techniques, once (<c>LogDiscoveredTechniques</c>).
    /// </summary>
    public void Discovered(IReadOnlyList<CombatTechnique> all)
    {
        if (!Settings.LogDiscoveredTechniques || Interlocked.Exchange(ref _listed, 1) != 0) return;

        for (int i = 0; i < all.Count; i++)
        {
            if (all[i] is not { IsDodge: true, IsStumbleDodge: false, IsProne: false } t) continue;

            Line(Invariant($"Discovered normal standing dodge animation=\"{t.Name}\" index={i} minSkill={t.MinSkill} maxSkill={t.MaxSkill} ")
                + Invariant($"chanceMult={t.Chance} block={B(t.IsBlock)}"));
        }
    }

    /// <summary>
    /// A choice the gate turned away before the fighter (<c>Verbose</c>) or after (<c>LogSelections</c>).
    /// </summary>
    public void Choice(World world, in DefenceChoiceAsk ask, DodgeDecision decision, bool early, IReadOnlyList<CombatTechnique> all)
    {
        if (!(early ? Settings.Verbose : Settings.LogSelections)) return;

        string line = Invariant($"chooseBlock: character={Name(world, ask.Defender)} opponent={Name(world, ask.Attacker)} ")
            + Invariant($"decision={decision.Reason} original={{{Technique(all, ask.Technique)}}} ")
            + Invariant($"selected={{{Technique(all, decision.Pick ?? ask.Technique)}}}");
        if (decision.Chance >= 0)
        {
            line += Invariant($" dodgeEffective={decision.Dodge:0.##} adjustedDodgeChance={decision.Chance:0.##} roll={decision.Roll:0.##}");
        }

        Line(line);
    }

    /// <summary>
    /// Adaptive priority's decision (<c>LogDecisions</c>).
    /// </summary>
    public void Priority(World world, CharacterId who, float dodge, float defence)
    {
        if (!Settings.LogDecisions) return;

        string verdict = dodge < defence ? "weaponDodge=suppressed priority=defence" : "weaponDodge=enabled priority=dodge";
        string name = Name(world, who);
        Line(Invariant($"Adaptive priority: character={name} effectiveDodge={dodge:0.##} meleeDefenceEffective={defence:0.##} {verdict}"));
    }

    /// <summary>
    /// The dodge taken (<c>LogSelectedAnimation</c>).
    /// </summary>
    public void Selected(IReadOnlyList<CombatTechnique> all, int pick)
    {
        if (!Settings.LogSelectedAnimation) return;

        Line(Invariant($"Selected dodge animation=\"{all[pick].Name}\" chanceMult={all[pick].Chance}"));
    }

    /// <summary>
    /// A new world lists its techniques again.
    /// </summary>
    public void NewWorld() => Interlocked.Exchange(ref _listed, 0);

    void Line(string text)
    {
        int n = Interlocked.Increment(ref _lines);
        int max = Settings.MaxLines;
        if (n < max) write(text);
        else if (n == max) write("Log limit reached; further lines are not written.");
    }

    static string Technique(IReadOnlyList<CombatTechnique> all, int index) => index >= 0 && index < all.Count ? all[index].Name : "<none>";

    static string Name(World world, CharacterId id) => world.Characters.NameOf(id) is { Length: > 0 } name ? name : "<null>";

    static int B(bool value) => value ? 1 : 0;

    static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
