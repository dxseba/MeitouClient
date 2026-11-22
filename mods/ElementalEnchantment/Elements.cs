using Meitou.Simulation.Combat;

namespace Meitou.ElementalEnchantment;

/// <summary>
/// The five elements and none, numbered as the original numbers them (0 none, 1 Fire, 2 Lightning, 3 Wind, 4 Water, 5 Ice).
/// </summary>
enum Element : byte { None, Fire, Lightning, Wind, Water, Ice }

/// <summary>
/// The original's numbers (decompiled DLL, <c>ElementalEnchantmentTest.dll</c>): the level curve, the weights of the main and secondary element,
/// what each element adds at a level, and the visual scale. All bonuses are lerped by the effect strength (at most
/// 5): value + (enchanted - value) x strength.
/// </summary>
static class Elements
{
    public static readonly Element[] All = [Element.Fire, Element.Lightning, Element.Wind, Element.Water, Element.Ice];

    /// <summary>
    /// The highest level, its experience (the curve below at 100) and the most a strength setting counts.
    /// </summary>
    public const int MaxLevel = 100, MaxXp = 47600;

    /// <summary>
    /// The effect strength's cap.
    /// </summary>
    public const float MaxStrength = 5;

    /// <summary>
    /// The level from which the secondary element counts, impacts show and trails show.
    /// </summary>
    public const int SecondaryLevel = 50, ImpactLevel = 40, TrailLevel = 20;

    #region Progression

    /// <summary>
    /// The experience a level needs: (4 L + 76) L, so 80 for level 1 and 47600 for 100.
    /// </summary>
    public static int XpFor(int level) => (4 * Math.Clamp(level, 0, MaxLevel) + 76) * Math.Clamp(level, 0, MaxLevel);

    /// <summary>
    /// The level of an experience total: the highest whose need it meets.
    /// </summary>
    public static int LevelOf(int xp)
    {
        int level = 0;
        while (level < MaxLevel && XpFor(level + 1) <= xp) level++;

        return level;
    }

    /// <summary>
    /// The secondary element's weight by level: none below 50, 0.25 from 50, 0.35, 0.45, 0.55, 0.65 by decade, 0.75 at 100.
    /// </summary>
    public static float SecondaryShare(int level) => level switch
    {
        >= 100 => 0.75f,
        >= 90 => 0.65f,
        >= 80 => 0.55f,
        >= 70 => 0.45f,
        >= 60 => 0.35f,
        >= 50 => 0.25f,
        _ => 0,
    };

    #endregion

    #region Bonuses

    /// <summary>
    /// Fire's blunt, Water's cut (of the blow's cut plus blunt) and Lightning's armour penetration: 0.05 + 0.001 L.
    /// </summary>
    public static float Percent(int level) => 0.05f + 0.001f * level;

    /// <summary>
    /// Ice's cut (of the blow's cut plus blunt) and Wind's speed: 0.02 + 0.0008 L.
    /// </summary>
    public static float Minor(int level) => 0.02f + 0.0008f * level;

    /// <summary>
    /// Ice's addition to the bleed multiplier: 0.1 + 0.004 L.
    /// </summary>
    public static float IceBleed(int level) => 0.1f + 0.004f * level;

    /// <summary>
    /// Lightning's attack speed: 0.01 + 0.0004 L.
    /// </summary>
    public static float LightningSpeed(int level) => 0.01f + 0.0004f * level;

    /// <summary>
    /// The blow with the enchantment's bonuses (<c>FUN_18000e960</c>'s rules): the base is cut plus blunt before them (at least 0).
    /// </summary>
    public static DamagePacket Apply(DamagePacket p, in Enchant e, float strength)
    {
        float m = Math.Clamp(strength, 0, MaxStrength);
        float body = Math.Max(0, p.Cut + p.Blunt);
        float pct = Percent(e.Level);
        float cut = body * (Minor(e.Level) * e.Weight(Element.Ice) + pct * e.Weight(Element.Water));
        return p with
        {
            Cut = p.Cut + m * cut,
            Blunt = p.Blunt + m * body * pct * e.Weight(Element.Fire),
            BleedMultiplier = p.BleedMultiplier + m * IceBleed(e.Level) * e.Weight(Element.Ice),
            ArmourPenetration = p.ArmourPenetration + m * pct * e.Weight(Element.Lightning),
        };
    }

