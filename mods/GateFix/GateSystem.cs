using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Movement;

namespace Meitou.GateFix;

/// <summary>
/// The gates the mod holds open. Runs in the serial slow step just before the lock system's, so a gate unlocked here is opened by the engine's
/// walk-through in the same step. Every <see cref="ScanEveryTicks"/> ticks it notes the closed gates of the player's faction that a player
/// character's path runs through near it (unlocking a locked one unless locked gates are respected); every <see cref="CloseEveryTicks"/> ticks
/// it closes the noted gates nobody needs any more and relocks those that were locked. Game time throughout; deterministic (no clock).
/// </summary>
sealed class GateSystem(GateFixSettings settings, Action<string> log) : ITickSystem
{
    /// <summary>Ticks between two looks at the walkers (the engine's own walk-through looks every 3).</summary>
    const int ScanEveryTicks = 3;

    /// <summary>Ticks between two closing checks (the original's once a second).</summary>
    const int CloseEveryTicks = 30;

    /// <summary>How near the leaf a walker must be for its gate to be taken (engine choice: before the engine stops a walker at a locked one, 15).</summary>
    const float NoteReach = 60;

    /// <summary>How many path legs ahead count as going through, and the height and width slack of a leaf (the engine's walk-through values).</summary>
    const int LegsAhead = 3;
    const float DoorHeight = 40, DoorSlack = 6;

    /// <summary>A noted gate: whether it was locked, since when it is held, the last tick a walker went for it, since when it is clear, closing.</summary>
    sealed class Held
    {
        public bool WasLocked;
        public long Since;
        public long LastPass;
        public long ClearSince = -1;
        public bool Closing;
    }

    readonly SortedDictionary<string, Held> _held = new(StringComparer.Ordinal);

    public void SlowWorld(World world)
    {
        if (!settings.Enabled)
        {
            if (_held.Count > 0) ReleaseAll(world);
            return;
        }

        if (world.Tick % ScanEveryTicks == 0) Scan(world);
        if (world.Tick % CloseEveryTicks == 0) CloseStep(world);
    }

    #region Noting

    void Scan(World world)
    {
        var walkers = Walkers(world);
        if (walkers.Count == 0) return;

        var fixtures = world.Fixtures;
        foreach (var door in fixtures.All)
        {
            if (!IsOwnGate(world, door) || fixtures.DoorSpotOf(door) is not { } spot) continue;
            if (!walkers.Any(slot => GoesThrough(world, slot, spot))) continue;

            if (_held.TryGetValue(door.Id, out var held)) held.LastPass = world.Tick;
            else if (door.Motion == DoorMotion.Closed) Take(world, door);
        }
    }

    /// <summary>The player's characters walking a path: alive, awake, free, not animals (animals cannot open gates, as in the original).</summary>
    static List<int> Walkers(World world)
    {
        var table = world.Characters;
        var walkers = new List<int>();
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            ref var hot = ref table.Next[slot];
            if (!hot.Alive || ((MoveFlags)hot.Flags & MoveFlags.HasPath) == 0) continue;
            if (table.Cold(slot) is not { IsPlayer: true, IsImprisoned: false, Medical.Incapacitated: false } cold) continue;
            if (cold.Fighter is not { IsAnimal: true } && cold.CarriedBy.IsNone && cold.Path.Length > 0) walkers.Add(slot);
        }

