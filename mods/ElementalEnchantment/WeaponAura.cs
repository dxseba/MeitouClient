using System.Diagnostics;
using System.Numerics;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.ElementalEnchantment;

/// <summary>
/// The elements on a character, as the original shows them (its blade and hand particle systems, <c>SB_Prototype_*</c>): while a character has a
/// main element, its held blade gives off that element's blade system and, once the secondary counts (level 50), the secondary's too
/// (<see cref="ModContext.KeepEffect"/> on <see cref="EffectAnchor.Weapon"/>, sized by level); fighting unarmed, both hands give off the hand
/// systems instead (<see cref="EffectAnchor.Bone"/> on <c>Bip01 R Hand</c> and <c>Bip01 L Hand</c>). The size by level is the original's 0.8 to
/// 1.15, times 1.3 for 0.18 s after the character lands a hit. From level 20, with *Weapon trails* on, an armed character swinging leaves the
/// original's trail (<c>EE_Trail</c>) from a point on its blade (<see cref="ModContext.KeepTrail"/>). Without the original mod's particles, base-game
/// stand-ins in the element's colour. Players and the enchanted bosses alike; run once a frame (<see cref="ModContext.AddFrame"/>), renewed once a
/// second or when a flare starts or ends. The <c>Blade tint</c> setting also tints the blade (<see cref="ModContext.Glow"/>).
/// </summary>
sealed class WeaponAura(ModContext context, EnchantSettings settings)
{
    /// <summary>
    /// How long each renewal lasts (game seconds, so it holds at the highest game speed); renewed every <see cref="RenewSeconds"/> of real time.
    /// </summary>
    const float GlowSeconds = 10f, RenewSeconds = 1f, TrailKeepSeconds = 0.2f;

    /// <summary>
    /// The original's hit flare: the size times 1.3 for 0.18 s (real time) after a landed hit.
    /// </summary>
    const float FlareSize = 1.3f, FlareSeconds = 0.18f;

    /// <summary>
    /// The original's trail: from level 20, its places fade over 0.47 s (alpha 0.28 losing 0.59 a second); one camera-facing ribbon point on
    /// the blade at (L x 0.12 + L x 0.98) / 2 - 5 along it from the weapon's origin (L the weapon's length), here (0.12 + 0.98) / 2 of the
    /// weapon's 8.5 units (the - 5 needs the length in world units, <b>Unknown</b> here).
    /// </summary>
    const int TrailLevel = 20;
    const float TrailSeconds = 0.47f, TrailAlpha = 0.28f, TrailPoint = 8.5f * (0.12f + 0.98f) / 2;

    /// <summary>The base game's stand-ins: a small fire, the parry sparks, a small smoke (ice), light dust (wind and water).</summary>
    const string Flame = "kenshi_fire1_small", Sparks = "kenshi_sparks", Smoke = "kenshi_smoke3_small", Dust = "kenshi_light_dust_particles1";

    /// <summary>The blade tint's strength when the setting is on.</summary>
    const float TintStrength = 0.5f;

    /// <summary>The hand bones of the game's rigs.</summary>
    const string RightHand = "Bip01 R Hand", LeftHand = "Bip01 L Hand";

    /// <summary>
    /// The effects kept at the last renewal, by character and place (main blade, secondary blade, each hand's main and secondary): the effect, the
    /// anchor without its size (what the effect is kept by) and the anchor it was last kept with.
    /// </summary>
    readonly Dictionary<(CharacterId Character, int Place), (string Effect, EffectAnchor Key, EffectAnchor Anchor)> _kept = [];

    /// <summary>The characters whose blade is tinted, and those leaving a trail.</summary>
    readonly HashSet<CharacterId> _tinted = [], _trailing = [];

    /// <summary>When each character's hit flare ends (a <see cref="Stopwatch"/> timestamp).</summary>
    readonly Dictionary<CharacterId, long> _flares = [];

    /// <summary>The characters flaring at the last renewal.</summary>
    readonly HashSet<CharacterId> _flaring = [];

    long _lastRenew;

    /// <summary>
    /// A landed hit by <paramref name="attacker"/> (from the hit feed, on the host's thread): its effects flare.
    /// </summary>
    public void Flare(CharacterId attacker) =>
        _flares[attacker] = Stopwatch.GetTimestamp() + (long)(FlareSeconds * Stopwatch.Frequency);

    /// <summary>
    /// Once a frame: renews every enchanted character's effects once a second, or at once when a flare starts or ends.
    /// </summary>
    public void Frame(ModFrame frame)
    {
        long now = Stopwatch.GetTimestamp();
        foreach (var (id, end) in _flares.ToArray())
        {
            if (end <= now) _flares.Remove(id);
        }

        bool flaresChanged = !_flaring.SetEquals(_flares.Keys);
        if (!flaresChanged && _lastRenew != 0 && Stopwatch.GetElapsedTime(_lastRenew, now).TotalSeconds < RenewSeconds) return;

        _lastRenew = now;
        _flaring.Clear();
        _flaring.UnionWith(_flares.Keys);
        Renew(frame.Snapshot);
    }

