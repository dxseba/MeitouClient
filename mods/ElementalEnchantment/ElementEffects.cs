using System.Numerics;
using Meitou.Modding;
using Meitou.Simulation.Combat;

namespace Meitou.ElementalEnchantment;

/// <summary>
/// The elements' looks, from the display-only hit feed: each hit by a character with an element active plays the element's impact on the
/// victim's chest and a short spray of sparks in its colour at the attacker's weapon (<c>Bip01 Prop2</c>, where the game throws its parry sparks).
/// </summary>
sealed class ElementEffects(ModContext context, EnchantSettings settings)
{
    /// <summary>The weapon's bone on the game's rigs.</summary>
    const string WeaponBone = "Bip01 Prop2";

    /// <summary>EFFECT <c>small- sparks1</c> (system <c>kenshi_sparks</c>), the parry sparks.</summary>
    const string Sparks = "5463-nodes_furniture1_otto.mod";

    /// <summary>EFFECT <c>small- fire2_torch</c> (system <c>kenshi_fire2_torch</c>).</summary>
    const string TorchFlame = "5462-new_furniture_otto.mod";

    /// <summary>EFFECT <c>small- smoke3_small</c> (system <c>kenshi_smoke3_small</c>).</summary>
    const string SmallSmoke = "5462-nodes_furniture1_otto.mod";

    /// <summary>The footstep dust and splash systems (particle scripts <c>Dustkick01.pu</c>, <c>Footsplashes01.pu</c>).</summary>
    const string DustKick = "Dustkick01", Splash = "Footsplashes01";

    const float ImpactSeconds = 0.3f, TrailSeconds = 0.25f;

    public void Feed(IReadOnlyList<CombatHit> hits)
    {
        if (!settings.Enabled || !settings.Effects || ElementalEnchantmentMod.Running?.View is not { Count: > 0 } view) return;

        foreach (var hit in hits)
        {
            if (hit.Source == CombatHitSource.Execution || !view.TryGetValue(hit.Attacker, out var active)) continue;
            if (active.Element == Element.None) continue;

            context.PlayEffect(Impact(active.Element), hit.Victim, seconds: ImpactSeconds, tint: Tint(active.Element));
            context.PlayEffect(Sparks, hit.Attacker, WeaponBone, TrailSeconds, Tint(active.Element));
        }
    }

    static string Impact(Element element) => element switch
    {
        Element.Fire => TorchFlame,
        Element.Lightning => Sparks,
        Element.Wind => DustKick,
        Element.Water => Splash,
        _ => SmallSmoke,
    };

    static Vector3 Tint(Element element) => element switch
    {
        Element.Fire => new Vector3(1.8f, 0.7f, 0.2f),
        Element.Lightning => new Vector3(0.7f, 0.9f, 2.0f),
        Element.Wind => new Vector3(1.3f, 1.4f, 1.2f),
        Element.Water => new Vector3(0.3f, 0.7f, 1.6f),
        _ => new Vector3(1.1f, 1.6f, 2.0f),
    };
}
