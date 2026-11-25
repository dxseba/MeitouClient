using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Gameplay;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Movement;

namespace Meitou.Nitoryu;

/// <summary>
/// The pose layer for the original's own tracks (its <c>Ogre::Skeleton::_updateTransforms</c> pose engine; README "My animations"), on the
/// ANIMATION records the data phase made for them (<see cref="NitoryuData"/>):
/// <list type="bullet">
/// <item>a pool move with <c>nitoryu clip</c> plays that track in place of its vanilla one, at the technique's progress (so its timing and
/// blows stay the record's);</item>
/// <item>a dual fighter in its combat stance, neither attacking nor blocking, holds its rank's stance (<c>nitoryu stance deshi</c> ...) in place
/// of the game's guard.</item>
/// </list>
/// Harmony: a prefix on <c>Meitou.Simulation.Movement.AnimationSystem.Update</c> (the combat view's clip) and a postfix on
/// <c>Meitou.Simulation.Movement.AnimationSystem.PickGait</c> (the guard). Both run in the character's own partition and change only its wanted
/// clips; a track the character's rig lacks is not swapped in. A failure logs once and leaves the vanilla clips.
/// </summary>
static class PoseLayer
{
    /// <summary>A library's track indices: the technique definition to the track laid over it, and each rank's stance.</summary>
    sealed class Tracks
    {
        public readonly Dictionary<int, int> Over = [];
        public readonly int[] Stances = [-1, -1, -1, -1, -1, -1];
        public readonly int[] Runs = [-1, -1, -1, -1, -1];
        public readonly int[] Jogs = [-1, -1, -1, -1, -1];
        public readonly Dictionary<int, int> BlockDirection = [];
        public readonly Dictionary<int, int> Mirrored = [];
        public int Overhead = -1, AngledLeft = -1, AngledRight = -1, Scissor = -1, MainBlock = -1, Offhand = -1;
    }

    /// <summary>The speeds a run and a jog hold start at (units a second; the original's own thresholds are not known).</summary>
    const float RunSpeed = 20, JogSpeed = 8;

    /// <summary>How long after the second blade's parry its own block track plays (the original's <c>parry ms</c>, 650).</summary>
    const float ParryMs = 650;

    static readonly ConditionalWeakTable<AnimationLibrary, Tracks> Table = [];
    static Func<AnimationSystem, CharacterAnimation, CharacterCold, int, bool>? s_plays;
    static AnimationLibrary? s_library;
    static ModContext? s_context;
    static int s_failed;

    public static void Patch(ModContext context)
    {
        s_context = context;
        const string type = "Meitou.Simulation.Movement.AnimationSystem";
        var update = AccessTools.Method(typeof(AnimationSystem), nameof(AnimationSystem.Update));
        var gait = AccessTools.Method($"{type}:PickGait");
        var plays = AccessTools.Method($"{type}:Plays");
        if (update is null || gait is null || plays is null)
        {
            context.LogError($"{type}.Update, PickGait or Plays not found: the original's animations do not play");
            return;
        }

        s_plays = AccessTools.MethodDelegate<Func<AnimationSystem, CharacterAnimation, CharacterCold, int, bool>>(plays);
        context.Harmony.Patch(update, prefix: new HarmonyMethod(typeof(PoseLayer), nameof(UpdatePrefix)));
        context.Harmony.Patch(gait, postfix: new HarmonyMethod(typeof(PoseLayer), nameof(GaitPostfix)));
    }

    /// <summary>Whether the animation library has the track (true before any character has been animated).</summary>
    public static bool HasTrack(string? track) => track is not null && (s_library is not { } library || library.IndexOf(track) >= 0);

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void UpdatePrefix(AnimationSystem __instance, CharacterAnimation a, CharacterCold cold, ulong key, ref CombatView? combatView)
    {
        try
        {
            s_library = __instance.Library;
            if (combatView is { Technique: >= 0 } view && Dual(key) is { } s) combatView = Over(__instance, a, cold, view, key, s);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Failed(e);
        }
    }

    static void GaitPostfix(AnimationSystem __instance, CharacterAnimation a, CharacterCold cold, in AnimationStance stance, int technique, ulong key,
        float speed, List<(int Index, float Weight)> lower, ref int legs)
    {
        try
        {
            if (technique >= 0 || Dual(key) is not { } s) return;

            if (MathF.Abs(speed) >= JogSpeed) Run(__instance, a, cold, key, speed, lower, legs);
            else if (stance.Combat) Stance(__instance, a, cold, s.Tier, lower, legs);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Failed(e);
        }
    }
#pragma warning restore IDE1006

    /// <summary>The dual state of the character the animation key names (its slot and generation), when its own animations are on.</summary>
    static DualFighter? Dual(ulong key)
    {
        if (NitoryuSystem.Latest is not { } system || !system.Session.Settings.Animations) return null;

        int slot = (int)(uint)key;
        if (!system.State.IsDual(slot)) return null;

        var s = system.State[slot];
        return Rng.Key(s.Id) == key ? s : null;
    }

    static Tracks TracksOf(AnimationLibrary library) => Table.GetValue(library, Build);

