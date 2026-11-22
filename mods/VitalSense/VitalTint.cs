using System.Numerics;
using Meitou.Game.Host;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.VitalSense;

/// <summary>
/// The colours of the original's <c>mod-config.defaults.json</c>.
/// </summary>
static class VitalColours
{
    /// <summary>
    /// Neutral, ally, enemy, squad (<see cref="Relation"/> order): <c>neutral_color_hex</c> #FFA64D, <c>ally_color_hex</c> #DEE85A,
    /// <c>enemy_color_hex</c> #FF3333, <c>squad_color_hex</c> #40FF40.
    /// </summary>
    public static readonly Vector4[] Relations = [Hex(0xFFA64DFF), Hex(0xDEE85AFF), Hex(0xFF3333FF), Hex(0x40FF40FF)];

    /// <summary>
    /// <c>bounty_color_*_hex</c>, trivial to legendary, with their alpha.
    /// </summary>
    public static readonly Vector4[] Tiers =
    [
        Hex(0xB0B0B0C8), Hex(0xC9A57BD4), Hex(0x9FBD69E0), Hex(0x6FA6D9EC), Hex(0xFFC247F5), Hex(0xFF8A2BFA), Hex(0xE03131FF),
    ];

    static Vector4 Hex(uint rgba) =>
        new(((rgba >> 24) & 0xFF) / 255f, ((rgba >> 16) & 0xFF) / 255f, ((rgba >> 8) & 0xFF) / 255f, (rgba & 0xFF) / 255f);
}

/// <summary>
/// The character tint, drawn as the engine's <c>Glow</c> on the whole character while the markers show: the marked characters, the player's
/// conscious ones within the distance (<c>character_tint_include_squad</c>), in the relation colour, or the bounty tier's colour for one with a
/// bounty (<b>Verified</b>, original DLL (decompiled): the tier colour replaces the relation colour when the bounty is above 0). Host thread only.
/// </summary>
static class VitalTint
{
    /// <summary>
    /// Each renewal's length (game seconds); renewed every frame the markers show, stopped as soon as they do not.
    /// </summary>
    const float Seconds = 0.5f;

    /// <summary>
    /// Glow strength: the original replaces the lit albedo (alpha 1), the engine's glow adds light; half keeps the texture readable.
    /// </summary>
    const float Strength = 0.5f;

    static ModContext? s_context;
    static readonly HashSet<CharacterId> Lit = [];
    static readonly HashSet<CharacterId> Now = [];

    public static void Install(ModContext context) => s_context = context;

    public static void Renew(WorldSession session, List<VitalTarget> targets)
    {
        var settings = VitalSenseMod.Settings;
        Now.Clear();
        if (settings.Tint && s_context is { } context)
        {
            foreach (var target in targets) Light(context, target);
            if (settings.TintSquad) LightSquad(context, session, settings);
        }

        StopOthers();
    }

    public static void Clear()
    {
        Now.Clear();
        StopOthers();
    }

    static void Light(ModContext context, VitalTarget target)
    {
        var settings = VitalSenseMod.Settings;
        if (target.Vital.State == VitalState.None && !settings.TintBounty) return;

        int tier = VitalMarkers.Tier(target.Vital.Bounty);
        var colour = tier >= 0 ? VitalColours.Tiers[tier] : VitalColours.Relations[(int)target.Relation];
        Glow(context, target.Character.Id, colour);
    }

    static void LightSquad(ModContext context, WorldSession session, VitalSettings settings)
    {
        var centre = session.Camera.Current.Target;
        foreach (var c in session.CurrentSnapshot.Characters)
        {
            if (!c.IsPlayer || !c.Standing || c.Appearance is null || Now.Contains(c.Id)) continue;
            if (VitalMarkers.Flat(c.Position, centre) > settings.MaxDistance) continue;

            Glow(context, c.Id, VitalColours.Relations[(int)Relation.Squad]);
        }
    }

    static void Glow(ModContext context, CharacterId id, Vector4 colour)
    {
        if (context.Glow(GlowTarget.Character(id), new Vector3(colour.X, colour.Y, colour.Z), Seconds, Strength)) Now.Add(id);
    }

    static void StopOthers()
    {
        foreach (var id in Lit)
            if (!Now.Contains(id)) s_context?.StopGlow(GlowTarget.Character(id));

        Lit.Clear();
        Lit.UnionWith(Now);
    }
}
