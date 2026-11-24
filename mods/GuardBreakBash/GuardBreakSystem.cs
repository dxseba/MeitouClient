using System.Globalization;
using System.Runtime.CompilerServices;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.GuardBreakBash;

/// <summary>
/// The original's block hook (<c>CombatClass::_blockHit</c>, <b>Verified</b> decompile of <c>GuardBreakBash.dll</c>), run from the Harmony
/// postfix on <c>CombatSystem.ApplyBlocked</c> (serial, in Commit): Bash rolls the blocker's force against the attacker's resistance and staggers
/// the attacker; Guard Break, independently (or only when Bash did not land, without Allow both effects), the attacker's force against the
/// blocker's resistance and staggers the blocker. A stagger needs a living, conscious target (not already stumbling, with Ignore already stumbling),
/// past that effect's cooldown on it, and a model: the target's last real stagger (from the hit reaction filter), else anyone's, no older than the
/// template age and seen often enough; heavy when the model's stagger damage passes the target's stumble threshold.
/// </summary>
sealed class GuardBreakSystem(CombatConstants constants, RngPurpose purpose, Action<string> log) : IBlowReactionFilter, IStateHashed
{
    static readonly ConditionalWeakTable<World, GuardBreakSystem> Systems = [];

    /// <summary>The last real stagger of each character, and of anyone: its stagger damage, tick and how many were seen.</summary>
    readonly Dictionary<CharacterId, Template> _templates = [];

    Template? _global;

    /// <summary>The tick each effect last staggered each character (the cooldown; game time, the original's is the wall clock's).</summary>
    readonly Dictionary<(CharacterId Who, int Effect), long> _applied = [];

    GuardBreakRules _rules = GuardBreakRules.Of(GuardBreakBashMod.Settings);

    int _lines;