    /// <summary>Keeps every enchanted character's effects and trails and stops the ones no longer wanted.</summary>
    void Renew(WorldSnapshot snapshot)
    {
        var wanted = new Dictionary<(CharacterId, int), (string, EffectAnchor, Vector3?)>();
        var view = ElementalEnchantmentMod.Running?.View;
        if (settings.Enabled && view is not null)
        {
            foreach (var (id, shown) in view)
            {
                if (shown.Able && shown.Main != Element.None) Want(wanted, id, shown);
            }
        }

        Apply(wanted);
        TintAll(wanted, view);
        Trails(snapshot, view);
    }

    /// <summary>
    /// The effects a character should show: blade systems when armed, hand systems when unarmed, the secondary's beside the main's.
    /// </summary>
    void Want(Dictionary<(CharacterId, int), (string, EffectAnchor, Vector3?)> wanted, CharacterId id, EnchantView shown)
    {
        var enchant = shown.Enchant;
        Element[] elements = enchant.Second == Element.None ? [enchant.Main] : [enchant.Main, enchant.Second];
        float size = Elements.VisualScale(shown.Level) * (_flaring.Contains(id) ? FlareSize : 1);
        for (int i = 0; i < elements.Length; i++)
        {
            if (!shown.Unarmed)
            {
                wanted[(id, i)] = Pick(Blade(elements[i]), EffectAnchor.Weapon(id).Sized(size), elements[i]);
                continue;
            }

            wanted[(id, 2 + i)] = Pick(Hand(elements[i]), EffectAnchor.Bone(id, RightHand).Scaled(size), elements[i]);
            wanted[(id, 4 + i)] = Pick(Hand(elements[i]), EffectAnchor.Bone(id, LeftHand).Scaled(size), elements[i]);
        }
    }

    /// <summary>
    /// The original's system in its own colours when loaded, else the stand-in tinted.
    /// </summary>
    (string, EffectAnchor, Vector3?) Pick(string original, EffectAnchor anchor, Element element) =>
        context.HasEffect(original) ? (original, anchor, null) : (Particles(element), anchor, Colour(element));

    /// <summary>
    /// The anchor an effect is kept by: its size and stretch aside, as <see cref="ModContext.KeepEffect"/> renews a kept effect in place.
    /// </summary>
    static EffectAnchor KeyOf(EffectAnchor anchor) => anchor with { Stretch = null, Size = null };

    /// <summary>
    /// Stops what is no longer wanted (or changed), keeps the rest up, taking a new size in place.
    /// </summary>
    void Apply(Dictionary<(CharacterId, int), (string Effect, EffectAnchor Anchor, Vector3? Tint)> wanted)
    {
        foreach (var (key, kept) in _kept.ToArray())
        {
            if (wanted.TryGetValue(key, out var want) && want.Effect == kept.Effect && KeyOf(want.Anchor) == kept.Key) continue;

            context.StopEffect(kept.Effect, kept.Anchor);
            _kept.Remove(key);
        }

        foreach (var (key, want) in wanted)
        {
            if (context.KeepEffect(want.Effect, want.Anchor, GlowSeconds, want.Tint)) _kept[key] = (want.Effect, KeyOf(want.Anchor), want.Anchor);
        }
    }

    /// <summary>
    /// The original's weapon trail on every armed enchanted character of level 20 or more while it swings (<see cref="CharacterSnapshot.Attacking"/>,
    /// the original's "while attacking"), while the setting is on: a ribbon from one point on the blade in the main element's colour, fading out
    /// behind; stopped otherwise.
    /// </summary>
    void Trails(WorldSnapshot snapshot, IReadOnlyDictionary<CharacterId, EnchantView>? view)
    {
        var trailing = new HashSet<CharacterId>();
        if (settings.Enabled && settings.Trails && view is not null)
        {
            var fighting = snapshot.Characters.Where(c => c.Attacking).Select(c => c.Id).ToHashSet();
            foreach (var (id, shown) in view)
            {
                if (!shown.Able || shown.Main == Element.None || shown.Unarmed || shown.Level < TrailLevel || !fighting.Contains(id)) continue;
                if (context.KeepTrail(TrailAnchor(id), TrailOf(shown.Main), TrailKeepSeconds)) trailing.Add(id);
            }
        }

        foreach (var id in _trailing.Except(trailing).ToArray()) context.StopTrail(TrailAnchor(id));
        _trailing.Clear();
        _trailing.UnionWith(trailing);
    }

    static EffectAnchor TrailAnchor(CharacterId id) => EffectAnchor.Weapon(id, new(0, TrailPoint, 0));

    /// <summary>
    /// The trail's look for an element: the original's material and colour, its alpha and fade; a camera-facing ribbon (the width is the
    /// engine's default, the original's <b>Unknown</b>).
    /// </summary>
    static TrailStyle TrailOf(Element element)
    {
        var colour = new Vector4(TrailColour(element), TrailAlpha);
        return new TrailStyle
        {
            Seconds = TrailSeconds, Colour = colour, EndColour = colour with { W = 0 }, Material = "EE_Trail",
        };
    }

    /// <summary>
    /// The original's trail colour of each element (its ribbon's initial colour).
    /// </summary>
    static Vector3 TrailColour(Element element) => element switch
    {
        Element.Fire => new(1, 0.2f, 0.04f),
        Element.Lightning => new(0.45f, 0.72f, 1),
        Element.Wind => new(0.65f, 1, 0.82f),
        Element.Water => new(0.12f, 0.55f, 1),
        _ => new(0.72f, 0.92f, 1),
    };

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

    /// <summary>The base game's stand-in system for an element.</summary>
    internal static string Particles(Element element) => element switch
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
