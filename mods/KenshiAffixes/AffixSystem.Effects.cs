using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Items;

namespace Meitou.KenshiAffixes;

/// <summary>
/// The affixes' effects. Stat affixes: modifiers under one key (armour's attack and defence points capped at +25 each in all, the original's
/// equipment-bonus cap; a stat multiplier's share of the trained stat, which the game's gear multipliers then scale as the armour's own multiplier
/// would). Damage: the fighting weapon's Keen, Crushing and Serrated scale the melee packet; Deadly and Devastating of the weapon and the worn
/// armour make a critical hit (chance capped at 50%, x1.5 plus a bonus capped at +0.5 on cut, blunt, pierce and stun).
/// </summary>
sealed partial class AffixSystem
{
    const string ModifierKey = "dxseba.kenshiaffixes";

    /// <summary>The original's caps: armour attack/defence points, critical chance, critical bonus, leech.</summary>
    const float ArmourPointCap = 25, CritChanceCap = 0.5f, CritBonusCap = 0.5f, LeechCap = 0.1f, CritBase = 1.5f;

    void ApplyAll(World world)
    {
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { Stats: { } stats } cold) continue;

            stats.Modifiers.RemoveAll(ModifierKey);
            if (settings.Enabled) Apply(cold.Inventory, stats);
        }
    }

    static void Apply(Inventory? inventory, CharacterStats stats)
    {
        var amounts = new SortedDictionary<StatsEnumerated, float>();
        var armourPoints = new SortedDictionary<StatsEnumerated, float>();
        void Add(SortedDictionary<StatsEnumerated, float> to, StatsEnumerated stat, float v) => to[stat] = to.GetValueOrDefault(stat) + v;
        void Share(StatsEnumerated stat, float v) => Add(amounts, stat, stats.Stored(stat) * v);

        foreach (var piece in Wearing.WornArmour(inventory))
        {
            foreach (var affix in AffixTag.Read(piece.ModTag))
            {
                switch (affix.Kind.Field)
                {
                    case AffixField.ArmourDefence: Add(armourPoints, StatsEnumerated.MeleeDefence, affix.Value); break;
                    case AffixField.ArmourAttack: Add(armourPoints, StatsEnumerated.MeleeAttack, affix.Value); break;
                    case AffixField.DodgeBonus: Add(amounts, StatsEnumerated.Dodge, affix.Value); break;
                    case AffixField.UnarmedBonus: Add(amounts, StatsEnumerated.MartialArts, affix.Value); break;
                    case AffixField.AthleticsMult: Share(StatsEnumerated.Athletics, affix.Value); break;
                    case AffixField.StealthMult: Share(StatsEnumerated.Stealth, affix.Value); break;
                    case AffixField.DexterityMult: Share(StatsEnumerated.Dexterity, affix.Value); break;
                }
            }
        }

        foreach (var affix in Wearing.FightingWeapon(inventory) is { } weapon ? AffixTag.Read(weapon.ModTag) : [])
        {
            if (affix.Kind.Field == AffixField.ModAttack) Add(amounts, StatsEnumerated.MeleeAttack, affix.Value);
            else if (affix.Kind.Field == AffixField.ModDefence) Add(amounts, StatsEnumerated.MeleeDefence, affix.Value);
        }

        foreach (var (stat, points) in armourPoints) Add(amounts, stat, Math.Min(points, ArmourPointCap));
        foreach (var (stat, amount) in amounts) stats.Modifiers.Set(ModifierKey, stat, amount);
    }

    public void HitLanding(World world, ref PendingHit hit)
    {
        if (!settings.Enabled || hit.Source != CombatHitSource.Melee || hit.Attacker == hit.Victim) return;
        if (!world.Characters.TryResolveNext(hit.Attacker, out int a) || world.Characters.Cold(a) is not { Medical.Dead: false } attacker) return;
        if (Wearing.FightingWeapon(attacker.Inventory) is not { } weapon) return;

        var weaponAffixes = AffixTag.Read(weapon.ModTag);
        var p = Scaled(hit.Packet, weaponAffixes);

        var (chance, bonus, leech) = CombatBonuses(weaponAffixes, attacker.Inventory);
        if (chance > 0 && CritRoll(world, hit) < chance)
        {
            float m = CritBase + bonus;
            p = p with { Cut = p.Cut * m, Blunt = p.Blunt * m, Pierce = p.Pierce * m, Stun = p.Stun * m };
        }

        hit.Packet = p;
        if (leech > 0) NoteLeech(world, hit, leech);
    }

    /// <summary>Keen, Crushing and Serrated: the weapon's cut, blunt and bleed multipliers x (1 + v).</summary>
    static DamagePacket Scaled(DamagePacket p, List<Affix> affixes)
    {
        foreach (var affix in affixes)
        {
            float share = 1 + affix.Value;
            p = affix.Kind.Field switch
            {
                AffixField.CutDamage => p with { Cut = p.Cut * share },
                AffixField.BluntDamage => p with { Blunt = p.Blunt * share },
                AffixField.BleedDamage => p with { BleedMultiplier = p.BleedMultiplier * share },
                _ => p,
            };
        }

        return p;
    }

    /// <summary>The combat effects of the active weapon and the worn armour, each total capped.</summary>
    static (float Chance, float Bonus, float Leech) CombatBonuses(List<Affix> weaponAffixes, Inventory? inventory)
    {
        float chance = 0, bonus = 0, leech = 0;
        foreach (var affix in weaponAffixes.Concat(Wearing.WornArmour(inventory).SelectMany(i => AffixTag.Read(i.ModTag))))
        {
            if (affix.Kind.Field == AffixField.CritChance) chance += affix.Value;
            else if (affix.Kind.Field == AffixField.CritDamage) bonus += affix.Value;
            else if (affix.Kind.Field == AffixField.LifeSteal) leech += affix.Value;
        }

        return (Math.Min(chance, CritChanceCap), Math.Min(bonus, CritBonusCap), Math.Min(leech, LeechCap));
    }

    /// <summary>The critical roll of this blow on this victim, from the world seed (the original draws from one stream of its own).</summary>
    float CritRoll(World world, in PendingHit hit)
    {
        ulong entity = Rng.Key(hit.Attacker) ^ Rng.Key(hit.Victim) * 31 ^ (ulong)(uint)hit.Sequence << 8 ^ (ulong)(uint)hit.Blow;
        return Rng.Float(Rng.Hash(world.Seed, entity, purpose, (ulong)hit.Tick));
    }

    /// <summary>
    /// Reinforced and Cushioned raise the piece's cut or blunt resistance by their share of what it lets through ((1 - r) x v), within the game's
    /// caps (90% cut, 80% blunt).
    /// </summary>
    public ArmourPiece Piece(World world, in ArmourAsk ask)
    {
        if (!settings.Enabled || ask.Item is not { Type: FcsRecordType.Armour } item) return ask.Piece;

        var p = ask.Piece;
        foreach (var affix in AffixTag.Read(item.ModTag))
        {
            p = affix.Kind.Field switch
            {
                AffixField.CutResistance => p with { CutDefence = Math.Min(0.9f, p.CutDefence + (1 - p.CutDefence) * affix.Value) },
                AffixField.BluntResistance => p with { BluntDefence = Math.Min(0.8f, p.BluntDefence + (1 - p.BluntDefence) * affix.Value) },
                _ => p,
            };
        }

        return p;
    }
}
