using System.Diagnostics;
using System.Numerics;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.ElementalEnchantment;

/// <summary>
/// The elements on a character, as the original shows them (its blade and hand particle systems, <c>SB_Prototype_*</c>): while a character has a
/// main element, its held blade gives off that element's blade system and, once the secondary counts (level 50), the secondary's too
/// (<see cref="ModContext.KeepEffect"/> on <see cref="EffectAnchor.Weapon"/>); fighting unarmed, both hands give off the hand systems instead
/// (<see cref="EffectAnchor.Bone"/> on <c>Bip01 R Hand</c> and <c>Bip01 L Hand</c>, sized by level as the original's 0.8 to 1.15). Without the
/// original mod's particles, base-game stand-ins in the element's colour. Players and the enchanted bosses alike; kept up once a second from a
/// hidden overlay node. The <c>Blade tint</c> setting also tints the blade (<see cref="ModContext.Glow"/>).
/// </summary>
sealed class WeaponAura(ModContext context, EnchantSettings settings) : IUiExtension
{
    public string Id => "dxseba.elementalenchantment.aura";

    /// <summary>
    /// How long each renewal lasts (game seconds, so it holds at the highest game speed); renewed every <see cref="RenewSeconds"/> of real time.
    /// </summary>
    const float GlowSeconds = 10f, RenewSeconds = 1f;

    /// <summary>The base game's stand-ins: a small fire, the parry sparks, a small smoke (ice), light dust (wind and water).</summary>
    const string Flame = "kenshi_fire1_small", Sparks = "kenshi_sparks", Smoke = "kenshi_smoke3_small", Dust = "kenshi_light_dust_particles1";

    /// <summary>The blade tint's strength when the setting is on.</summary>
    const float TintStrength = 0.5f;

    /// <summary>The hand bones of the game's rigs.</summary>
    const string RightHand = "Bip01 R Hand", LeftHand = "Bip01 L Hand";

    /// <summary>
    /// The effects kept at the last renewal, by character and place (main blade, secondary blade, each hand's main and secondary).
    /// </summary>
    readonly Dictionary<(CharacterId Character, int Place), (string Effect, EffectAnchor Anchor)> _kept = [];

    /// <summary>The characters whose blade is tinted.</summary>
    readonly HashSet<CharacterId> _tinted = [];

    long _lastRenew;

    public void Register(UiRegistration registration) =>
        registration.Contribute(UiSlots.Overlay, _ =>
        {
            Renew();
            return new UiNode("elements-aura") { Visible = false };
        });

    /// <summary>Once a second: keeps every enchanted character's effects and stops the ones no longer wanted.</summary>
    void Renew()
    {
        long now = Stopwatch.GetTimestamp();
        if (_lastRenew != 0 && Stopwatch.GetElapsedTime(_lastRenew, now).TotalSeconds < RenewSeconds) return;

        _lastRenew = now;
        var wanted = new Dictionary<(CharacterId, int), (string, EffectAnchor, Vector3?)>();
        if (settings.Enabled && ElementalEnchantmentMod.Running?.View is { } view)
        {
            foreach (var (id, shown) in view)
            {
                if (shown.Able && shown.Main != Element.None) Want(wanted, id, shown);
            }
        }

        Apply(wanted);
        TintAll(wanted, ElementalEnchantmentMod.Running?.View);
    }

    /// <summary>
    /// The effects a character should show: blade systems when armed, hand systems when unarmed, the secondary's beside the main's.
    /// </summary>
    void Want(Dictionary<(CharacterId, int), (string, EffectAnchor, Vector3?)> wanted, CharacterId id, EnchantView shown)
    {
        var enchant = shown.Enchant;
        Element[] elements = enchant.Second == Element.None ? [enchant.Main] : [enchant.Main, enchant.Second];
        float scale = Elements.VisualScale(shown.Level);
        for (int i = 0; i < elements.Length; i++)
        {
            if (!shown.Unarmed)
            {
                wanted[(id, i)] = Pick(Blade(elements[i]), EffectAnchor.Weapon(id), elements[i]);
                continue;
            }

            wanted[(id, 2 + i)] = Pick(Hand(elements[i]), EffectAnchor.Bone(id, RightHand).Scaled(scale), elements[i]);
            wanted[(id, 4 + i)] = Pick(Hand(elements[i]), EffectAnchor.Bone(id, LeftHand).Scaled(scale), elements[i]);
        }
    }

