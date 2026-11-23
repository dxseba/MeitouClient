using System.Numerics;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.ElementalEnchantment;

/// <summary>
/// The hits' looks, from the display-only hit feed, as the original's: from level 40, with *Impact effects* on, a hit by an enchanted character
/// bursts its main element's impact (<c>EE_Impact_*</c>) and its secondary's on the victim for up to a second; with *Device combat effects* on, a
/// ward-wearer's hit shows the ward's effect (<c>EE_Device*</c>) at its pelvis. Base-game stand-ins in the element's colour without the original
/// mod's particles. Each landed hit flares the attacker's blade and hand particles (<see cref="WeaponAura.Flare"/>). A bolt an enchanted
/// character shoots carries its main element's bolt system (<c>EE_Bolt_*</c>, fire's <c>SB_Prototype_HandFire</c>) until it lands or goes
/// (<see cref="ModContext.AddProjectileFeed"/>, <see cref="EffectAnchor.Projectile"/>).
/// </summary>
sealed class ElementEffects(ModContext context, EnchantSettings settings, WeaponAura aura)
{
    /// <summary>
    /// The most a bolt's effect is kept (game seconds); it is stopped when the bolt lands or goes.
    /// </summary>
    const float BoltSeconds = 10f;

    /// <summary>
    /// The bolt effects playing, by bolt id.
    /// </summary>
    readonly Dictionary<int, string> _bolts = [];

    /// <summary>EFFECT <c>small- sparks1</c> (system <c>kenshi_sparks</c>).</summary>
    const string Sparks = "5463-nodes_furniture1_otto.mod";

    /// <summary>EFFECT <c>small- fire2_torch</c> (system <c>kenshi_fire2_torch</c>).</summary>
    const string TorchFlame = "5462-new_furniture_otto.mod";

    /// <summary>EFFECT <c>small- smoke3_small</c> (system <c>kenshi_smoke3_small</c>).</summary>
    const string SmallSmoke = "5462-nodes_furniture1_otto.mod";

    /// <summary>The footstep dust and splash systems (particle scripts <c>Dustkick01.pu</c>, <c>Footsplashes01.pu</c>).</summary>
    const string DustKick = "Dustkick01", Splash = "Footsplashes01";

    /// <summary>
    /// The original's impact life (1000 ms) and a device burst's.
    /// </summary>
    const float ImpactSeconds = 1f, DeviceSeconds = 1f;

    /// <summary>
    /// The bone the original hangs the ward's effect on.
    /// </summary>
    const string Pelvis = "Bip01 Pelvis";

    public void Feed(IReadOnlyList<CombatHit> hits)
    {
        if (!settings.Enabled || ElementalEnchantmentMod.Running?.View is not { Count: > 0 } view) return;

        foreach (var hit in hits)
        {
            if (hit.Source == CombatHitSource.Execution || !view.TryGetValue(hit.Attacker, out var shown) || !shown.Able) continue;

            var enchant = shown.Enchant;
            if (enchant.Main != Element.None) aura.Flare(hit.Attacker);
            if (settings.Effects && enchant.Main != Element.None && shown.Level >= Elements.ImpactLevel)
            {
                Impact(hit.Victim, enchant.Main);
                if (enchant.Second != Element.None) Impact(hit.Victim, enchant.Second);
            }

            if (settings.DeviceEffects && enchant.Ward != Element.None) Device(hit.Attacker, enchant.Ward);
        }
    }

    /// <summary>
    /// The bolts' changes: a bolt fired by an enchanted character takes its main element's bolt system; a landed or gone one loses it.
    /// </summary>
    public void Projectiles(IReadOnlyList<ProjectileEvent> events)
    {
        var view = ElementalEnchantmentMod.Running?.View;
        foreach (var e in events)
        {
            if (e.Change != ProjectileChange.Fired)
            {
                Unbolt(e.Id);
                continue;
            }

            if (settings.Enabled && view is not null && view.TryGetValue(e.Shooter, out var shown) && shown is { Able: true, Main: not Element.None })
                Bolt(e.Id, shown.Main);
        }
    }

    /// <summary>
    /// One bolt's system: the original's when loaded, else the element's stand-in tinted.
    /// </summary>
    void Bolt(int id, Element element)
    {
        string original = element == Element.Fire ? "SB_Prototype_HandFire" : $"EE_Bolt_{element}";
        var anchor = EffectAnchor.Projectile(id);
        bool kept = context.HasEffect(original)
            ? context.KeepEffect(original, anchor, BoltSeconds)
            : context.KeepEffect(original = WeaponAura.Particles(element), anchor, BoltSeconds, WeaponAura.Colour(element));
        if (kept) _bolts[id] = original;
    }

    /// <summary>
    /// Ends a bolt's system.
    /// </summary>
    void Unbolt(int id)
    {
        if (_bolts.Remove(id, out var effect)) context.StopEffect(effect, EffectAnchor.Projectile(id));
    }

    /// <summary>
    /// One element's impact on the victim.
    /// </summary>
    void Impact(CharacterId victim, Element element)
    {
        string original = $"EE_Impact_{element}";
        if (context.HasEffect(original)) context.PlayEffect(original, victim, seconds: ImpactSeconds);
        else context.PlayEffect(StandIn(element), victim, seconds: ImpactSeconds, tint: WeaponAura.Colour(element));
    }

    /// <summary>
    /// The ward's effect at the wearer's pelvis (the original's device systems; nothing without them).
    /// </summary>
    void Device(CharacterId wearer, Element ward)
    {
        string system = ward switch
        {
            Element.Fire => "EE_AuraPreview",
            Element.Lightning => "EE_DeviceStorm",
            _ => $"EE_Device{ward}",
        };
        if (context.HasEffect(system)) context.PlayEffect(system, wearer, Pelvis, DeviceSeconds);
    }

    /// <summary>The base game's stand-in impact.</summary>
    static string StandIn(Element element) => element switch
    {
        Element.Fire => TorchFlame,
        Element.Lightning => Sparks,
        Element.Wind => DustKick,
        Element.Water => Splash,
        _ => SmallSmoke,
    };
}