        return walkers;
    }

    static bool IsOwnGate(World world, Fixture door) =>
        door is { Kind: FixtureKind.Door, IsGate: true, DoorBroken: false } && door.Owner >= 0 && door.Owner == world.Player.Faction;

    static bool GoesThrough(World world, int slot, DoorSpot spot)
    {
        var table = world.Characters;
        ref var hot = ref table.Next[slot];
        if (MathF.Abs(hot.Position.Y - spot.Foot) > DoorHeight || FromLeaf(hot.Position, spot) > NoteReach) return false;

        return Crosses(hot.Position, table.Cold(slot)!.Path, hot.PathCursor, spot);
    }

    /// <summary>Holds a closed gate: a locked one is unlocked (unless locked gates are respected, when it is left alone).</summary>
    void Take(World world, Fixture door)
    {
        bool locked = door.Lock is { EffectivelyLocked: true };
        if (locked && settings.RespectLocked) return;

        _held[door.Id] = new Held { WasLocked = door.Lock is { Locked: true }, Since = world.Tick, LastPass = world.Tick };
        if (locked) world.Fixtures.SetLocked(door, false);
        Note($"opening gate {door.Id} (was locked: {locked})");
    }

    #endregion

    #region Closing

    void CloseStep(World world)
    {
        List<string>? done = null;
        foreach (var (id, held) in _held)
        {
            if (Finished(world, id, held)) (done ??= []).Add(id);
        }

        if (done is not null) foreach (var id in done) _held.Remove(id);
    }

    /// <summary>One held gate's step; true when it is let go.</summary>
    bool Finished(World world, string id, Held held)
    {
        if (world.Fixtures.Find(id) is not { } door || !IsOwnGate(world, door)) return true;

        float seconds = world.TickSeconds;
        switch (door.Motion)
        {
            case DoorMotion.Closed:
                // Closed by the mod, or never opened and nobody went for it during the grace: the lock it had is put back.
                if (!held.Closing && (world.Tick - held.LastPass) * seconds < settings.CloseGrace) return false;
                Relock(world, door, held);
                return true;
            case DoorMotion.Open:
                return OpenStep(world, door, held, seconds);
            default:
                return false;
        }
    }

    bool OpenStep(World world, Fixture door, Held held, float seconds)
    {
        if (!settings.AutoClose) return true;
        if ((world.Tick - held.Since) * seconds > settings.CloseGiveUp)
        {
            Note($"gave up closing gate {door.Id}: left open");
            return true;
        }

        if ((world.Tick - held.LastPass) * seconds < settings.CloseGrace || AllyNear(world, door))
        {
            held.ClearSince = -1;
            return false;
        }

        if (held.ClearSince < 0) held.ClearSince = world.Tick;
        if ((world.Tick - held.ClearSince) * seconds < settings.CloseDelay) return false;

        held.Closing = FixtureRegistry.CloseDoor(door) || held.Closing;
        if (held.Closing) Note($"closing gate {door.Id}");
        return false;
    }

    /// <summary>Whether a living character of the gate's faction stands within the close radius of its leaf.</summary>
    bool AllyNear(World world, Fixture door)
    {
        if (world.Fixtures.DoorSpotOf(door) is not { } spot) return false;

        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { Medical.Dead: false } cold || cold.Faction != door.Owner) continue;
            if (FromLeaf(table.Next[slot].Position, spot) <= settings.CloseRadius) return true;
        }

        return false;
    }

    void Relock(World world, Fixture door, Held held)
    {
        if (held.WasLocked && door.Lock is { Locked: false }) world.Fixtures.SetLocked(door, true);
        Note($"gate {door.Id} closed{(held.WasLocked ? " and locked again" : "")}");
    }

    /// <summary>The mod was switched off: every held gate gets its lock back as it stands (an open one locks when it next closes).</summary>
    void ReleaseAll(World world)
    {
        foreach (var (id, held) in _held)
        {
            if (world.Fixtures.Find(id) is { Motion: DoorMotion.Closed } door) Relock(world, door, held);
        }

        _held.Clear();
    }

    #endregion

    void Note(string line)
    {
        if (settings.Debug) log(line);
    }

    /// <summary>How far a point is from the leaf's closed width in X/Z.</summary>
    static float FromLeaf(Vector3 at, DoorSpot door)
    {
        var d = new Vector2(at.X - door.Centre.X, at.Z - door.Centre.Z);
        var along = new Vector2(-door.Normal.Z, door.Normal.X);
        float t = Math.Clamp(Vector2.Dot(d, along), -door.HalfWidth, door.HalfWidth);
        return (d - along * t).Length();
    }

    /// <summary>Whether one of the next path legs from the walker crosses the leaf's plane within its width plus a little, in X/Z.</summary>
    static bool Crosses(Vector3 at, Vector3[] path, int cursor, DoorSpot door)
    {
        var centre = new Vector2(door.Centre.X, door.Centre.Z);
        var normal = new Vector2(door.Normal.X, door.Normal.Z);
        var a = new Vector2(at.X, at.Z);
        for (int k = Math.Max(cursor, 0); k < path.Length && k < cursor + LegsAhead; k++)
        {
            var b = new Vector2(path[k].X, path[k].Z);
            float da = Vector2.Dot(a - centre, normal), db = Vector2.Dot(b - centre, normal);
            if ((da <= 0) != (db <= 0) && Vector2.Distance(a + (b - a) * (da / (da - db)), centre) <= door.HalfWidth + DoorSlack) return true;

            a = b;
        }

        return false;
    }
}
