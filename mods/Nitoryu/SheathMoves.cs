using Meitou.Data.Gameplay;
using Meitou.Simulation;
using Meitou.Simulation.Movement;

namespace Meitou.Nitoryu;

/// <summary>
/// The original's one-shot tracks of the pair (README "My animations" 1 to 3) through the engine's mod moves (<c>AddCombatMove</c>, played only
/// while the game plays nothing of its own): the dual draw (<c>nitoryu dual draw</c>, <c>... hip</c> with the second blade on the hip) as the
/// pair comes out, the sheathe (<c>nitoryu dual sheathe</c> / <c>... hip</c>) as it goes home, and after a fight that just ended the blood flick
/// (<c>nitoryu chiburi</c> / <c>... hip</c>) before the sheathe. The game draws and sheathes at once; only the look takes the clip's length.
/// Answers from the state the serial update settled (the ticks of the draw and sheathe), so it is a pure read.
/// </summary>
static class SheathMoves
{
    public static CombatMove? Source(World world, CharacterId id, long tick)
    {
        if (!NitoryuMod.Settings.Animations || NitoryuSystem.Of(world) is not { } system || (uint)id.Slot >= (uint)system.State.Count) return null;

        var s = system.State[id.Slot];
        if (s.Id != id || system.Lengths is not { } lengths) return null;

        string hip = s.OffHip ? " hip" : "";
        if (s.PairOut) return Clip(world, lengths, "nitoryu dual draw" + hip, tick - s.DrawTick);
        if (!s.Chiburi) return Clip(world, lengths, "nitoryu dual sheathe" + hip, tick - s.SheatheTick);

        string flick = "nitoryu chiburi" + hip;
        float first = lengths.Of(flick);
        long flickTicks = (long)MathF.Ceiling(first / world.TickSeconds);
        long since = tick - s.SheatheTick;
        return since < flickTicks ? Clip(world, lengths, flick, since) : Clip(world, lengths, "nitoryu dual sheathe" + hip, since - flickTicks);
    }

    static CombatMove? Clip(World world, AnimationLengths lengths, string clip, long ticks)
    {
        if (ticks < 0) return null;

        float length = lengths.Of(clip);
        float progress = ticks * world.TickSeconds / length;
        return progress < 1 ? CombatMove.Clip(clip, progress) : null;
    }
}
