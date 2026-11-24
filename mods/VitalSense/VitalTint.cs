using System.Numerics;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.VitalSense;

/// <summary>
/// The character tint, the engine's <c>Tint</c> (the original's <c>coloroverride</c>: the body's albedo blended to the colour by its alpha) while
/// the markers show, through walls as its <c>overrideDepth</c> when set: the marked characters, the player's conscious ones within the distance
/// (<c>character_tint_include_squad</c>), in the relation colour, or the bounty tier's colour for one with a bounty (<b>Verified</b>, original DLL
/// (decompiled): the tier colour replaces the relation colour when the bounty is above 0). Host thread only.
/// </summary>
static class VitalTint
{
    /// <summary>
    /// Each renewal's length (game seconds); renewed every frame the markers show, stopped as soon as they do not.
    /// </summary>
    const float Seconds = 0.5f;

    /// <summary>
    /// The most characters the engine draws through walls.
    /// </summary>
    const int ThroughWallsMax = 64;

    static ModContext? s_context;
    static readonly HashSet<CharacterId> Lit = [];
    static readonly HashSet<CharacterId> Now = [];
    static int s_throughWalls;

    public static void Install(ModContext context) => s_context = context;

    public static void Renew(ModFrame frame, List<VitalTarget> targets)
    {
        var settings = VitalSenseMod.Settings;
        Now.Clear();
        s_throughWalls = 0;
        if (settings.Tint && s_context is { } context)
        {
            foreach (var target in targets) Light(context, target);
            if (settings.TintSquad) LightSquad(context, frame, settings);
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
        var colour = tier >= 0 ? VitalColours.Tier(tier) : VitalColours.Of(target.Relation);
        Paint(context, target.Character.Id, colour);
    }

    static void LightSquad(ModContext context, ModFrame frame, VitalSettings settings)
    {
        var centre = frame.CameraTarget;
        foreach (var c in frame.Snapshot.Characters)
        {
            if (!c.IsPlayer || !c.Standing || c.Appearance is null || Now.Contains(c.Id)) continue;
            if (VitalMarkers.Flat(c.Position, centre) > settings.MaxDistance) continue;

            Paint(context, c.Id, VitalColours.Of(Relation.Squad));
        }
    }

    /// <summary>
    /// Tints <paramref name="id"/> by the colour's alpha (1 for the relation colours, the tier's own for a bounty).
    /// </summary>
    static void Paint(ModContext context, CharacterId id, Vector4 colour)
    {
        bool walls = VitalSenseMod.Settings.TintThroughWalls && s_throughWalls < ThroughWallsMax;
        if (!context.Tint(GlowTarget.Character(id), new Vector3(colour.X, colour.Y, colour.Z), Seconds, colour.W, walls)) return;

        Now.Add(id);
        if (walls) s_throughWalls++;
    }

    static void StopOthers()
    {
        foreach (var id in Lit)
            if (!Now.Contains(id)) s_context?.StopGlow(GlowTarget.Character(id));

        Lit.Clear();
        Lit.UnionWith(Now);
    }
}
