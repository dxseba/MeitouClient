using System.Diagnostics;
using System.Numerics;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.ElementalEnchantment;

/// <summary>
/// The element on the blade, as the original shows it: while a character has an element active its held weapon glows in the element's colour
/// along the blade and gives off the element's particles (<see cref="ModContext.GlowWeapon"/>), at rest as in combat. Kept up once a second from
/// a hidden overlay node (the host's frame); a character whose element was cleared has its glow stopped.
/// </summary>
sealed class WeaponAura(ModContext context, EnchantSettings settings) : IUiExtension
{
    public string Id => "dxseba.elementalenchantment.aura";

    /// <summary>
    /// How long each renewal lasts (game seconds, so it holds at the highest game speed); renewed every <see cref="RenewSeconds"/> of real time.
    /// </summary>
    const float GlowSeconds = 10f, RenewSeconds = 1f;

    /// <summary>The flame of a torch (fire), the parry sparks (lightning) and a small smoke (the cold and airy elements).</summary>
    const string Flame = "kenshi_fire1_small", Sparks = "kenshi_sparks", Smoke = "kenshi_smoke3_small";

    /// <summary>The characters given a glow at the last renewal.</summary>
    readonly HashSet<CharacterId> _glowing = [];

    long _lastRenew;

    public void Register(UiRegistration registration) =>
        registration.Contribute(UiSlots.Overlay, _ =>
        {
            Renew();
            return new UiNode("elements-aura") { Visible = false };
        });

    /// <summary>Once a second: glows every character with an element active and stops the ones that no longer have one.</summary>
    void Renew()
    {
        long now = Stopwatch.GetTimestamp();
        if (_lastRenew != 0 && Stopwatch.GetElapsedTime(_lastRenew, now).TotalSeconds < RenewSeconds) return;

        _lastRenew = now;
        var view = ElementalEnchantmentMod.Running?.View;
        bool on = settings.Enabled && settings.Effects && view is not null;
        var glowingNow = new HashSet<CharacterId>();
        if (on)
        {
            foreach (var (id, active) in view!)
            {
                if (active.Element == Element.None) continue;
                if (context.GlowWeapon(id, Colour(active.Element), GlowSeconds, Strength(active.Level), Particles(active.Element))) glowingNow.Add(id);
            }
        }

        Stop(glowingNow);
    }

    /// <summary>Stops the glows of the characters glowing before but not now.</summary>
    void Stop(HashSet<CharacterId> glowingNow)
    {
        foreach (var id in _glowing)
        {
            if (!glowingNow.Contains(id)) context.StopWeaponGlow(id);
        }

        _glowing.Clear();
        _glowing.UnionWith(glowingNow);
    }

    /// <summary>The glow grows with the element's level: 0.6 at level 0 to 1.6 at 100.</summary>
    static float Strength(int level) => 0.6f + Math.Clamp(level, 0, 100) / 100f;

    static string Particles(Element element) => element switch
    {
        Element.Fire => Flame,
        Element.Lightning => Sparks,
        _ => Smoke,
    };

    /// <summary>The blade's colour (the screenshots: orange fire, pale cyan ice).</summary>
    static Vector3 Colour(Element element) => element switch
    {
        Element.Fire => new Vector3(1.6f, 0.55f, 0.12f),
        Element.Lightning => new Vector3(0.75f, 0.85f, 1.8f),
        Element.Wind => new Vector3(0.9f, 1.3f, 0.95f),
        Element.Water => new Vector3(0.25f, 0.6f, 1.6f),
        _ => new Vector3(0.6f, 1.4f, 1.8f),
    };
}
