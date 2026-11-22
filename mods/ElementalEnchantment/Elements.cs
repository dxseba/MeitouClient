using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;

namespace Meitou.ElementalEnchantment;

/// <summary>The five elements (and none), in the order the menu cycles them.</summary>
enum Element { None, Fire, Lightning, Wind, Water, Ice }

/// <summary>
/// The elements' skills and what each adds to a blow at strength s (level / 100 x the effect setting): Fire blunt, Water cut, Ice cut and bleeding,
/// Lightning armour penetration; Wind's speed has no hook here. The shares are this port's (the original's are not published).
/// </summary>
static class Elements
{
    public static readonly Element[] All = [Element.Fire, Element.Lightning, Element.Wind, Element.Water, Element.Ice];

    /// <summary>The secondary element's strength share by level band (the original's 25% at 50 up to 75% at 100).</summary>
    public static float SecondaryShare(float level) => level switch
    {
        >= 100 => 0.75f,
        >= 90 => 0.65f,
        >= 80 => 0.55f,
        >= 70 => 0.45f,
        >= 60 => 0.35f,
        >= 50 => 0.25f,
        _ => 0,
    };

    public static DamagePacket Apply(DamagePacket p, Element element, float s)
    {
        float body = p.Cut + p.Blunt + p.Pierce;
        return element switch
        {
            Element.Fire => p with { Blunt = p.Blunt + body * 0.30f * s },
            Element.Water => p with { Cut = p.Cut + body * 0.30f * s },
            Element.Ice => p with { Cut = p.Cut + body * 0.15f * s, BleedMultiplier = p.BleedMultiplier * (1 + 0.5f * s) },
            Element.Lightning => p with { ArmourPenetration = p.ArmourPenetration + 0.3f * s },
            _ => p,
        };
    }

    public static ModSkill Skill(Element element) => new("dxseba.element." + element.ToString().ToLowerInvariant(), element + " Enchantment",
        SkillColumn.Combat)
    {
        Description = $"Your {element.ToString().ToLowerInvariant()} enchantment. It grows by attacking with {element} active.",
        Affects = [Effect(element)],
        Training = [$"Landing hits with {element} active"],
    };

    static string Effect(Element element) => element switch
    {
        Element.Fire => "Adds blunt damage",
        Element.Lightning => "Improves armour penetration",
        Element.Wind => "Speed (not modelled in this port)",
        Element.Water => "Adds cut damage",
        _ => "Adds cut damage and bleeding",
    };
}
