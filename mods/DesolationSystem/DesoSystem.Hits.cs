using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using static Meitou.Data.Gameplay.Bodies.StatsEnumerated;

namespace Meitou.DesolationSystem;

/// <summary>
/// The damage perks as a hit filter (before armour): the attacker's Strength penetration, Ryu damage, Blade Unity, the weapon masteries, Crossbow
/// Mastery and Universal Mastery's damage; the victim's Steel Body, Hemostasis, Physical Sanctification's -10 % and a sovereign pack's half bleed.
/// Extra wounds (Shockwave, Blunt's head hit, Katana's instant blood loss) are queued and dealt in <see cref="SlowWorld"/>.
/// </summary>
sealed partial class DesoSystem : ICombatHitFilter
{
    public void HitLanding(World world, ref PendingHit hit)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(hit.Victim, out int v) || table.Cold(v) is not { Stats: { } vs } victim) return;

        var cfg = _cfg;
        var p = hit.Packet;
        if (table.TryResolveNext(hit.Attacker, out int a) && table.Cold(a) is { Stats: { } st } attacker)
        {
            p = hit.Source == CombatHitSource.Projectile
                ? Ranged(attacker, st, victim, vs, cfg, p)
                : Melee(world, hit, attacker, st, victim, vs, cfg, p);
        }

        p = Taken(victim, vs, cfg, hit.Source, p);
        hit.Packet = Sane(p, hit.Packet);
    }

    DamagePacket Melee(World world, in PendingHit hit, CharacterCold attacker, CharacterStats st, CharacterCold victim, CharacterStats vs,
        DesoSettings cfg, DamagePacket p)
    {
        if (Gates.InScope(attacker, cfg.ScopeIndex))
        {
            float str = Gates.L(st, Strength);
            if (cfg.P1 && str > 0) p = p with { ArmourPenetration = p.ArmourPenetration + Math.Min(1, str / 1000) };
            p = RyuDamage(hit.Attacker, st, cfg, p);
            if (cfg.P213 && attacker.Fighter is { Unarmed: false } && Gates.L(st, SmithingWeapon) is var ws && ws >= 60)
                p = p with { Cut = p.Cut * (1 + 0.005f * (ws - 60)), Blunt = p.Blunt * (1 + 0.005f * (ws - 60)) };
            p = Weapon(world, hit, attacker, st, victim, cfg, p);
            if (cfg.P6 && str >= cfg.ShockwaveStrength) Shock(hit.Victim, vs, str, cfg);
        }

        if (cfg.AscMartDmg && Gates.Reached(attacker, cfg, Gates.Realm.Universal))
            p = p with { Cut = p.Cut * 1.5f, Blunt = p.Blunt * 1.5f, Pierce = p.Pierce * 1.5f };
        return p;
    }

    DamagePacket RyuDamage(CharacterId attacker, CharacterStats st, DesoSettings cfg, DamagePacket p)
    {
        if (!cfg.Ryu || !_view.Styles.TryGetValue(attacker, out int style)) return p;

        var (_, damage, pierce) = Ryu(style, Mastery.RyuFactor(Gates.L(st, MeleeAttack)));
        return p with
        {
            Cut = p.Cut * damage, Blunt = p.Blunt * damage, Pierce = p.Pierce * damage, ArmourPenetration = p.ArmourPenetration + pierce,
        };
    }

    /// <summary>The held weapon's mastery (skill above the threshold, that weapon in hand).</summary>
    DamagePacket Weapon(World world, in PendingHit hit, CharacterCold attacker, CharacterStats st, CharacterCold victim, DesoSettings cfg,
        DamagePacket p)
    {
        if (attacker.Fighter is not { Unarmed: false } fighter) return p;

        var skill = fighter.WeaponSkill;
        float level = Gates.L(st, skill);
        if (level <= cfg.MasteryThreshold) return p;

        bool robot = victim.Race?.IsRobot == true;
        switch (skill)
        {
            case Katanas when cfg.P7 && !robot:
                float bleed = Math.Min(2, p.BleedMultiplier + Mastery.Value(level, 0.1f, 0.5f));
                Queue(hit.Victim, Target.Blood, Mastery.Value(level, 1, 4));
                return p with { Cut = p.Cut * (1 + Mastery.Value(level, 0.04f, 0.16f)), BleedMultiplier = Math.Max(p.BleedMultiplier, bleed) };
            case Hackers when cfg.P9:
                float vs = robot ? 1 + Mastery.Value(level, 0.05f, 0.20f) : 1;
                return p with { Cut = p.Cut * vs, Blunt = p.Blunt * vs, ArmourPenetration = p.ArmourPenetration + Mastery.Value(level, 0.03f, 0.15f) };
            case Blunt when cfg.P10:
                float chance = Math.Min(1, Mastery.Value(level, 0.04f, 0.14f));
                if (Roll(world, hit, 1) < chance) Queue(hit.Victim, Target.Head, Mastery.Value(level, 12, 30));
                return p with { ArmourPenetration = p.ArmourPenetration + Mastery.Value(level, 0.05f, 0.25f) };
            default:
                return p;
        }
    }

    /// <summary>Crossbow Mastery: the shooter's factor; a humanoid target takes 6 % less per 10 melee defence, at most 60 % less.</summary>
    DamagePacket Ranged(CharacterCold attacker, CharacterStats st, CharacterCold victim, CharacterStats vs, DesoSettings cfg, DamagePacket p)
    {
        if (!cfg.P207 || !Gates.InScope(attacker, cfg.ScopeIndex)) return p;

        float factor = Perks.Crossbow(Gates.L(st, Crossbows));
        if (!Gates.IsAnimal(victim)) factor *= 1 - Math.Min(0.6f, 0.006f * Gates.L(vs, MeleeDefence));
        return p with { Cut = p.Cut * factor, Blunt = p.Blunt * factor, Pierce = p.Pierce * factor };
    }

    /// <summary>The victim's own reductions.</summary>
    DamagePacket Taken(CharacterCold victim, CharacterStats vs, DesoSettings cfg, CombatHitSource source, DamagePacket p)
    {
        if (Gates.InScope(victim, cfg.ScopeIndex))
        {
            if (cfg.P214 && Gates.L(vs, SmithingArmour) is var smith && smith >= 60)
            {
                float keep = 1 - Math.Min(0.95f, 0.005f * (smith - 60));
                float pierce = source == CombatHitSource.Projectile ? Math.Max(0, p.Pierce - 0.75f * (smith - 60)) : p.Pierce;
                p = p with { Cut = p.Cut * keep, Blunt = p.Blunt * keep, Pierce = pierce };
            }

            if (cfg.P20 && Perks.Medical(vs) is var med && med >= 50)
                p = p with { BleedMultiplier = p.BleedMultiplier * (1 - Math.Min(0.95f, 0.006f * med)) };
        }

        if (cfg.AscPhysDr && Gates.Reached(victim, cfg, Gates.Realm.Physical))
            p = p with { Cut = p.Cut * 0.9f, Blunt = p.Blunt * 0.9f, Pierce = p.Pierce * 0.9f, Stun = p.Stun * 0.9f };
        if (cfg.AscWldSync && Gates.IsAnimal(victim) && _view.WildSquads.Contains(victim.SquadId))
            p = p with { BleedMultiplier = p.BleedMultiplier * 0.5f };
        return p;
    }

    /// <summary>Titan Shockwave: chest blunt damage, less the victim's Shockwave Resist.</summary>
    void Shock(CharacterId victim, CharacterStats vs, float strength, DesoSettings cfg)
    {
        float resist = cfg.P6Resist ? Perks.ShockResist(vs) : 0;
        float damage = Perks.Shockwave(strength, cfg.ShockwaveStrength) * (1 - resist);
        if (damage > 0) Queue(victim, Target.Chest, damage);
    }

    /// <summary>The filtered packet, or the incoming one when a value is not finite or negative (a fault would disable the filter).</summary>
    static DamagePacket Sane(DamagePacket p, DamagePacket incoming)
    {
        bool ok = float.IsFinite(p.Cut + p.Blunt + p.Pierce + p.Stun + p.BleedMultiplier + p.ArmourPenetration)
            && p.Cut >= 0 && p.Blunt >= 0 && p.Pierce >= 0 && p.Stun >= 0 && p.BleedMultiplier >= 0;
        return ok ? p with { ArmourPenetration = Math.Min(1, p.ArmourPenetration) } : incoming;
    }

    /// <summary>A roll of the mod's purpose for this hit.</summary>
    float Roll(World world, in PendingHit hit, ulong salt) =>
        Rng.Float(Rng.Hash(world.Seed, Rng.Key(hit.Victim), purpose, ((ulong)world.Tick << 16) ^ ((ulong)(uint)hit.Sequence << 4) ^ salt));
}
