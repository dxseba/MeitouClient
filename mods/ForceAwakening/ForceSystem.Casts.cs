using System.Numerics;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Movement;

namespace Meitou.ForceAwakening;

/// <summary>The casts: checks, costs, the 21 skill kinds' effects and their sounds and particles.</summary>
sealed partial class ForceSystem
{
    /// <summary>Kinds cast on the caster alone or round it (no target to click).</summary>
    public static readonly HashSet<string> SelfKinds =
        ["deflect", "force_heal", "battle_meditation", "force_wave", "dark_rage", "hunt"];

    /// <summary>Kinds aimed at a ground point (a character clicked stands for its place).</summary>
    public static readonly HashSet<string> PointKinds = ["thermal_detonator", "emp", "force_push", "flamethrower"];

    /// <summary>Port reaches (world units, 10 = 1 m; <b>Unknown</b> in the original, engine choices).</summary>
    const float PushReach = 150, WaveRadius = 100, LeapLanding = 40, HealRadius = 100, AuraRadius = 150, ChainReach = 80, FlameReach = 120,
        BlastRadius = 50, ThrowSpeed = 140;

    /// <summary>Why <paramref name="spell"/> cannot be cast now (null: it can).</summary>
    string? Refusal(World world, CharacterId id, CharacterCold cold, Adept a, Spell spell, bool checkTarget)
    {
        if (cold.Medical is not { Incapacitated: false }) return "";

        string name = ForceTexts.SpellName(spell);
        string stat = a.Path == ForcePath.Hunter ? "Crossbows" : ForceTexts.T("STAT_FORCE");
        if (Level(cold, a.Path) < spell.Level) return ForceTexts.T("ST_NEEDS", name, $"{spell.Level:0}", stat);
        if (Cooldown(world, a, spell) > 0) return ForceTexts.T("ST_COOLDOWN", name);
        if (a.Path == ForcePath.Hunter && cold.Fighter?.Crossbow is null) return ForceTexts.T("ST_NEED_XBOW");

        string unit = a.Path switch { ForcePath.Sith => "FURY", ForcePath.Hunter => "FOCUS", _ => "MANA" };
        if (spell.Full && a.Pool < PoolMax(cold, a) - 0.01f) return ForceTexts.T("ST_FULL_" + unit);
        if (!spell.Full && a.Pool < Cost(cold, a, spell)) return ForceTexts.T("ST_LOW_" + unit);

        return checkTarget && id.IsNone ? "" : null;
    }

    float Cost(CharacterCold cold, Adept a, Spell s) =>
        s.Full ? PoolMax(cold, a) : MathF.Max(0, s.Cost * (1 - _config.CostCutPerLevel * Level(cold, a.Path)));

    /// <summary>The spell's power: its cfg power, times (1 + level x <c>power_per_arcane</c>), times the open passives' percent.</summary>
    float Power(CharacterCold cold, Adept a, Spell s) =>
        s.Power * (1 + _config.PowerPerLevel * Level(cold, a.Path)) * (1 + PassivePercent(cold, a, s.Kind) / 100);

    /// <summary>The percent passives (<c>PASSIVE_&lt;class&gt;_&lt;tree&gt;_&lt;n&gt;</c> texts) that raise <paramref name="kind"/>.</summary>
    float PassivePercent(CharacterCold cold, Adept a, string kind) => (a.Path, kind) switch
    {
        (ForcePath.Jedi, "force_leap") => Passive(cold, a, 1, 1),
        (ForcePath.Jedi, "force_push") => Passive(cold, a, 1, 3),
        (ForcePath.Jedi, "force_heal") => Passive(cold, a, 2, 3),
        (ForcePath.Sith, "force_wave") => Passive(cold, a, 1, 1),
        (ForcePath.Sith, "force_leap") => Passive(cold, a, 1, 3),
        (ForcePath.Sith, "force_choke") => Passive(cold, a, 2, 2),
        (ForcePath.Sith, "life_drain") => Passive(cold, a, 2, 3),
        (ForcePath.Hunter, "volley") => Passive(cold, a, 1, 1),
        (ForcePath.Hunter, "aimed_shot") => Passive(cold, a, 1, 3),
        (ForcePath.Hunter, "flamethrower") => Passive(cold, a, 2, 1),
        (ForcePath.Hunter, "thermal_detonator") => Passive(cold, a, 2, 2),
        (ForcePath.Hunter, "dirty_kick") => Passive(cold, a, 2, 3),
        (ForcePath.Grey, "force_leap") => Passive(cold, a, 1, 1),
        (ForcePath.Grey, "force_push") => Passive(cold, a, 1, 2),
        (ForcePath.Grey, "force_wave") => Passive(cold, a, 1, 3),
        (ForcePath.Grey, "force_choke") => Passive(cold, a, 2, 2),
        (ForcePath.Grey, "life_drain" or "force_heal") => Passive(cold, a, 2, 3),
        _ => 0,
    };

