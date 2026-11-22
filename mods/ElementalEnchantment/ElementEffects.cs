using System.Numerics;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Session;

namespace Meitou.ElementalEnchantment;

/// <summary>
/// The elements' looks, from the display-only hit feed: each hit by a character with an element active plays the element's impact on the
/// victim's chest and a short spray of sparks in its colour at the attacker's weapon (<c>Bip01 Prop2</c>, where the game throws its parry sparks),
/// and keeps the element burning on that weapon point while the character keeps landing hits (<see cref="GlowSeconds"/> after the last).
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

    const float ImpactSeconds = 0.6f, TrailSeconds = 0.25f;

    /// <summary>How long the weapon's element burns after a hit; it is started again once <see cref="GlowRestart"/> of it has passed.</summary>
    const float GlowSeconds = 3f, GlowRestart = 2f;

    /// <summary>The tick each character's weapon glow was last started (host thread only, like the feed).</summary>
    readonly Dictionary<CharacterId, long> _glowStarted = [];

    public void Feed(IReadOnlyList<CombatHit> hits)
    {
        if (!settings.Enabled || !settings.Effects || ElementalEnchantmentMod.Running?.View is not { Count: > 0 } view) return;

        foreach (var hit in hits)
        {
            if (hit.Source == CombatHitSource.Execution || !view.TryGetValue(hit.Attacker, out var active)) continue;
            if (active.Element == Element.None) continue;

            context.PlayEffect(Impact(active.Element), hit.Victim, seconds: ImpactSeconds, tint: Tint(active.Element));
            context.PlayEffect(Sparks, hit.Attacker, WeaponBone, TrailSeconds, Tint(active.Element));
            Glow(hit, active.Element);
        }
    }

    /// <summary>Starts the element on the attacker's weapon unless its last glow is still well under way.</summary>
    void Glow(in CombatHit hit, Element element)
    {
        long restart = (long)(GlowRestart * SimulationClock.TickRate);
        if (_glowStarted.TryGetValue(hit.Attacker, out long started) && hit.Tick >= started && hit.Tick - started < restart) return;

        _glowStarted[hit.Attacker] = hit.Tick;
        context.PlayEffect(Impact(element), hit.Attacker, WeaponBone, GlowSeconds, Tint(element));
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
