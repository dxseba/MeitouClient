using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.ArcaneMagic;

/// <summary>
/// The Focus's release (README "Facts"): which spell a shot is, whether its mana gate opens, what the Missile volley does and the other spells'
/// casts. Called from the combat system's aim (read only, parallel) and shot (serial) steps.
/// </summary>
sealed partial class ArcaneSystem
{
    /// <summary>The spell each caster released last (the recovery clip's choice; set in the serial shot step, read by the animation).</summary>
    readonly Dictionary<CharacterCold, Spell> _lastCast = new(ReferenceEqualityComparer.Instance);

    /// <summary>The spell a character released last, null for none.</summary>
    public Spell? LastCast(CharacterCold cold) => _lastCast.TryGetValue(cold, out var spell) ? spell : null;

    /// <summary>
    /// The spell a Focus holder releases now: a player's selected spell; a named Magus its own spell while its gate opens, else the Missile; every
    /// other NPC the Missile.
    /// </summary>
    public Spell SpellFor(World world, int slot, CharacterCold cold, Caster? caster, int target)
    {
        if (cold.IsPlayer) return caster?.Spell ?? Spell.Missile;

        if (!ArcaneRules.Magi.TryGetValue(cold.RecordId, out var magus) || magus.Spell == Spell.Missile) return Spell.Missile;

        return Gate(world, slot, cold, caster, magus.Spell, target) ? magus.Spell : Spell.Missile;
    }

    /// <summary>
    /// The original's hard gate on a release: an unlocked spell (players), enough mana for its cost (Summon: the whole capacity), the spell's
    /// own conditions (a living target; a whirlwind's target not on the caster; a Summon below the creature cap, a player's only with a squad; the
    /// bomb's caster not down). A character not yet seen by the port's tick counts as full of mana.
    /// </summary>
    public bool Gate(World world, int slot, CharacterCold cold, Caster? caster, Spell spell, int target)
    {
        if (cold.IsPlayer && !Unlocked(spell)) return false;

        float level = Level(cold);
        float mana = caster?.Mana ?? ArcaneRules.ManaMax(level);
        if (mana + 0.0001f < ArcaneRules.Cost(spell, level)) return false;

        var prev = world.Characters.Previous;
        bool targetAlive = target >= 0 && target < prev.Length && prev[target].Alive;
        return spell switch
        {
            Spell.EarthSpike => targetAlive && target != slot,
            Spell.Whirlwind => targetAlive && target != slot && Flat(prev[target].Position - prev[slot].Position).LengthSquared() > 0.01f,
            Spell.Summon => (!cold.IsPlayer || cold.SquadId >= 0) && SummonsOf(world.Characters.IdOf(slot)) < ArcaneRules.SummonCap(level),
            Spell.Bomb => cold.Medical is { Incapacitated: false } && targetAlive,
            _ => true,
        };
    }

    /// <summary>The aim step's question (parallel, read only): may the holder in <paramref name="slot"/> release at <paramref name="target"/> now.</summary>
    public bool MayRelease(World world, int slot, CharacterCold cold, int target)
    {
        var caster = Find(world.Characters.IdOf(slot));
        return Gate(world, slot, cold, caster, SpellFor(world, slot, cold, caster, target), target);
    }

    /// <summary>
    /// A Missile shot just made <paramref name="added"/> bolts from index <paramref name="first"/>: pays 25 mana and gives 5 Arcane XP, scales its
    /// damage by the Arcane factor and adds the volley's other shards (1 to 5 missiles a cast), each turned a little off the first one's line.
    /// </summary>
    public void Volley(World world, CombatSystem combat, int slot, CharacterCold cold, int first, int added)
    {
        var caster = Ensure(world, slot, cold);
        float level = Level(cold);
        caster.Mana = MathF.Max(caster.Mana - ArcaneRules.MissileCost, 0);
        float factor = ArcaneRules.DamageFactor(level);
        for (int k = first; k < first + added; k++) BoltAccess.ScaleDamage(combat, k, factor);

        var id = world.Characters.IdOf(slot);
        int shards = ArcaneRules.Volley(level) - 1;
        for (int n = 0; n < shards && added > 0; n++)
        {
            var (_, direction) = BoltAccess.Read(combat, first);
            float spread = Uniform(world, id, 1.5f, 4f);
            float spin = Uniform(world, id, 0, 1);
            BoltAccess.AddShard(combat, first, RangedFormulas.Deviate(direction, spread, spin));
        }

        GiveXp(cold, caster, ArcaneRules.MissileXp);
        _lastCast[cold] = Spell.Missile;
        Visuals.Enqueue(new Visual(ArcaneRules.Flash, default, id, 1));
    }