    /// <summary>Casts <paramref name="spell"/>: checks, the effect, then the cost, cooldown, training and the release sound.</summary>
    bool Cast(World world, CharacterId caster, Adept a, Spell spell, CharacterId target, Vector3? point, bool manual)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(caster, out int slot) || table.Cold(slot) is not { } cold) return false;

        if (Refusal(world, caster, cold, a, spell, false) is { } why)
        {
            if (manual && why.Length > 0) world.Post(why);
            return false;
        }

        if (!Aim(world, slot, spell, ref target, ref point))
        {
            if (manual) world.Post(ForceTexts.T("ST_OUT_OF_RANGE"));
            return false;
        }

        float power = Power(cold, a, spell);
        _by = slot;
        bool done = Effect(world, caster, slot, cold, a, spell, power, target, point ?? table.Next[slot].Position);
        _by = -1;
        if (!done) return false;

        Move(world, caster, cold, spell);

        Spend(world, cold, a, spell);
        Sound(world, cold, spell.Kind, "release", caster);
        return true;
    }

    /// <summary>A target the kind needs, within <c>cast_range</c> (Dirty Kick: <c>rush_range</c>); a point kind takes a clicked character's place.</summary>
    bool Aim(World world, int slot, Spell spell, ref CharacterId target, ref Vector3? point)
    {
        if (SelfKinds.Contains(spell.Kind)) return true;

        var table = world.Characters;
        var from = table.Next[slot].Position;
        bool hasTarget = table.TryResolveNext(target, out int t) && t != slot && table.Cold(t) is { Medical.Dead: false };
        if (PointKinds.Contains(spell.Kind) && point is null && hasTarget) point = table.Next[t].Position;
        if (PointKinds.Contains(spell.Kind)) return point is { } p && Flat(p - from) <= _config.CastRange;

        float reach = spell.Kind == "dirty_kick" ? _config.RushRange : _config.CastRange;
        return hasTarget && Flat(table.Next[t].Position - from) <= reach;
    }

    void Spend(World world, CharacterCold cold, Adept a, Spell spell)
    {
        float cost = Cost(cold, a, spell);
        a.Pool = MathF.Max(0, a.Pool - cost);
        a.Ready[spell.Number] = world.Tick + Ticks(world, MathF.Max(spell.Cooldown, 0.1f));
        a.LastFight = world.Tick;
        if (cold.Stats is not { } stats) return;

        float xp = MathF.Max(cost, 5) * 0.01f * _config.XpMult;
        if (a.Path == ForcePath.Hunter) stats.Gain(StatsEnumerated.Crossbows, xp);
        else
        {
            mod.Force.Gain(stats, xp);
            if (a.Path is ForcePath.Jedi or ForcePath.Grey) mod.Wisdom.Gain(stats, xp * 0.5f);
        }
    }

    /// <summary>The kind's effect; false when it could not take place (nothing spent).</summary>
    bool Effect(World world, CharacterId caster, int slot, CharacterCold cold, Adept a, Spell spell, float power, CharacterId target, Vector3 point)
    {
        var table = world.Characters;
        var at = table.Next[slot].Position;
        table.TryResolveNext(target, out int t);
        switch (spell.Kind)
        {
            case "force_push": Cone(world, cold, at, point - at, PushReach, 45, (v, away) => Blow(world, v, new HitDamage(0, power, 0), 3, away)); break;
            case "force_wave": Around(world, cold, at, WaveRadius, (v, away) => Blow(world, v, new HitDamage(0, power, 0), 3, away)); break;
            case "force_leap": return Leap(world, caster, cold, target, power);
            case "command": return Command(world, cold, a, target, t, spell);
            case "deflect": Start(world, Lasting.Deflect, caster, caster, 6 + (a.Path == ForcePath.Jedi ? Passive(cold, a, 1, 2) : 0), power); break;
            case "force_heal": Allies(world, cold, at, HealRadius, v => HealSlot(world, v, power)); break;
            case "mind_trick": MindTrick(world, cold, a, target, t); break;
            case "battle_meditation": Allies(world, cold, at, AuraRadius, v => Start(world, Lasting.BattleMeditation, table.IdOf(v), caster, 20, power)); break;
            case "brainwash": return Brainwash(world, caster, cold, target, t, spell);
            case "force_choke": Choke(world, caster, target, t, power); break;
            case "force_lightning": Lightning(world, cold, a, t, power); break;
            case "life_drain": Hurt(world, target, new HitDamage(power, 0, 0)); HealSlot(world, slot, power); Fx(world, "life_drain", target); break;
            case "dark_rage": Start(world, Lasting.DarkRage, caster, caster, 15 + Passive(cold, a, 1, 2), power); break;
            case "volley": for (int i = 0; i < 5; i++) Hurt(world, target, new HitDamage(0, 0, power * 0.4f)); Fx(world, "volley", target); break;
            case "aimed_shot": Hurt(world, target, new HitDamage(power * 0.3f, 0, power * 1.5f)); Fx(world, "aimed_shot", target); break;
            case "thermal_detonator": Start(world, Lasting.Detonator, CharacterId.None, caster, 1, power).Point = point; break;
            case "emp": Stun(world, cold, a, point); break;
            case "dirty_kick": Blow(world, t, new HitDamage(0, power, 0), 2.5f, table.Next[t].Position - at, ThrowSpeed * 1.2f); break;
            case "hunt": Start(world, Lasting.Hunt, caster, caster, _config.HuntDuration, power); break;
            case "flamethrower": Flame(world, caster, point - at, power); break;
            default: return false;
        }

        return true;
    }

    #region Kinds

    bool Leap(World world, CharacterId caster, CharacterCold cold, CharacterId target, float power)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(target, out int t)) return false;

        var land = table.Next[t].Position;
        if (CharacterPlacement.Place(world, [caster], land) == 0) return false;

        Around(world, cold, land, LeapLanding, (v, away) => Blow(world, v, new HitDamage(0, power, 0), 2, away, ThrowSpeed * 0.6f));
        Fx(world, "force_leap", target);
        return true;
    }

    /// <summary>A roll of the caster's The Force against the target's (the port's 50 + 2 per level of difference, 5 to 95 %).</summary>
    float Chance(CharacterCold cold, Adept a, CharacterCold target) =>
        Math.Clamp(50 + 2 * (Level(cold, a.Path) - (target.Stats is { } s ? mod.Force.Level(s) : 1)), 5, 95);

    bool Roll(World world, CharacterId target, float chance) =>
        Rng.Float(Rng.Hash(world.Seed, Rng.Key(target), purpose, (ulong)world.Tick)) * 100 < chance;

    bool Command(World world, CharacterCold cold, Adept a, CharacterId target, int t, Spell spell)
    {
        if (world.Characters.Cold(t) is not { } victim) return false;

        float chance = Chance(cold, a, victim);
        if (!Roll(world, target, chance))
        {
            world.Post(ForceTexts.T("ST_COMMAND_FAIL", victim.Name, $"{chance:0}"));
            return true;
        }

        var timed = Start(world, Lasting.Command, target, world.Characters.IdOf(t), 120, 0);
        timed.Faction = cold.Faction;
        TaskStart.Stand(world.Characters, t);
        world.Post(ForceTexts.T("ST_COMMAND_OK", victim.Name, cold.Name, $"{chance:0}"));
        Fx(world, spell.Kind, target);
        return true;
    }

    void MindTrick(World world, CharacterCold cold, Adept a, CharacterId target, int t)
    {
        float seconds = 4 + (a.Path == ForcePath.Jedi ? Passive(cold, a, 2, 2) : 0);
        Start(world, Lasting.MindTrick, target, CharacterId.None, seconds, 0);
        TaskStart.Stand(world.Characters, t);
        world.Post(ForceTexts.T("ST_MIND_TRICK", world.Characters.NameOf(target)));
        Fx(world, "mind_trick", target);
    }

    bool Brainwash(World world, CharacterId caster, CharacterCold cold, CharacterId target, int t, Spell spell)
    {
        if (!cold.IsPlayer || world.Characters.Cold(t) is not { IsPlayer: false } victim) return false;

        float chance = Chance(cold, AdeptOf(world, caster)!, victim);
        var adept = AdeptOf(world, caster)!;
        var timed = Start(world, Lasting.Brainwash, target, caster, MathF.Max(spell.CastTime, 1), chance);
        timed.Faction = adept.Faction > 0 && adept.Faction <= _config.BrainwashFactions.Count
            ? population.FactionIndex(_config.BrainwashFactions[adept.Faction - 1])
            : -1;
        world.Post(ForceTexts.T("ST_BRAINWASH_START", cold.Name, victim.Name, $"{chance:0}"));
        return true;
    }

    void Choke(World world, CharacterId caster, CharacterId target, int t, float power)
    {
        Blow(world, t, default, 3, Vector3.Zero, 0);
        if (world.Characters.Cold(t) is { } victim) victim.Launch = new Vector3(0, 35, 0);
        Start(world, Lasting.Choke, target, caster, 3, power);
        Fx(world, "force_choke", target);
    }

    /// <summary>Three bolts (more with the Sith passive): the target, then the nearest enemy not yet struck within reach of the last.</summary>
    void Lightning(World world, CharacterCold cold, Adept a, int t, float power)
    {
        var table = world.Characters;
        var struck = new HashSet<int>();
        int bolts = 3 + (int)(a.Path == ForcePath.Sith ? Passive(cold, a, 2, 1) : 0);
        for (int next = t; next >= 0 && struck.Count < bolts; next = Nearest(world, cold, table.Next[next].Position, ChainReach, struck))
        {
            struck.Add(next);
            var id = table.IdOf(next);
            Hurt(world, id, new HitDamage(power * 0.5f, power * 0.5f, 0, power * 0.5f));
            Fx(world, "force_lightning", id);
            Sound(world, cold, "force_lightning", "impact", id);
        }
    }

    void Stun(World world, CharacterCold cold, Adept a, Vector3 point)
    {
        float radius = _config.EmpRadius * (1 + Passive(cold, a, 1, 2) / 100);
        Around(world, cold, point, radius, (v, _) => Blow(world, v, default, 3, Vector3.Zero, 0));
        Fx(world, "emp", CharacterId.None, point);
    }

    void Flame(World world, CharacterId caster, Vector3 aim, float power)
    {
        var timed = Start(world, Lasting.Flame, CharacterId.None, caster, 1.5f, power);
        timed.Aim = aim;
    }

    #endregion

    #region Timed

    Timed Start(World world, Lasting kind, CharacterId who, CharacterId by, float seconds, float power)
    {
        _timed.RemoveAll(t => t.Kind == kind && t.Who == who && !who.IsNone);
        var timed = new Timed { Kind = kind, Who = who, By = by, End = world.Tick + Ticks(world, seconds), Power = power, NextStep = world.Tick };
        _timed.Add(timed);
        if (Shown(kind) is { } effect && !who.IsNone) effect.Apply(world, who, double.PositiveInfinity, Math.Clamp((int)MathF.Round(power), 1, 100));
        return timed;
    }

    Meitou.Simulation.Bodies.StatusEffect? Shown(Lasting kind) => kind switch
    {
        Lasting.Deflect => mod.Effects.Deflect,
        Lasting.DarkRage => mod.Effects.DarkRage,
        Lasting.BattleMeditation => mod.Effects.Meditation,
        Lasting.Hunt => mod.Effects.Hunt,
        Lasting.Command or Lasting.MindTrick => mod.Effects.Calm,
        Lasting.Choke => mod.Effects.Choke,
        _ => null,
    };

    bool Has(Lasting kind, CharacterId who) => Find(kind, who) is not null;

    Timed? Find(Lasting kind, CharacterId who) => who.IsNone ? null : _timed.Find(t => t.Kind == kind && t.Who == who);

    void StepTimed(World world)
    {
        foreach (var t in _timed.ToArray())
        {
            _by = world.Characters.TryResolveNext(t.By, out int by) ? by : -1;
            if (t.NextStep <= world.Tick) Step(world, t);
            if (world.Tick < t.End && world.Characters.IsAlive(t.Who.IsNone ? t.By : t.Who)) continue;

            if (world.Tick >= t.End) Finish(world, t);
            _by = -1;
            _timed.Remove(t);
            if (Shown(t.Kind) is { } effect && !t.Who.IsNone && !_timed.Exists(o => o.Who == t.Who && Shown(o.Kind) == effect)) effect.Remove(world, t.Who);
        }
    }

    /// <summary>The per-second steps: the choke's damage, the flame's bursts, Dark Rage keeping its man up, the brainwash's reach.</summary>
    void Step(World world, Timed t)
    {
        var table = world.Characters;
        switch (t.Kind)
        {
            case Lasting.Choke:
                Hurt(world, t.Who, new HitDamage(0, t.Power, 0));
                t.NextStep = world.Tick + Ticks(world, 1);
                break;
            case Lasting.Flame when table.TryResolveNext(t.By, out int by) && table.Cold(by) is { } cold:
                var from = table.Next[by].Position;
                Cone(world, cold, from, t.Aim, FlameReach, 30, (v, _) => Hurt(world, table.IdOf(v), new HitDamage(t.Power * 0.5f, 0, 0)));
                Fx(world, "flamethrower", t.By);
                t.NextStep = world.Tick + Ticks(world, 0.5f);
                break;
            case Lasting.DarkRage:
                if (table.TryResolveNext(t.Who, out int s) && table.Cold(s)?.Medical is { Unconscious: true, Dead: false } m) m.WakeUp();
                t.NextStep = world.Tick + 1;
                break;
            case Lasting.Brainwash:
                t.NextStep = world.Tick + Ticks(world, 0.5f);
                if (!OutOfReach(world, t)) break;

                world.Post(ForceTexts.T("ST_BRAINWASH_LOST"));
                t.End = world.Tick - 1;
                t.Power = -1;
                break;
            default:
                t.NextStep = long.MaxValue;
                break;
        }
    }

    bool OutOfReach(World world, Timed t)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(t.Who, out int a) || !table.TryResolveNext(t.By, out int b)) return true;

        return Flat(table.Next[a].Position - table.Next[b].Position) > _config.CastRange * 1.1f || table.Cold(b)?.Medical is { Incapacitated: true };
    }

    void Finish(World world, Timed t)
    {
        var table = world.Characters;
        if (t.Kind == Lasting.Detonator && table.TryResolveNext(t.By, out int by) && table.Cold(by) is { } cold && AdeptOf(world, t.By) is { } a)
        {
            float radius = BlastRadius * (1 + Passive(cold, a, 1, 2) / 100);
            Around(world, cold, t.Point, radius, (v, away) => Blow(world, v, new HitDamage(t.Power * 0.5f, t.Power, 0), 3, away));
            Fx(world, "thermal_detonator", CharacterId.None, t.Point);
            Sound(world, cold, "thermal_detonator", "release", CharacterId.None, t.Point);
        }
        else if (t.Kind == Lasting.Brainwash && t.Power >= 0) EndBrainwash(world, t);
    }

    void EndBrainwash(World world, Timed t)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(t.By, out int by) || table.Cold(by) is not { } cold || table.Cold(t.Who.Slot) is not { } victim) return;

        if (!Roll(world, t.Who, t.Power))
        {
            world.Post(ForceTexts.T("ST_BRAINWASH_FAIL", victim.Name, $"{t.Power:0}"));
            return;
        }

        bool joined = populationSystem?.JoinPlayerSquad(world, t.Who, cold.SquadId) ?? false;
        if (joined && t.Faction >= 0)
        {
            bool sent = BrainwashPatch.Send(world, population, t.Who, t.Faction);
            world.Post(sent ? ForceTexts.T("ST_BRAINWASH_OK", victim.Name, population.ShownFactionName(t.Faction)) : ForceTexts.T("ST_BRAINWASH_ERR", victim.Name));
            return;
        }

        world.Post(joined ? ForceTexts.T("ST_BRAINWASH_OK", victim.Name, ForceTexts.T("BRAINWASH_MINE")) : ForceTexts.T("ST_BRAINWASH_ERR", victim.Name));
    }

    /// <summary>The grimoire's Brainwash choice: "your faction", or a faction of <c>brainwash_faction</c> by its shown name.</summary>
    string BrainwashFactionName(Adept a)
    {
        if (a.Faction <= 0 || a.Faction > _config.BrainwashFactions.Count) return ForceTexts.T("BRAINWASH_MINE");

        int index = population.FactionIndex(_config.BrainwashFactions[a.Faction - 1]);
        return index >= 0 ? population.ShownFactionName(index) : _config.BrainwashFactions[a.Faction - 1];
    }

    /// <summary>A load or new world: timed effects are gone, so their shown status effects are dropped from everyone.</summary>
    void ClearEffects(World world)
    {
        _timed.Clear();
        var table = world.Characters;
        var mine = mod.Effects.All.Select(e => e.Id).ToArray();
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (table.Cold(slot)?.Stats is not { } stats) continue;

            foreach (string id in mine) StatusEffects.Forget(stats, id);
        }
    }

    #endregion

    #region Auto-cast

    /// <summary>
    /// The grimoire's auto-cast: a fighting adept (not knocked out) uses its ticked bar skills by itself, every <c>auto_gap_player</c> s (an NPC every
    /// <c>auto_gap_npc</c> s): a heal when it or a friend near is hurt, else the first ready skill, on its combat target.
    /// </summary>
    void AutoCasts(World world)
    {
        foreach (var (id, slot, cold, a) in Present(world).ToArray())
        {
            if (a.Auto.Count == 0 || a.NextAuto > world.Tick || !cold.InCombat || cold.Medical is { Incapacitated: true }) continue;

            a.NextAuto = world.Tick + Ticks(world, cold.IsPlayer ? _config.AutoGapPlayer : _config.AutoGapNpc);
            var (ts, tg) = combat is null ? (-1, 0) : combat.TargetOf(slot, id.Generation);
            var target = ts >= 0 ? new CharacterId(ts, tg) : CharacterId.None;
            foreach (int number in a.Bar)
            {
                if (!a.Auto.Contains(number) || _config.SpellByNumber(number) is not { } spell) continue;
                if (spell.Kind == "force_heal" && !Hurt(world, cold, slot)) continue;
                if (!SelfKinds.Contains(spell.Kind) && target.IsNone) continue;
                if (Cast(world, id, a, spell, target, null, manual: false)) break;
            }
        }
    }

    /// <summary>Whether the adept or a friend round it has a part below 60 %.</summary>
    bool Hurt(World world, CharacterCold cold, int slot)
    {
        bool hurt = false;
        Allies(world, cold, world.Characters.Next[slot].Position, HealRadius, v =>
            hurt |= world.Characters.Cold(v)?.Medical?.Parts.Any(p => p.MaxHp > 0 && p.Fraction < 0.6f) ?? false);
        return hurt;
    }

    #endregion
}
