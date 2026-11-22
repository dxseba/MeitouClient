using Meitou.Simulation;

namespace Meitou.ElementalEnchantment;

/// <summary>
/// A player character's elemental profile, as the original keeps it: one experience total (so one level) for the character, the main and the
/// secondary element chosen in the ENCHANTMENT window, and its aptitudes: each element 60%, the favoured one 100% and its two neighbours on the
/// ring Fire, Wind, Ice, Water, Lightning 80%; a rare character (1 in 100) has 100% in all five. A character may have no elemental ability at all
/// (the recruit chance setting).
/// </summary>
sealed class Profile
{
    /// <summary>
    /// The aptitude ring: the favoured element's two neighbours get 80%.
    /// </summary>
    static readonly Element[] Ring = [Element.Fire, Element.Wind, Element.Ice, Element.Water, Element.Lightning];

    /// <summary>
    /// Whether the character has elemental ability.
    /// </summary>
    public bool Able { get; set; } = true;

    /// <summary>
    /// The main element (none: no enchantment).
    /// </summary>
    public Element Main { get; set; }

    /// <summary>
    /// The secondary element; counts from level 50 and only when unlike the main.
    /// </summary>
    public Element Second { get; set; }

    /// <summary>
    /// The experience total, at most <see cref="Elements.MaxXp"/>.
    /// </summary>
    public int Xp { get; set; }

    /// <summary>
    /// Hundredths of an experience point carried to the next hit.
    /// </summary>
    public int Carry { get; set; }

    /// <summary>
    /// The favoured element.
    /// </summary>
    public Element Favoured { get; set; } = Element.Fire;

    /// <summary>
    /// A rare character with 100% in every element.
    /// </summary>
    public bool Rare { get; set; }

    /// <summary>
    /// The level of <see cref="Xp"/>.
    /// </summary>
    public int Level => Elements.LevelOf(Xp);

    /// <summary>
    /// The aptitude for an element in percent.
    /// </summary>
    public int Aptitude(Element element)
    {
        if (Rare || element == Favoured) return 100;

        int at = Array.IndexOf(Ring, Favoured), of = Array.IndexOf(Ring, element);
        int apart = Math.Abs(at - of);
        return apart is 1 or 4 ? 80 : 60;
    }

    /// <summary>
    /// A new character's profile rolled from its lasting id: the favoured element one of five, rare 1 in 100, able with the recruit chance.
    /// </summary>
    public static Profile Roll(ulong lasting, int recruitChance)
    {
        ulong h = Rng.Mix(lasting ^ 0x45454C454D454E54UL);
        var profile = new Profile { Favoured = Elements.All[(int)(h % 5)], Rare = Rng.Mix(h) % 100 == 0 };
        profile.Able = recruitChance >= 100 || (int)(Rng.Mix(h ^ 0xA5A5) % 100) < recruitChance;
        return profile;
    }

    /// <summary>
    /// The recalibrator: a different aptitude result (another favoured element, or rare to ordinary and back), and ability for one without it.
    /// </summary>
    public void Recalibrate(ulong seed)
    {
        for (ulong i = 0; ; i++)
        {
            ulong h = Rng.Mix(seed + i);
            bool rare = Rng.Mix(h) % 100 == 0;
            var favoured = Elements.All[(int)(h % 5)];
            if (rare == Rare && (rare || favoured == Favoured)) continue;

            (Rare, Favoured, Able) = (rare, favoured, true);
            return;
        }
    }

    /// <summary>
    /// Adds one landed hit's experience: 4 points at 100% aptitude and 1x, times the XP multiplier (in tenths, at most 20x) and the main element's
    /// aptitude, hundredths carried; capped at <see cref="Elements.MaxXp"/>.
    /// </summary>
    public bool Gain(float multiplier, bool flat)
    {
        if (!Able || Main == Element.None || Xp >= Elements.MaxXp) return false;

        int tenths = Math.Clamp((int)MathF.Round(multiplier * 10), 0, 200);
        int aptitude = flat ? 100 : Aptitude(Main);
        int hundredths = Carry + aptitude * tenths * 4 / 10;
        Xp = Math.Min(Elements.MaxXp, Xp + hundredths / 100);
        Carry = Xp >= Elements.MaxXp ? 0 : hundredths % 100;
        return hundredths >= 100;
    }
}
