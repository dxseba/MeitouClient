namespace Meitou.ArcaneMagic;

/// <summary>The five spells, numbered as the original's spell selector numbers them (README "Facts").</summary>
public enum Spell
{
    Missile = 0,
    Summon = 1,
    EarthSpike = 2,
    Whirlwind = 3,
    Bomb = 4,
}

/// <summary>
/// The original's numbers (README "Facts", from the decompile of <c>ArcaneMagicNative.dll</c> v0.75.1 and its constants): Arcane levels 0 to 100,
/// mana, costs, volleys, damage factors, the spells' reach and timing. Pure functions of their arguments.
/// </summary>
static class ArcaneRules
{
    public const float MaxLevel = 100;

    /// <summary>The Focus (CROSSBOW <c>Arcane Focus Orb</c>).</summary>
    public const string Focus = "20-ArcaneMagic.mod";

    /// <summary>The backpack that regenerates mana half as fast again (CONTAINER <c>Arcane Levigator</c>).</summary>
    public const string Levigator = "231-ArcaneMagic.mod";

    /// <summary>The summoned creature (ANIMAL_CHARACTER <c>Summonathan</c>).</summary>
    public const string Summonathan = "238-ArcaneMagic.mod";

    /// <summary>The EFFECT the original's missile cast flashes with (<c>AM_ArcaneFlash_Proto</c>, particle system <c>AM_arcane_flash</c>).</summary>
    public const string Flash = "17-ArcaneMagic.mod";

    public const float MissileCost = 25, SpikeCost = 25, WhirlwindCost = 40, BombCost = 100;

    public const float MissileXp = 5, SpikeXp = 5, WhirlwindXp = 10, BombXp = 5, SummonXp = 15;

    /// <summary>The flesh the bomb takes from the caster's healthiest part.</summary>
    public const float BombSacrifice = 20;

    /// <summary>
    /// Earth Spike: the spikes rise at the hand contact (0.3667 of the 2.0 s cast clip), strike 0.18 s later and stand until 2.18 s; each spike's
    /// share of the missile damage.
    /// </summary>
    public const float SpikeContact = 0.36666667f * 2.0f, SpikeImpact = 0.18f, SpikeLife = 2.18f, SpikeShare = 0.6f;

    /// <summary>Whirlwind: it leaves at the slap, 0.3889 of the 1.8 s cast clip.</summary>
    public const float WhirlwindSlap = 0.3888889f * 1.8f;

    /// <summary>Whirlwind: speed (units a second), damage step (seconds), reach across and up (units), each step's share of the missile damage.</summary>
    public const float WhirlwindSpeed = 135, WhirlwindStep = 0.2f, WhirlwindRadius = 30, WhirlwindHeight = 20, WhirlwindShare = 0.3f;

    /// <summary>Bomb: reach (units, 10 a metre), full damage within, damage left at the edge.</summary>
    public const float BombRadius = 80, BombFull = 25, BombEdge = 0.35f;

    /// <summary>
    /// Bomb: thrown at 0.4444 of the 2.4 s cast clip from the left hand, flying 180 units a second towards the aim point in 0.02 s steps; it bursts
    /// on arrival, at ground above it, or after 12 s in the air; the burst is shown 0.8 s.
    /// </summary>
    public const float BombLaunch = 0.44444445f * 2.4f, BombSpeed = 180, BombStep = 0.02f, BombMaxFlight = 12, BurstShow = 0.8f;

    /// <summary>Summon: seconds a creature stays.</summary>
    public const float SummonLife = 150;

    /// <summary>The named Magi (CHARACTER string ids), each one's spell and the Arcane level it starts with.</summary>
    public static readonly IReadOnlyDictionary<string, (Spell Spell, float Level)> Magi = new Dictionary<string, (Spell, float)>
    {
        ["46-ArcaneMagic.mod"] = (Spell.Summon, 75),
        ["151-ArcaneMagic.mod"] = (Spell.Whirlwind, 78),
        ["81-ArcaneMagic.mod"] = (Spell.EarthSpike, 86),
        ["99-ArcaneMagic.mod"] = (Spell.Bomb, 90),
        ["197-ArcaneMagic.mod"] = (Spell.Missile, 100),
    };

