using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using static Meitou.Data.Gameplay.Bodies.StatsEnumerated;

namespace Meitou.DesolationSystem;

/// <summary>
/// The reaction, defence and hit hooks: Hyper Armor, Wind-Eroded Wall and Heavy Onslaught on the stagger; Strength Gap Recoil, Saber Guard's riposte
/// and Lightning Counter on a block; a polearm's slow and the attackers Lightning Counter counts on a hit.
/// </summary>
sealed partial class DesoSystem : IBlowReactionFilter, ICombatDefenceObserver, ICombatHitObserver
{
    /// <summary>Lightning Counter's window (5 s), burst (1.2 s) and Polearm Supremacy's slow (5 s), in seconds.</summary>
    const float CounterWindow = 5, BurstSeconds = 1.2f, SlowSeconds = 5;

    public BlowReactionKind Reaction(World world, in BlowReactionAsk ask)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(ask.Victim, out int v) || table.Cold(v) is not { Stats: { } vs } victim) return ask.Kind;

        var cfg = _cfg;
        var attacker = table.TryResolveNext(ask.Attacker, out int a) ? table.Cold(a) : null;
        float barrage = ask.Source == CombatHitSource.Projectile ? Turret(world, attacker, cfg) : 1;
        if (barrage > 1 && ask.Kind != BlowReactionKind.Heavy && ask.StaggerDamage * barrage > ask.Threshold) return BlowReactionKind.Heavy;
        if (ask.Kind == BlowReactionKind.None) return ask.Kind;

        float gap = attacker?.Stats is { } st ? Gates.L(st, Strength) - Gates.L(vs, Strength) : 0;
        var kind = ask.Kind;
        if (attacker?.Stats is { } ast && Heavy(attacker, ast, cfg) is var more and > 0 && kind == BlowReactionKind.Light
            && ask.StaggerDamage * (1 + more) > ask.Threshold)
            kind = BlowReactionKind.Heavy;

        if (!Gates.InScope(victim, cfg.ScopeIndex)) return kind;
        if (cfg.P222 && kind == BlowReactionKind.Heavy && Perks.Wall(vs)) kind = BlowReactionKind.Light;
        if (cfg.P5 && Perks.HyperArmor(vs, cfg) && gap <= 30 && combat?.Playing(ask.Victim, world.Tick) is { IsAttack: true }) kind = BlowReactionKind.None;
        return kind;
    }

    /// <summary>Heavy Onslaught: the stagger's extra share (10 / 20 / 50 %) with a heavy weapon in hand above the threshold; 0 otherwise.</summary>
    static float Heavy(CharacterCold attacker, CharacterStats st, DesoSettings cfg)
    {
        if (!cfg.P11 || !Gates.InScope(attacker, cfg.ScopeIndex) || attacker.Fighter is not { Unarmed: false, WeaponSkill: HeavyWeapons }) return 0;

        float level = Gates.L(st, HeavyWeapons);
        return level > cfg.MasteryThreshold ? Mastery.Value(level, 0.10f, 0.50f) : 0;
    }

    /// <summary>
    /// Turret Barrage: a gunner (turrets above the threshold, manning a turret in the last second) staggers 1 / 1.375 / 2.5 times as hard, at most 4.
    /// </summary>
    static float Turret(World world, CharacterCold? gunner, DesoSettings cfg)
    {
        if (!cfg.P215 || gunner is not { Stats: { } s } || !Gates.InScope(gunner, cfg.ScopeIndex)) return 1;
        if (gunner.SkillInUse != Turrets || world.Tick - gunner.SkillInUseTick > Ticks(world, 1)) return 1;

        float level = Gates.L(s, Turrets);
        return level > cfg.MasteryThreshold ? Math.Min(4, Mastery.Value(level, 1, 2.5f)) : 1;
    }

    public void Defended(World world, in CombatDefence defence)
    {
        var table = world.Characters;
        if (defence.Kind != CombatDefenceKind.Blocked) return;
        if (!table.TryResolveNext(defence.Defender, out int d) || table.Cold(d) is not { Stats: { } ds } defender) return;
        if (!table.TryResolveNext(defence.Attacker, out int a) || table.Cold(a) is not { Stats: { } st } attacker) return;

        var cfg = _cfg;
        Recoil(attacker, st, defence.Defender, ds, cfg);
        Riposte(world, defence, defender, ds, cfg);
        Counter(world, defence.Defender, defence.Attacker, defender, ds, cfg, blocked: true);
    }

    /// <summary>Strength Gap Recoil: a block against a stronger foe bruises both of the defender's arms.</summary>
    void Recoil(CharacterCold attacker, CharacterStats st, CharacterId defender, CharacterStats ds, DesoSettings cfg)
    {
        if (!cfg.P2 || !Gates.InScope(attacker, cfg.ScopeIndex)) return;

        float gap = Gates.L(st, Strength) - Gates.L(ds, Strength);
        if (gap <= cfg.StrengthGap || (cfg.P3 && Gates.L(ds, MeleeDefence) > cfg.DefenceMastery)) return;

        float damage = cfg.RecoilPer10 * (gap - cfg.StrengthGap) / 10;
        Queue(defender, Target.Arms, damage);
    }

    /// <summary>Saber Guard: 0.5 % riposte chance per sabre level above 60 (at most 30 %), 4 + 1 per 10 levels; half the time the chest, else both arms.</summary>
    void Riposte(World world, in CombatDefence defence, CharacterCold defender, CharacterStats ds, DesoSettings cfg)
    {
        float level = Gates.L(ds, Sabres);
        if (!cfg.P8 || !Gates.InScope(defender, cfg.ScopeIndex) || defender.Fighter is not { Unarmed: false, WeaponSkill: Sabres }) return;
        if (level <= cfg.MasteryThreshold) return;

        ulong seed = Rng.Hash(world.Seed, Rng.Key(defence.Attacker), purpose, ((ulong)world.Tick << 16) ^ ((ulong)(uint)defence.Sequence << 4) ^ 7);
        if (Rng.Float(seed) >= Math.Min(0.3f, 0.005f * (level - 60))) return;

        float damage = 4 + (level - 60) / 10;
        Queue(defence.Attacker, Rng.Float(Rng.Mix(seed)) < 0.5f ? Target.Chest : Target.Arms, damage);
    }

    public void HitResolved(World world, in CombatHit hit)
    {
        var table = world.Characters;
        if (hit.Source != CombatHitSource.Melee || !table.TryResolveNext(hit.Victim, out int v) || table.Cold(v) is not { Stats: { } vs } victim)
            return;

        var cfg = _cfg;
        Counter(world, hit.Victim, hit.Attacker, victim, vs, cfg, blocked: false);
        if (!table.TryResolveNext(hit.Attacker, out int a) || table.Cold(a) is not { Stats: { } st } attacker) return;

        Polearm(world, hit.Victim, attacker, st, cfg);
        if (cfg.AscMartTrue && hit.Part >= 0 && hit.Part < victim.Medical!.Parts.Count && Gates.Reached(attacker, cfg, Gates.Realm.Universal))
            Queue(hit.Victim, Target.Part, 0.1f * victim.Medical.Parts[hit.Part].MaxHp, hit.Part);
        if (cfg.AscCybStun && victim.Race?.IsRobot == true && Gates.Reached(attacker, cfg, Gates.Realm.Cyber)) Queue(hit.Victim, Target.Ko, 15);
    }

    /// <summary>Polearm Supremacy: a hit slows the victim's attacks by 4 / 6.75 / 15 % (at most 60 %) for 5 s, not stacking.</summary>
    void Polearm(World world, CharacterId victim, CharacterCold attacker, CharacterStats st, DesoSettings cfg)
    {
        float level = Gates.L(st, Polearms);
        if (!cfg.P12 || !Gates.InScope(attacker, cfg.ScopeIndex) || attacker.Fighter is not { Unarmed: false, WeaponSkill: Polearms }) return;
        if (level <= cfg.MasteryThreshold) return;

        _slows[victim] = new Slow(Math.Min(0.6f, Mastery.Value(level, 0.04f, 0.15f)), world.Tick + Ticks(world, SlowSeconds));
        _timedDirty = true;
    }

    /// <summary>
    /// Lightning Counter: attacks from two different foes within 5 s and then a block give the defender x1.5 attack speed for 1.2 s.
    /// </summary>
    void Counter(World world, CharacterId me, CharacterId foe, CharacterCold cold, CharacterStats stats, DesoSettings cfg, bool blocked)
    {
        if (!cfg.P209 || !Gates.InScope(cold, cfg.ScopeIndex) || Gates.L(stats, Dexterity) <= 75) return;

        long since = world.Tick - Ticks(world, CounterWindow);
        if (!_attackers.TryGetValue(me, out var list)) _attackers[me] = list = [];
        list.RemoveAll(x => x.Tick < since || x.Foe == foe);
        list.Add((foe, world.Tick));
        if (!blocked || list.Count < 2) return;

        _bursts[me] = world.Tick + Ticks(world, BurstSeconds);
        _timedDirty = true;
    }

    static long Ticks(World world, float seconds) => (long)MathF.Ceiling(seconds / world.TickSeconds);
}