    static Tracks Build(AnimationLibrary library)
    {
        var tracks = new Tracks();
        if (NitoryuSystem.Latest is { } system)
        {
            foreach (var move in system.Moves.All)
            {
                if (move.Track is null)
                {
                    if (move.Kind == MoveKind.Strike && move.UsesSecondBlade && library.IndexOfTechnique(move.Tech.StringId) is >= 0 and var at
                        && library.IndexOf(Mirror.Prefix + move.Tech.AnimName) is >= 0 and var mirrored)
                        tracks.Mirrored[at] = mirrored;
                    continue;
                }

                int tech = library.IndexOfTechnique(move.Tech.StringId);
                int track = library.IndexOf(move.Track);
                if (tech >= 0 && track >= 0) tracks.Over[tech] = track;
            }
        }

        for (int tier = 1; tier <= 5; tier++) tracks.Stances[tier] = library.IndexOf(Ranks.StanceTrack(tier)!);
        string[] letters = ["", "a", "b", "c"];
        for (int k = 1; k <= 3; k++)
        {
            tracks.Runs[k] = library.IndexOf($"nitoryu run stance {letters[k]}");
            tracks.Jogs[k] = library.IndexOf($"nitoryu jog stance {letters[k]}");
        }

        tracks.Runs[4] = library.IndexOf("nitoryu run trail");
        tracks.Jogs[4] = library.IndexOf("nitoryu jog trail");
        Blocks(library, tracks);
        return tracks;
    }

    static void Blocks(AnimationLibrary library, Tracks tracks)
    {
        tracks.Overhead = library.IndexOf("nitoryu main overhead block");
        tracks.AngledLeft = library.IndexOf("nitoryu angled block left");
        tracks.AngledRight = library.IndexOf("nitoryu angled block right");
        tracks.Scissor = library.IndexOf("nitoryu scissor block");
        tracks.MainBlock = library.IndexOf("nitoryu main block");
        tracks.Offhand = library.IndexOf("nitoryu offhand block");
        if (NitoryuSystem.Latest?.Combat is not { } combat) return;

        foreach (var t in combat.Techniques)
        {
            if (t is { IsBlock: true, IsDodge: false } && library.IndexOfTechnique(t.StringId) is >= 0 and var index)
                tracks.BlockDirection[index] = t.DirectionOf(1);
        }
    }

    /// <summary>
    /// The track laid over a pool move, at the move's progress; a dual fighter's block becomes the original's block by the blow's side (from above
    /// the overhead block, from the left or right the angled one, from the front the scissor X or the one-blade block), and the second blade's
    /// parry its own; the view as it was for anything else.
    /// </summary>
    static CombatView Over(AnimationSystem system, CharacterAnimation a, CharacterCold cold, CombatView view, ulong key, in DualFighter s)
    {
        var tracks = TracksOf(system.Library);
        if (!tracks.Over.TryGetValue(view.Technique, out int track)
            && !(NitoryuMod.Settings.Mirror && tracks.Mirrored.TryGetValue(view.Technique, out track)))
        {
            if (!tracks.BlockDirection.TryGetValue(view.Technique, out int side)) return view;

            track = BlockTrack(tracks, side, key, s);
        }

        if (track < 0 || !Plays(system, a, cold, track)) return view;

        return view with { Technique = track };
    }

    static int BlockTrack(Tracks tracks, int side, ulong key, in DualFighter s)
    {
        if (NitoryuSystem.Latest is { } system && (system.LastTick - s.LastParry) * system.TickSeconds * 1000 < ParryMs && tracks.Offhand >= 0)
            return tracks.Offhand;

        return side switch
        {
            1 => tracks.Overhead,
            2 => tracks.AngledLeft,
            3 => tracks.AngledRight,
            _ => (Rng.Mix(key ^ (ulong)(uint)s.PickSeq) & 1) == 0 ? tracks.Scissor : tracks.MainBlock,
        };
    }

    /// <summary>A dual fighter on the move with its pair out: its run or jog hold (its own of the chosen run stances) over the upper body.</summary>
    static void Run(AnimationSystem system, CharacterAnimation a, CharacterCold cold, ulong key, float speed, List<(int Index, float Weight)> wanted,
        int legs)
    {
        float v = MathF.Abs(speed);
        int[] stances = NitoryuSystem.Latest?.Session.Config.RunStances is { Length: > 0 } list ? list : [4];
        int stance = stances[(int)(Rng.Mix(key) % (ulong)stances.Length)];
        var tracks = TracksOf(system.Library);
        int track = v >= RunSpeed ? tracks.Runs[stance] : tracks.Jogs[stance];
        if (track < 0 || !Plays(system, a, cold, track)) return;

        var definitions = system.Library.Definitions;
        for (int i = legs; i < wanted.Count; i++)
        {
            var d = definitions[wanted[i].Index];
            if (d.Area == AnimationArea.Upper && !d.Technique) wanted[i] = (track, wanted[i].Weight);
        }
    }

    /// <summary>The guard among the wanted clips (past the legs) becomes the rank's stance.</summary>
    static void Stance(AnimationSystem system, CharacterAnimation a, CharacterCold cold, int tier, List<(int Index, float Weight)> wanted, int legs)
    {
        if (tier is < 1 or > 5) return;

        int stance = TracksOf(system.Library).Stances[tier];
        if (stance < 0 || !Plays(system, a, cold, stance)) return;

        var definitions = system.Library.Definitions;
        for (int i = legs; i < wanted.Count; i++)
        {
            var d = definitions[wanted[i].Index];
            if (d.CombatMode == 1 && d.Area == AnimationArea.Upper && !d.Technique) wanted[i] = (stance, wanted[i].Weight);
        }
    }

    static bool Plays(AnimationSystem system, CharacterAnimation a, CharacterCold cold, int clip) => s_plays?.Invoke(system, a, cold, clip) ?? false;

    static void Failed(Exception e)
    {
        if (Interlocked.Exchange(ref s_failed, 1) == 0) s_context?.LogError($"dual animation step failed ({e.GetType().Name}: {e.Message})");
    }
}