    /// <summary>
    /// The original's system in its own colours when loaded, else the stand-in tinted.
    /// </summary>
    (string, EffectAnchor, Vector3?) Pick(string original, EffectAnchor anchor, Element element) =>
        context.HasEffect(original) ? (original, anchor, null) : (Particles(element), anchor, Colour(element));

    /// <summary>
    /// Stops what is no longer wanted (or changed), keeps the rest up.
    /// </summary>
    void Apply(Dictionary<(CharacterId, int), (string Effect, EffectAnchor Anchor, Vector3? Tint)> wanted)
    {
        foreach (var (key, kept) in _kept.ToArray())
        {
            if (wanted.TryGetValue(key, out var want) && want.Effect == kept.Effect && want.Anchor == kept.Anchor) continue;

            context.StopEffect(kept.Effect, kept.Anchor);
            _kept.Remove(key);
        }

        foreach (var (key, want) in wanted)
        {
            if (context.KeepEffect(want.Effect, want.Anchor, GlowSeconds, want.Tint)) _kept[key] = (want.Effect, want.Anchor);
        }
    }

    /// <summary>
    /// The blade tint of every armed enchanted character while the setting is on; taken off otherwise.
    /// </summary>
    void TintAll(Dictionary<(CharacterId Character, int Place), (string, EffectAnchor, Vector3?)> wanted,
        IReadOnlyDictionary<CharacterId, EnchantView>? view)
    {
        var tint = new HashSet<CharacterId>();
        if (settings.BladeTint && view is not null)
        {
            foreach (var key in wanted.Keys)
            {
                if (key.Place != 0 || !view.TryGetValue(key.Character, out var shown)) continue;
                if (context.Glow(GlowTarget.Weapon(key.Character), Colour(shown.Main), GlowSeconds, TintStrength * Strength(shown.Level)))
                    tint.Add(key.Character);
            }
        }

        foreach (var id in _tinted.Except(tint).ToArray()) context.StopGlow(GlowTarget.Weapon(id));
        _tinted.Clear();
        _tinted.UnionWith(tint);
    }

    /// <summary>The original mod's blade system for <paramref name="element"/> (fire's has no <c>Blade</c> suffix).</summary>
    static string Blade(Element element) => element == Element.Fire ? "SB_Prototype_Fire" : $"SB_Prototype_{element}Blade";

    /// <summary>The original mod's hand system for <paramref name="element"/> (fire's is <c>HandFire</c>).</summary>
    static string Hand(Element element) => element == Element.Fire ? "SB_Prototype_HandFire" : $"SB_Prototype_{element}Hand";

    /// <summary>The tint grows with the level: 0.6 at level 0 to 1.6 at 100.</summary>
    static float Strength(int level) => 0.6f + Math.Clamp(level, 0, 100) / 100f;

    static string Particles(Element element) => element switch
    {
        Element.Fire => Flame,
        Element.Lightning => Sparks,
        Element.Ice => Smoke,
        _ => Dust,
    };

    /// <summary>The element's colour (the screenshots: orange fire, pale cyan ice).</summary>
    internal static Vector3 Colour(Element element) => element switch
    {
        Element.Fire => new Vector3(1.6f, 0.55f, 0.12f),
        Element.Lightning => new Vector3(0.75f, 0.85f, 1.8f),
        Element.Wind => new Vector3(0.9f, 1.3f, 0.95f),
        Element.Water => new Vector3(0.25f, 0.6f, 1.6f),
        _ => new Vector3(0.6f, 1.4f, 1.8f),
    };
}
