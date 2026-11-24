using System.Globalization;
using HarmonyLib;
using Meitou.Data.Fcs;
using Meitou.Game.Audio;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.GunManufactureSounds;

/// <summary>
/// The two Harmony patches (README "Facts"), host thread, display only:
/// <list type="bullet">
/// <item>a prefix on the animation sound post (<c>Meitou.Game.Audio.CharacterSounds.Fire</c>) drops any event the gun table names (the
/// reload animations' gun cues), as the original suppresses them on every object;</item>
/// <item>a prefix on the combat sound playback (<c>Meitou.Game.Audio.CharacterAudio.PlayCombatSound</c>) replaces a crossbow shot (<c>Attack</c>
/// on the shooter) by the gun's own event when the shooter holds a gun of the table.</item>
/// </list>
/// A prefix that fails logs once and lets the game's sound play.
/// </summary>
static class GunSoundPatches
{
    static IReadOnlyDictionary<int, string> s_guns = new Dictionary<int, string>();
    static HashSet<string> s_cues = [];
    static ModContext? s_context;
    static volatile WorldSnapshot? s_snapshot;
    static int s_failed;

    public static void Patch(ModContext context, IReadOnlyDictionary<int, string> guns)
    {
        s_context = context;
        s_guns = guns;
        s_cues = guns.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var fire = AccessTools.Method(typeof(CharacterSounds), "Fire");
        var combat = AccessTools.Method("Meitou.Game.Audio.CharacterAudio:PlayCombatSound");
        if (fire is null || combat is null)
        {
            context.LogError("CharacterSounds.Fire or CharacterAudio.PlayCombatSound not found: the game's sounds play unchanged");
            return;
        }

        context.Harmony.Patch(fire, prefix: new HarmonyMethod(typeof(GunSoundPatches), nameof(FirePrefix)));
        context.Harmony.Patch(combat, prefix: new HarmonyMethod(typeof(GunSoundPatches), nameof(ShotPrefix)));
    }

    /// <summary>The snapshot on screen, for the shooters' guns (host thread, each frame).</summary>
    public static void Frame(ModFrame frame) => s_snapshot = frame.Snapshot;

    /// <summary>False (skip) for a gun cue of the reload animations.</summary>
    static bool FirePrefix(AnimationSoundEvent ev)
    {
        try
        {
            return !s_cues.Contains(ev.Event);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Failed(e);
            return true;
        }
    }

    /// <summary>False (skip) for a shot of a gun of the table, whose own event is posted on the shooter instead.</summary>
    static bool ShotPrefix(in SoundEvent e)
    {
        try
        {
            if (e.Kind != SoundKind.Shot || s_context is not { } context || GunEvent(e.Subject) is not { } gun) return true;

            context.PostSound(gun, e.Subject);
            return false;
        }
        catch (Exception x) when (x is not OutOfMemoryException)
        {
            Failed(x);
            return true;
        }
    }

    /// <summary>The event of the gun (a CROSSBOW of the table, by record number) the character holds, from the snapshot on screen; null if none.</summary>
    static string? GunEvent(CharacterId shooter)
    {
        if (shooter.IsNone || s_snapshot?.Characters.FirstOrDefault(c => c.Id == shooter)?.Appearance is not { } look) return null;

        var gun = look.Parts.FirstOrDefault(p => p.Record.Type == FcsRecordType.Crossbow)?.Record;
        return gun is not null && Number(gun.StringId) is int n && s_guns.TryGetValue(n, out var ev) ? ev : null;
    }

    /// <summary>A record id's number (the digits before its first <c>-</c>), the record's id in the original's game data.</summary>
    static int? Number(string stringId)
    {
        int dash = stringId.IndexOf('-');
        return dash > 0 && int.TryParse(stringId.AsSpan(0, dash), NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : null;
    }

    static void Failed(Exception e)
    {
        if (Interlocked.Exchange(ref s_failed, 1) == 0) s_context?.LogError($"gun sound patch failed ({e.GetType().Name}: {e.Message})");
    }
}