    public static float Clamp(float level) => float.IsNaN(level) ? 0 : Math.Clamp(level, 0, MaxLevel);

    /// <summary>Mana capacity: 100 plus 2 a level (100 to 300).</summary>
    public static float ManaMax(float level) => Clamp(level) * 2 + 100;

    /// <summary>Mana a second: 3 plus 0.04 a level, half as much again with the Levigator worn.</summary>
    public static float Regen(float level, bool levigator) => (Clamp(level) * 0.04f + 3) * (levigator ? 1.5f : 1);

    /// <summary>Arcane XP a level needs: 20 plus 2 a level.</summary>
    public static float XpFor(float level) => MathF.Max(level, 0) * 2 + 20;

    /// <summary>Missiles a cast (and Earth Spikes, and the whirlwind's length): 1 under 25, 2 under 50, 3 under 75, 4 under 100, 5 at 100.</summary>
    public static int Volley(float level) => level switch
    {
        >= 100 => 5,
        >= 75 => 4,
        >= 50 => 3,
        >= 25 => 2,
        _ => 1,
    };

    /// <summary>The Focus damage factor: 1 plus 0.04 a level (1 to 5).</summary>
    public static float DamageFactor(float level) => Clamp(level) * 0.04f + 1;

    /// <summary>The Focus grade factor of the spells (its item level): 0.9, 0.95, 1, 1.05, 1.1, 1.15.</summary>
    public static float Grade(float itemLevel) => itemLevel switch
    {
        >= 95 => 1.15f,
        >= 80 => 1.1f,
        >= 60 => 1.05f,
        >= 40 => 1,
        >= 20 => 0.95f,
        _ => 0.9f,
    };

    /// <summary>Whirlwind seconds: 3, plus half a second a volley step.</summary>
    public static float WhirlwindLife(float level) => 3 + (Volley(level) - 1) * 0.5f;

    /// <summary>Summoned creatures at once: 1 under 50, 2 under 100, 3 at 100.</summary>
    public static int SummonCap(float level) => level >= 100 ? 3 : level >= 50 ? 2 : 1;

    /// <summary>The bomb's cut damage at the centre: 35 plus 1.2 a level, times the Focus grade.</summary>
    public static float BombDamage(float level, float grade) => (Clamp(level) * 1.2f + 35) * grade;

    /// <summary>The bomb's falloff with the distance: full to 25, then down to 0.35 at 80.</summary>
    public static float BombFalloff(float distance) =>
        distance <= BombFull ? 1 : 1 - MathF.Min((distance - BombFull) / (BombRadius - BombFull), 1) * (1 - BombEdge);

    /// <summary>The cost a spell's release needs (the Summon: the whole capacity).</summary>
    public static float Cost(Spell spell, float level) => spell switch
    {
        Spell.Summon => ManaMax(level),
        Spell.EarthSpike => SpikeCost,
        Spell.Whirlwind => WhirlwindCost,
        Spell.Bomb => BombCost,
        _ => MissileCost,
    };

    /// <summary>The selector's caption (the original's texts).</summary>
    public static string Caption(Spell spell) => spell switch
    {
        Spell.Summon => "SUMMON",
        Spell.EarthSpike => "EARTH SPIKE",
        Spell.Whirlwind => "WHIRLWIND",
        Spell.Bomb => "ARCANE BOMB",
        _ => "ARCANE MISSILE",
    };

    /// <summary>The unlock notice of a Magus's defeat (the original's texts).</summary>
    public static string Unlocked(Spell spell) => spell switch
    {
        Spell.Summon => "VANE DEFEATED - SUMMON UNLOCKED",
        Spell.EarthSpike => "TOR DEFEATED - EARTH SPIKE UNLOCKED",
        Spell.Whirlwind => "CHRISTOPHER DEFEATED - WHIRLWIND UNLOCKED",
        Spell.Bomb => "MALAKOR DEFEATED - ARCANE BOMB UNLOCKED",
        _ => "",
    };
}