    /// <summary>
    /// A release of a spell other than the Missile, in place of the Focus's bolt: pays its cost, gives its XP and starts it (README "Facts").
    /// False, nothing done, when its gate shuts now.
    /// </summary>
    public bool Cast(World world, int slot, int target, CharacterCold cold, Spell spell)
    {
        var caster = Ensure(world, slot, cold);
        if (!Gate(world, slot, cold, caster, spell, target) || cold.Fighter?.Crossbow is not { } focus) return false;

        float level = Level(cold);
        var prev = world.Characters.Previous;
        var id = world.Characters.IdOf(slot);
        var (min, max) = SpellDamage(focus, level, prev[slot].Position, prev[target].Position);
        switch (spell)
        {
            case Spell.EarthSpike:
                StartSpikes(id, cold, world.Characters.IdOf(target), min * ArcaneRules.SpikeShare, max * ArcaneRules.SpikeShare, level);
                break;
            case Spell.Whirlwind:
                StartWhirlwind(id, cold, prev[slot].Position, prev[target].Position, min, max, level);
                break;
            case Spell.Bomb:
                if (!Sacrifice(cold)) return false;

                var aim = prev[target].Position + Vector3.UnitY * RangedTuning.AimHeight;
                StartBomb(id, cold, aim, ArcaneRules.BombDamage(level, ArcaneRules.Grade(focus.Quality)), level);
                break;
            case Spell.Summon:
                _summonOrders.Add((id, cold.SquadId, ArcaneRules.SummonCap(level) - SummonsOf(id)));
                break;
            default:
                return false;
        }

        Pay(cold, caster, spell, level);
        _lastCast[cold] = spell;
        return true;
    }

    void Pay(CharacterCold cold, Caster caster, Spell spell, float level)
    {
        caster.Mana = spell == Spell.Summon ? 0 : MathF.Max(caster.Mana - ArcaneRules.Cost(spell, level), 0);
        GiveXp(cold, caster, spell switch
        {
            Spell.EarthSpike => ArcaneRules.SpikeXp,
            Spell.Whirlwind => ArcaneRules.WhirlwindXp,
            Spell.Bomb => ArcaneRules.BombXp,
            Spell.Summon => ArcaneRules.SummonXp,
            _ => ArcaneRules.MissileXp,
        });
    }

    /// <summary>
    /// A spell blow's pierce range: the Focus's pierce damage (its grade's) times the Arcane factor, rounded, at least 1; times the height bonus
    /// and the Focus grade factor.
    /// </summary>
    static (float Min, float Max) SpellDamage(CrossbowInstance focus, float level, Vector3 from, Vector3 to)
    {
        float factor = ArcaneRules.DamageFactor(level);
        float min = MathF.Max(MathF.Floor(focus.Stats.PierceMin * factor + 0.5f), 1);
        float max = MathF.Max(MathF.Floor(focus.Stats.PierceMax * factor + 0.5f), min);
        float scale = RangedFormulas.HeightFactor(from.Y + RangedTuning.BarrelHeight, to.Y) * ArcaneRules.Grade(focus.Quality);
        return (min * scale, max * scale);
    }

    /// <summary>The bomb's price in flesh: 20 from the caster's healthiest part (the original's "health" check: no part, no cast).</summary>
    static bool Sacrifice(CharacterCold cold)
    {
        if (cold.Medical is not { Parts.Count: > 0 } medical) return false;

        var part = medical.Parts.MaxBy(p => p.Flesh)!;
        part.Flesh -= ArcaneRules.BombSacrifice;
        return true;
    }

    static Vector3 Flat(Vector3 v) => v with { Y = 0 };
}