    /// <summary>
    /// The resonance's bonuses (three different elements in the main, secondary and ward slots): for each of them, Fire 2% of the base as blunt,
    /// Lightning 0.02 armour penetration, Water 2% as cut, Ice 0.05 bleed; Wind's 2% speed is in <see cref="Speed"/>.
    /// </summary>
    public static DamagePacket Resonate(DamagePacket p, in Enchant e, float strength)
    {
        float r = Math.Clamp(strength, 0, MaxStrength);
        if (r <= 0 || !e.Resonant) return p;

        float body = Math.Max(0, p.Cut + p.Blunt);
        return p with
        {
            Blunt = p.Blunt + (e.Resonates(Element.Fire) ? body * 0.02f * r : 0),
            ArmourPenetration = p.ArmourPenetration + (e.Resonates(Element.Lightning) ? 0.02f * r : 0),
            Cut = p.Cut + (e.Resonates(Element.Water) ? body * 0.02f * r : 0),
            BleedMultiplier = p.BleedMultiplier + (e.Resonates(Element.Ice) ? 0.05f * r : 0),
        };
    }

    /// <summary>
    /// An attack or block play rate with the enchantment (<c>CharStats::calculateAttackOrBlockSpeed</c> hook): Wind adds to both, Lightning to the
    /// attack; a Wind resonance 2% more. Rates above 100 are left alone.
    /// </summary>
    public static float Speed(float rate, CombatSpeedKind kind, in Enchant e, float strength, float resonance)
    {
        if (rate <= 0 || rate > 100) return rate;

        float m = Math.Clamp(strength, 0, MaxStrength);
        float gain = Minor(e.Level) * e.Weight(Element.Wind);
        if (kind == CombatSpeedKind.Attack) gain += LightningSpeed(e.Level) * e.Weight(Element.Lightning);

        float r = Math.Clamp(resonance, 0, MaxStrength);
        float extra = e.Resonant && e.Resonates(Element.Wind) && r > 0 ? rate * 0.02f * r : 0;
        return rate * (1 + m * gain) + extra;
    }

    #endregion

    #region Visuals

    /// <summary>
    /// The size of the element's particles by level: 0.8 below 20, 0.9 below 40, 1 below 80, 1.1 below 100, 1.15 at 100 (times 1.3 for 0.18 s after
    /// a hit in the original).
    /// </summary>
    public static float VisualScale(int level) => level switch
    {
        >= 100 => 1.15f,
        >= 80 => 1.1f,
        >= 40 => 1,
        >= 20 => 0.9f,
        _ => 0.8f,
    };

    #endregion
}

/// <summary>
/// A character's enchantment as the bonuses read it: main and secondary element, the level, the ward's element (none without one).
/// </summary>
readonly record struct Enchant(Element Main, Element Second, int Level, Element Ward)
{
    /// <summary>
    /// The element's weight: 1 for the main, the level's share for a secondary unlike it, else 0.
    /// </summary>
    public float Weight(Element element) =>
        element == Element.None ? 0 : element == Main ? 1 : element == Second && Second != Main ? Elements.SecondaryShare(Level) : 0;

    /// <summary>
    /// Whether the main, secondary and ward elements are three different ones.
    /// </summary>
    public bool Resonant => Main != Element.None && Second != Element.None && Ward != Element.None && Main != Second && Main != Ward && Second != Ward;

    /// <summary>
    /// Whether the element is one of the three resonating.
    /// </summary>
    public bool Resonates(Element element) => element == Main || element == Second || element == Ward;
}