    internal static GuardBreakSystem? Of(World world) => Systems.TryGetValue(world, out var system) ? system : null;

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        Systems.AddOrUpdate(world, this);
        _rules = GuardBreakRules.Of(GuardBreakBashMod.Settings);
    }

    /// <summary>A real stagger (the original's observed <c>startStumble</c>): kept as the model; the game's reaction is not changed.</summary>
    public BlowReactionKind Reaction(World world, in BlowReactionAsk ask)
    {
        if (ask.Kind == BlowReactionKind.None) return ask.Kind;

        int seen = _templates.TryGetValue(ask.Victim, out var old) ? old.Observations : 0;
        var template = new Template(ask.StaggerDamage, ask.Tick, seen + 1);
        _templates[ask.Victim] = template;
        _global = template;
        return ask.Kind;
    }

    /// <summary>The block of the blow being applied: attacker slot, blocker slot, tick (-1: none).</summary>
    (int A, int D, int Now) _noted = (-1, -1, 0);

    /// <summary>A blow of <paramref name="a"/> was blocked by <paramref name="d"/>; rolled once the blow is applied.</summary>
    internal void Noted(int a, int d, int now) => _noted = (a, d, now);

    /// <summary>The noted block, if any: Bash, then Guard Break.</summary>
    internal void Blocked(World world, CombatSystem combat)
    {
        var (a, d, now) = _noted;
        _noted = (-1, -1, 0);
        var rules = _rules;
        var table = world.Characters;
        if (a < 0 || !rules.Enabled || table.Cold(a) is not { Stats: not null } ac || table.Cold(d) is not { Stats: not null } dc) return;

        var (attacker, blocker) = (world.Characters.IdOf(a), world.Characters.IdOf(d));
        bool bashed = rules.Bash.Enabled && Roll(world, rules.Bash, dc, ac, attacker, rules, 0)
            && Stagger(world, combat, rules, rules.Bash, 0, a, ac, now);
        if (rules.GuardBreak.Enabled && (rules.AllowBoth || !bashed) && Roll(world, rules.GuardBreak, ac, dc, blocker, rules, 1))
            Stagger(world, combat, rules, rules.GuardBreak, 1, d, dc, now);
    }

    /// <summary>The effect's roll: the source's score against the target's on the curve; success under the chance (percent).</summary>
    bool Roll(World world, EffectRule effect, CharacterCold source, CharacterCold target, CharacterId targetId, GuardBreakRules rules, int salt)
    {
        float from = Score(effect.Source, source, rules.Effective), against = Score(effect.Target, target, rules.Effective);
        float chance = effect.Curve.Chance(from - against);
        float roll = Rng.Float(Rng.Hash(world.Seed, Rng.Key(targetId), purpose, (ulong)world.Tick * 2 + (ulong)salt)) * 100;
        if (rules.LogRolls)
        {
            Log($"{effect.Name} source={from:0.##} target={against:0.##} difference={from - against:0.##} chance={chance:0.##} roll={roll:0.##}");
        }

        return roll < chance;
    }

    static float Score(Reference reference, CharacterCold c, bool effective) =>
        reference.Score(Stat(c, StatsEnumerated.Strength, effective), Stat(c, StatsEnumerated.Dexterity, effective),
            Stat(c, StatsEnumerated.Toughness, effective));

    static float Stat(CharacterCold c, StatsEnumerated stat, bool effective) =>
        effective && CombatAccess.Eff is { } eff ? eff(c, stat) : c.Stats![stat];

    /// <summary>Staggers the character in <paramref name="slot"/> when it is eligible and has a model; true when it did.</summary>
    bool Stagger(World world, CombatSystem combat, GuardBreakRules rules, EffectRule effect, int effectIndex, int slot, CharacterCold cold, int now)
    {
        var id = world.Characters.IdOf(slot);
        if (Skip(world, combat, rules, effect, effectIndex, slot, id, cold, now) is { } reason)
        {
            if (rules.LogStagger) Log($"{effect.Name} skipped: {reason}");
            return false;
        }

        var model = Model(world, rules, id)!;
        float threshold = DamageFormulas.StumbleThreshold(Stat(cold, StatsEnumerated.Toughness, effective: true), constants);
        bool heavy = model.Value.Damage > threshold;
        ulong counter = Rng.Hash(world.Seed, Rng.Key(id), purpose, (ulong)now * 2 + (ulong)effectIndex + 0x5157);
        if (!CombatAccess.Stagger(combat, world, slot, cold, heavy, now, counter)) return false;

        _applied[(id, effectIndex)] = world.Tick;
        float force = MathF.Max(model.Value.Damage * effect.ForceMultiplier, effect.MinimumForce);
        if (rules.LogStagger)
        {
            Log($"{effect.Name} applied: target={id.Slot} damageTotal={model.Value.Damage:0.##} requestedForce={force:0.##} heavy={heavy}");
        }

        return true;
    }

    /// <summary>Why the character cannot be staggered now, or null.</summary>
    string? Skip(World world, CombatSystem combat, GuardBreakRules rules, EffectRule effect, int effectIndex, int slot, CharacterId id,
        CharacterCold cold, int now)
    {
        if (cold.Medical is not { Dead: false, Incapacitated: false }) return "target was not eligible for stagger.";
        if (CombatAccess.Slot(combat, slot) is not { } s) return "target CombatClass was null.";
        if (rules.IgnoreStumbling && s.StumbleClip >= 0 && now < s.StunUntil) return "target was not eligible for stagger.";
        if (s.HitPart < 0) return "selected native bodyPart was null.";
        if (Model(world, rules, id) is null) return "no native stumble template was available.";

        if (_applied.TryGetValue((id, effectIndex), out long last) && Milliseconds(world, world.Tick - last) < effect.CooldownMs)
            return "target cooldown active.";

        return null;
    }

    /// <summary>The model: the character's own last stagger, else anyone's, when young and seen often enough.</summary>
    Template? Model(World world, GuardBreakRules rules, CharacterId id)
    {
        bool Usable(Template t) => Milliseconds(world, world.Tick - t.Tick) <= rules.TemplateMaxAgeMs && t.Observations >= rules.MinimumObservations;

        if (_templates.TryGetValue(id, out var own) && Usable(own)) return own;

        return _global is { } global && Usable(global) ? global : null;
    }

    static double Milliseconds(World world, long ticks) => ticks * world.TickSeconds * 1000;

    /// <summary>Drops models and cooldowns too old to matter (every 10 s of game time).</summary>
    public void SlowWorld(World world)
    {
        if (world.Tick % (long)Math.Max(1, 10 / world.TickSeconds) != 0) return;

        var rules = _rules;
        foreach (var id in _templates.Where(t => Milliseconds(world, world.Tick - t.Value.Tick) > rules.TemplateMaxAgeMs).Select(t => t.Key).ToList())
            _templates.Remove(id);

        int longest = Math.Max(rules.Bash.CooldownMs, rules.GuardBreak.CooldownMs);
        foreach (var key in _applied.Where(e => Milliseconds(world, world.Tick - e.Value) > longest).Select(e => e.Key).ToList())
            _applied.Remove(key);
    }

    void Log(FormattableString text)
    {
        string line = text.ToString(CultureInfo.InvariantCulture);
        int max = GuardBreakBashMod.Settings.MaxLines;
        if (_lines > max) return;

        _lines++;
        log(_lines > max ? "Log limit reached; further lines suppressed." : line);
    }

    public void Hash(ref StateHasher hasher)
    {
        hasher.Add(_templates.Count);
        foreach (var (id, t) in _templates.OrderBy(e => e.Key.Slot).ThenBy(e => e.Key.Generation))
        {
            hasher.Add(id);
            t.Hash(ref hasher);
        }

        hasher.Add(_global is not null);
        if (_global is { } global) global.Hash(ref hasher);
        hasher.Add(_applied.Count);
        foreach (var ((id, effect), tick) in _applied.OrderBy(e => e.Key.Who.Slot).ThenBy(e => e.Key.Who.Generation).ThenBy(e => e.Key.Effect))
        {
            hasher.Add(id);
            hasher.Add(effect);
            hasher.Add(tick);
        }
    }

    /// <summary>A real stagger kept as a model: its stagger damage (cut + blunt + pierce + extra stun), tick and the count seen on that character.</summary>
    readonly record struct Template(float Damage, long Tick, int Observations)
    {
        public void Hash(ref StateHasher hasher)
        {
            hasher.Add(Damage);
            hasher.Add(Tick);
            hasher.Add(Observations);
        }
    }
}
