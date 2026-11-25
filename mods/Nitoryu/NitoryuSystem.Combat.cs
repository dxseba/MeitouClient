using System.Numerics;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;

namespace Meitou.Nitoryu;

/// <summary>
/// The combat hooks of the registration surface: the blow's weapon (hit filter), the second blade's parry (defence filter), the dual speed factor
/// (speed filter), the Dual Wielding experience (experience filter), and the block, flow and riposte bookkeeping (defence and hit observers).
/// Each writes only the state of the character whose step it runs in.
/// </summary>
sealed partial class NitoryuSystem : ICombatHitFilter, ICombatDefenceFilter, ICombatSpeedFilter, IExperienceFilter, ICombatDefenceObserver,
    ICombatHitObserver
{
    CombatConstants? _constants;

    CombatConstants Constants => _constants ??= CombatConstants.FromDatabase(session.Data);

    float Ms(World world, long ticks) => ticks * world.TickSeconds * 1000;

    #region Blows

    /// <summary>
    /// A dual fighter's blow: the impacts of the pair's moves with <c>limb</c> 0 (the left arm) take the second weapon's own packet (its damage,
    /// bleeding, armour penetration, race multipliers) times the off-hand damage curve by Path and the WEAPON's <c>nitoryu off hand damage</c>;
    /// every blow's cut counts Dexterity +12 % (+12 % more in flow).
    /// </summary>
    public void HitLanding(World world, ref PendingHit hit)
    {
        if (hit.Source != CombatHitSource.Melee) return;
        if (_state.IsLeftHanded(hit.Attacker.Slot, hit.Attacker))
        {
            float k = _state[hit.Attacker.Slot].LeftDamage;
            hit.Packet = hit.Packet with { Cut = hit.Packet.Cut * k, Blunt = hit.Packet.Blunt * k, Pierce = hit.Packet.Pierce * k };
            return;
        }

        if (!_state.IsDual(hit.Attacker.Slot, hit.Attacker)) return;

        var table = world.Characters;
        if (table.Cold(hit.Attacker.Slot) is not { Fighter.Weapon: { } main } ac || table.Cold(hit.Victim.Slot) is not { Race: not null } dc) return;

        ref var s = ref _state[hit.Attacker.Slot];
        var move = s.PickSeq == hit.Sequence ? Moves.Of(s.PickTech) : null;
        bool off = move is not null && s.Off is not null && move.Tech.LimbOf(hit.Blow) == 0;
        float dex = DexterityBonus(world, ref s);
        if (off)
        {
            var packet = Packet(ac, dc, s.Off!, s.Off!.Skill, 1 + dex);
            float k = move!.Tech.PowerOf(hit.Blow) * 0.01f * session.Config.OffHandDamage.At(s.Path) * session.Pairs.OffHandFactor(s.Off.Data);
            hit.Packet = packet with { Cut = packet.Cut * k, Blunt = packet.Blunt * k, Pierce = packet.Pierce * k, Stun = packet.Stun * k };
            s.OffBlowXp = s.Off.Skill != main.Skill;
            (s.OffBlowSeq, s.OffBlowBlow) = (hit.Sequence, hit.Blow);
            return;
        }

        float plain = Packet(ac, dc, main, main.Skill, 1).Cut;
        if (plain > 0) hit.Packet = hit.Packet with { Cut = hit.Packet.Cut * Packet(ac, dc, main, main.Skill, 1 + dex).Cut / plain };
    }

    float DexterityBonus(World world, ref DualFighter s) =>
        session.Config.DexterityBonus + (InFlow(world, ref s) ? session.Config.FlowDexterityBonus : 0);

    bool InFlow(World world, ref DualFighter s) => session.Settings.Flow && session.Config.Flow && world.Tick < s.FlowUntil;

    /// <summary>The game's melee packet of <paramref name="weapon"/> against the victim, Dexterity times <paramref name="dexterity"/>.</summary>
    DamagePacket Packet(CharacterCold ac, CharacterCold dc, WeaponInstance weapon, StatsEnumerated skill, float dexterity)
    {
        var fighter = ac.Fighter!;
        var race = dc.Race!;
        var kind = race.IsRobot ? TargetKind.Robot : dc.Fighter is { IsAnimal: true } ? TargetKind.Animal : TargetKind.Human;
        float dex = CombatAccess.Eff(ac, StatsEnumerated.Dexterity) * fighter.DexterityMult * dexterity;
        return DamageFormulas.Packet(CombatAccess.Eff(ac, skill), dex, CombatAccess.Eff(ac, StatsEnumerated.Strength), weapon, kind,
            race.StringId, fighter.DamageOutput, Constants);
    }

    public void HitResolved(World world, in CombatHit hit)
    {
        Released(hit);
        if (_state.IsDual(hit.Attacker.Slot, hit.Attacker))
        {
            ref var attacker = ref _state[hit.Attacker.Slot];
            attacker.OffBlowXp = false;
            if (hit.Source == CombatHitSource.Melee) TwinLanded(world, hit);
            if (hit.Source == CombatHitSource.Melee && attacker.OffBlowSeq == hit.Sequence && attacker.OffBlowBlow == hit.Blow)
            {
                var (who, whom) = (hit.Attacker, hit.Victim);
                Addons.Raise(l => l.SecondBladeHit(world, who, whom));
            }
        }

        if (hit.Source != CombatHitSource.Melee || !_state.IsDual(hit.Victim.Slot, hit.Victim)) return;

        ref var victim = ref _state[hit.Victim.Slot];
        Attacked(ref victim, hit.Attacker, hit.Tick);
        Incoming(world, ref victim);
    }

    #endregion

    #region Defence

    /// <summary>
    /// The second blade's parry (rank 2+): a blow the game's pick does not meet may be parried by the second blade, at the off-hand guard chance by
    /// Path (x1.3 for a long + short pair), from the front and flanks (110 degrees each side; from rank 3 also from behind at half), at most once a
    /// game second; a blow of the enemy the fighter faces counts at half from rank 3 and not before, a later blow of a combo at half.
    /// </summary>
    public int Defence(World world, in DefenceChoiceAsk ask)
    {
        if (ask.Technique >= 0 || ask.Stumbling || !session.Settings.Guard || !session.Config.Guard || _combat is null) return ask.Technique;
        if (!_state.IsDual(ask.Defender.Slot, ask.Defender)) return ask.Technique;

        ref var s = ref _state[ask.Defender.Slot];
        var config = session.Config;
        if (s.Tier < config.GuardMinTier || Ms(world, ask.Tick - s.LastParry) < config.GuardCooldownMs) return ask.Technique;

        float chance = GuardChance(world, ask, ref s);
        ulong hash = Rng.Hash(world.Seed, Rng.Key(ask.Defender), session.Purpose, (ulong)ask.Sequence << 16 | (uint)ask.Blow);
        if (chance <= 0 || Rng.Float(hash) >= chance) return ask.Technique;

        int pick = Block(world, ask, Rng.Float(Rng.Mix(hash)));
        if (pick >= 0) s.LastParry = ask.Tick;
        return pick >= 0 ? pick : ask.Technique;
    }

    float GuardChance(World world, in DefenceChoiceAsk ask, ref DualFighter s)
    {
        var config = session.Config;
        float chance = config.OffHandGuard.At(s.Path);
        if (config.PairBonuses && s.Traits.LongShort) chance *= config.PairLongShortParry;

        var prev = world.Characters.Previous;
        var me = prev[ask.Defender.Slot];
        var them = prev[ask.Attacker.Slot];
        var to = new Vector2(them.Position.X - me.Position.X, them.Position.Z - me.Position.Z);
        var facing = new Vector2(MathF.Sin(me.Yaw), MathF.Cos(me.Yaw));
        float angle = to.LengthSquared() > 1e-6f ? MathF.Acos(Math.Clamp(Vector2.Dot(Vector2.Normalize(to), facing), -1, 1)) * 180 / MathF.PI : 0;
        if (angle > config.GuardArcDegrees)
        {
            if (s.Tier < config.BackGuardMinTier) return 0;
            chance *= config.BackGuardShare;
        }

        if (_combat!.StateOf(ask.Defender).TargetSlot == ask.Attacker.Slot)
        {
            if (s.Tier < config.GuardMainMinTier) return 0;
            chance *= config.GuardShareMain;
        }
        else if (ask.Blow > 1)
        {
            chance *= config.GuardShareCombo;
        }

        return chance;
    }

    /// <summary>A block of the defender's weapon class meeting the blow's direction (any of the class's blocks when none does), drawn by chance.</summary>
    int Block(World world, in DefenceChoiceAsk ask, float roll)
    {
        if (world.Characters.Cold(ask.Defender.Slot) is not { Fighter: { CanBlock: true } fighter, Stats: { } stats }) return -1;

        var techniques = _combat!.Techniques;
        var kind = WeaponCategories.TechniqueKind(fighter.TechniqueCategory);
        float skill = stats[fighter.WeaponSkill];
        int direction = ask.Direction;
        int pick = Draw(techniques, t => Blocks(t, kind, skill) && t.DirectionOf(1) == direction, roll);
        return pick >= 0 ? pick : Draw(techniques, t => Blocks(t, kind, skill), roll);
    }

    static bool Blocks(CombatTechnique t, WeaponKinds kind, float skill) =>
        t is { IsBlock: true, IsDodge: false, IsProne: false, Disabled: false } && (t.Kinds & kind) != 0 && skill >= t.MinSkill && skill <= t.MaxSkill;

    static int Draw(IReadOnlyList<CombatTechnique> all, Func<CombatTechnique, bool> fits, float roll)
    {
        float total = 0;
        for (int i = 0; i < all.Count; i++)
        {
            if (fits(all[i])) total += MathF.Max(all[i].Chance, 0);
        }

        if (total <= 0) return -1;

        float target = roll * total;
        int last = -1;
        for (int i = 0; i < all.Count; i++)
        {
            if (!fits(all[i]) || all[i].Chance <= 0) continue;

            last = i;
            target -= all[i].Chance;
            if (target < 0) return i;
        }

        return last;
    }

    public void Defended(World world, in CombatDefence defence)
    {
        Pressed(world, defence);
        if (!_state.IsDual(defence.Defender.Slot, defence.Defender)) return;

        ref var s = ref _state[defence.Defender.Slot];
        Attacked(ref s, defence.Attacker, defence.Tick);
        if (defence.Kind == CombatDefenceKind.Blocked && s.LastParry != s.ParryReported && Ms(world, defence.Tick - s.LastParry) <= 1500)
        {
            s.ParryReported = s.LastParry;
            var (who, whom) = (defence.Defender, defence.Attacker);
            Addons.Raise(l => l.Parried(world, who, whom));
        }
        if (defence.Kind == CombatDefenceKind.Blocked) s.LastBlock = defence.Tick;
        Incoming(world, ref s);
    }

    /// <summary>
    /// Flow (rank 4+): the third attack aimed at the fighter within 5 s (blocked, dodged or landed) starts 4 s of flow, then 5 s before attacks
    /// count again.
    /// </summary>
    void Incoming(World world, ref DualFighter s)
    {
        long now = world.Tick;
        (s.Incoming0, s.Incoming1, s.Incoming2) = (s.Incoming1, s.Incoming2, now);
        var config = session.Config;
        if (s.Tier < config.FlowMinTier || !config.Flow || now < s.FlowReadyAt) return;

        long oldest = Math.Clamp(config.FlowAttacks, 1, 3) switch { 1 => s.Incoming2, 2 => s.Incoming1, _ => s.Incoming0 };
        if (Ms(world, now - oldest) > config.FlowWindowMs) return;

        s.FlowUntil = now + (long)(config.FlowDurationMs / 1000f / world.TickSeconds);
        s.FlowReadyAt = s.FlowUntil + (long)(config.FlowCooldownMs / 1000f / world.TickSeconds);
    }

    #endregion

    #region Speed and experience

    /// <summary>
    /// The dual speed factor on attack and block rates (README "Attack speed"): x0.85 at Path 0 up to x1 at Path 30, times 1 + 0.3 x the shares of
    /// Dexterity and Path above 30 (both at 100 give x1.3), +0.04 in flow; the attack cap is 1.5 (1.8 in flow), blocks 4.
    /// </summary>
    public float Rate(World world, in CombatSpeedAsk ask)
    {
        if (ask.Kind is not (CombatSpeedKind.Attack or CombatSpeedKind.Block)) return ask.Rate;
        if (_state.IsLeftHanded(ask.Character.Slot, ask.Character)) return ask.Rate * _state[ask.Character.Slot].LeftSpeed;
        if (!_state.IsDual(ask.Character.Slot, ask.Character)) return ask.Rate;
        if (world.Characters.Cold(ask.Character.Slot) is not { Stats: not null, Medical: not null } cold) return ask.Rate;

        ref var s = ref _state[ask.Character.Slot];
        var config = session.Config;
        float factor = SpeedFactor(ref s, CombatAccess.Eff(cold, StatsEnumerated.Dexterity));
        bool flow = InFlow(world, ref s);
        if (flow) factor += config.FlowSpeed;

        float cap = ask.Kind == CombatSpeedKind.Attack ? config.AttackSpeedCap + (flow ? config.FlowCapBonus : 0) : config.BlockSpeedCap;
        float rate = ask.Rate * factor;
        return rate > ask.Rate ? MathF.Min(rate, MathF.Max(cap, ask.Rate)) : rate;
    }

    float SpeedFactor(ref DualFighter s, float dexterity)
    {
        var config = session.Config;
        float learner = s.Path >= config.SpeedFullPath || config.SpeedFullPath <= 0
            ? 1
            : config.SpeedAtZero + (1 - config.SpeedAtZero) * s.Path / config.SpeedFullPath;
        float span = 100 - config.SpeedBonusFrom;
        float d = span > 0 ? Math.Clamp((dexterity - config.SpeedBonusFrom) / span, 0, 1) : 1;
        float p = span > 0 ? Math.Clamp((s.Path - config.SpeedBonusFrom) / span, 0, 1) : 1;
        return learner * (1 + config.SpeedBonus * d * p);
    }

    /// <summary>
    /// Dual Wielding learns half the weapon experience of a dual fighter's combat (<c>nitoryu ambidexterity xp share</c> x 0.5); the experience of
    /// a second-blade blow goes to the second weapon's skill when that differs from the main one's.
    /// </summary>
    public float Gain(World world, in ExperienceGain gain)
    {
        if (gain.Source != ExperienceSource.Combat || !_state.IsActive(gain.Character.Slot, gain.Character)) return gain.Amount;
        if (world.Characters.Cold(gain.Character.Slot) is not { Fighter.Weapon: { } main }) return gain.Amount;

        ref var s = ref _state[gain.Character.Slot];
        if (gain.Skill != main.Skill && gain.Skill != s.Off?.Skill) return gain.Amount;
        if (s.LeftHand)
        {
            s.PendingXp += gain.Amount * 0.5f * session.Config.XpShare;
            return gain.Amount;
        }

        s.PendingXp += gain.Amount * 0.5f * session.Config.XpShare;
        if (!s.OffBlowXp || gain.Skill != main.Skill || s.Off is null) return gain.Amount;

        s.PendingOffXp += gain.Amount;
        return 0;
    }

    #endregion
}
