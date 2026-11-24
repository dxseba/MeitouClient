using System.Diagnostics;
using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.KathunLightsabers;

/// <summary>
/// Once a frame on the host's thread (every <see cref="Refresh"/> seconds): the player's characters and those within <c>range</c> of one,
/// alive and holding a saber drawn, keep their blade glowing in the saber's colour; the first <c>max_lights</c> of them (the player's first) also
/// keep their halo glows along each blade and hand their light to <see cref="SaberLamps"/>.
/// </summary>
sealed class SaberFrame(ModContext context, SaberConfig config, SaberCatalog catalog)
{
    /// <summary>Seconds between refreshes; each glow and halo is kept for <see cref="Keep"/>, renewed in place before it ends.</summary>
    const float Refresh = 0.25f, Keep = 0.6f;

    /// <summary>At most this many halo glows at once (the effects' 64 alive are shared by every mod).</summary>
    const int HaloBudget = 48;

    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly HashSet<string> _missingHalo = [];
    readonly Dictionary<string, Data.World.WorldLight> _lights = [];
    double _next;

    public void Frame(ModFrame frame)
    {
        try
        {
            SaberLamps.Bind(frame);
            if (_clock.Elapsed.TotalSeconds < _next) return;

            _next = _clock.Elapsed.TotalSeconds + Refresh;
            Update(frame.Snapshot);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            SaberLamps.Publish([]);
            context.LogError($"saber frame failed: {e.Message}");
        }
    }

    void Update(WorldSnapshot? snapshot)
    {
        var held = snapshot is null ? [] : Held(snapshot);
        foreach (var (id, saber) in held) context.Glow(GlowTarget.Weapon(id), saber.Colour.Glow, Keep, config.GlowBrightness);

        var lit = config.Light || config.Halo ? held.Take(config.MaxLights).ToList() : [];
        if (config.Halo) KeepHalos(lit);

        SaberLamps.Publish(config.Light ? [.. lit.Select(s => Lamp(s.Id, s.Saber)).OfType<SaberLamp>()] : []);
    }

    /// <summary>The sabers held drawn: the player's living characters first, then the others within range of one, in snapshot order.</summary>
    List<(CharacterId Id, Saber Saber)> Held(WorldSnapshot snapshot)
    {
        var players = snapshot.Characters.Where(c => c.IsPlayer && Alive(c)).ToList();
        float range2 = config.Range * config.Range;
        bool Near(CharacterSnapshot c) => players.Any(p => Vector3.DistanceSquared(p.Position, c.Position) < range2);

        var result = new List<(CharacterId, Saber)>();
        foreach (var c in players.Concat(snapshot.Characters.Where(c => !c.IsPlayer && Alive(c) && Near(c))))
        {
            if (c.WeaponDrawn && HeldWeapon(c.Appearance) is { } mesh && catalog.Of(mesh) is { } saber) result.Add((c.Id, saber));
        }

        return result;
    }

    static bool Alive(CharacterSnapshot c) => c.Body is not { Dead: true };

    /// <summary>The model of the melee weapon the character holds (its <c>bare sword</c> part in the weapon slot); null when none.</summary>
    static string? HeldWeapon(CharacterAppearance? look) =>
        look?.Parts.FirstOrDefault(p => p.Field == "bare sword" && p.Slot == AttachSlot.Weapon && p.Record.Type == FcsRecordType.Weapon)?.Mesh;

    /// <summary>
    /// The halo glows (<c>KSG_halo_&lt;colour&gt;</c>, one every <c>halo_step</c> model units along each blade, its ends included, at their authored
    /// size) of the lit sabers; past <see cref="HaloBudget"/> the points are spaced wider and each glow grown to match (port choice).
    /// </summary>
    void KeepHalos(List<(CharacterId Id, Saber Saber)> lit)
    {
        var points = lit.Select(s => (s.Id, s.Saber, Points: HaloPoints(s.Saber))).ToList();
        int total = points.Sum(p => p.Points.Count);
        float thin = total > HaloBudget ? (float)HaloBudget / total : 1;

        foreach (var (id, saber, along) in points)
        {
            string effect = "KSG_halo_" + saber.Colour.Name;
            if (!context.HasEffect(effect))
            {
                if (_missingHalo.Add(effect)) context.Log($"no particle system {effect}: no halo (is the original enabled?)");
                continue;
            }

            int every = thin < 1 ? (int)MathF.Ceiling(1 / thin) : 1;
            for (int i = 0; i < along.Count; i += every)
                context.KeepEffect(effect, EffectAnchor.Weapon(id, new Vector3(0, along[i] * 8.5f, 0)).Scaled(1).Sized(every), Keep);
        }
    }

    /// <summary>
    /// The halo points of a saber as frame fractions: per blade n = round(length / halo_step), at least 1, and n + 1 points from end to end. The
    /// blade length is in the model's units; with the fractions the port takes a blade of 12.8 units as the original's 12.8 (README "Facts").
    /// </summary>
    List<float> HaloPoints(Saber saber)
    {
        var result = new List<float>();
        foreach (var (from, to) in saber.Blades)
        {
            float units = (to - from) * saber.ModelLength;
            int n = Math.Max(1, (int)(units / config.HaloStep + 0.5f));
            for (int i = 0; i <= n; i++) result.Add(from + (to - from) * i / n);
        }

        return result;
    }

    /// <summary>The saber's light: its colour's LIGHT record at the middle of its first blade; null without the record (logged once).</summary>
    SaberLamp? Lamp(CharacterId id, Saber saber)
    {
        if (context.Data.Find(saber.Colour.LightId) is not { Type: FcsRecordType.Light } light)
        {
            if (_missingHalo.Add("light:" + saber.Colour.LightId)) context.Log($"no light record {saber.Colour.LightId}");
            return null;
        }

        if (!_lights.TryGetValue(light.StringId, out var template)) _lights[light.StringId] = template = SaberLamps.Template(light);

        var (from, to) = saber.Blades[0];
        return new SaberLamp(id, template, (from + to) / 2);
    }
}
