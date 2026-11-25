using System.Numerics;
using Meitou.Simulation;

namespace Meitou.ForceAwakening;

/// <summary>The player's cast: <paramref name="Caster"/> uses bar slot <paramref name="Slot"/> (0-4) on a character and/or a ground point.</summary>
public sealed record CastOrder(CharacterId Caster, int Slot, CharacterId Target, Vector3? Point) : SimCommand;

/// <summary>The grimoire: put spell <paramref name="Spell"/> on bar slot <paramref name="Slot"/> of <paramref name="Who"/> (0: empty the slot).</summary>
public sealed record BarOrder(CharacterId Who, int Slot, int Spell) : SimCommand;

/// <summary>The grimoire's auto-cast tick of a spell.</summary>
public sealed record AutoOrder(CharacterId Who, int Spell, bool On) : SimCommand;

/// <summary>The grimoire's specialization (1 or 2).</summary>
public sealed record TreeOrder(CharacterId Who, int Tree) : SimCommand;

/// <summary>The grimoire's Brainwash button: the next faction in its list.</summary>
public sealed record BrainwashFactionOrder(CharacterId Who) : SimCommand;

/// <summary>The grimoire's confirmed Renounce.</summary>
public sealed record RenounceOrder(CharacterId Who) : SimCommand;

/// <summary>
/// One character's path: the original keeps it per character (one path each), its five bar skills, the auto-cast ticks, its specialization and
/// its pool (Concentration, Fury or Focus). Saved by lasting id.
/// </summary>
sealed class Adept
{
    public ForcePath Path;
    public int Tree;
    public readonly int[] Bar = new int[5];
    public readonly SortedSet<int> Auto = [];
    public float Pool;

    /// <summary>The grimoire's Brainwash choice: 0 the caster's own faction, n the n-th <c>brainwash_faction</c>.</summary>
    public int Faction;

    /// <summary>Not saved: the tick each spell is ready again, the last tick of a blow given or taken, the next auto-cast tick.</summary>
    public readonly Dictionary<int, long> Ready = [];
    public long LastFight;
    public long NextAuto;

    public void Write(BinaryWriter w)
    {
        w.Write((byte)Path);
        w.Write((byte)Tree);
        foreach (int s in Bar) w.Write(s);
        w.Write(Auto.Count);
        foreach (int s in Auto) w.Write(s);
        w.Write(Pool);
        w.Write(Faction);
    }

    public static Adept Read(BinaryReader r, int version)
    {
        var a = new Adept { Path = (ForcePath)r.ReadByte(), Tree = r.ReadByte() };
        for (int i = 0; i < a.Bar.Length; i++) a.Bar[i] = r.ReadInt32();
        for (int n = r.ReadInt32(); n > 0; n--) a.Auto.Add(r.ReadInt32());
        a.Pool = r.ReadSingle();
        if (version >= 2) a.Faction = r.ReadInt32();
        return a;
    }
}

/// <summary>What a timed effect does while it lasts (not saved: a load ends them, as the port's choice).</summary>
enum Lasting : byte
{
    Deflect,
    DarkRage,
    BattleMeditation,
    Hunt,
    Command,
    MindTrick,
    Choke,
    Flame,
    Detonator,
    Stun,
    Brainwash,
}

/// <summary>A running effect: on <see cref="Who"/> (or at <see cref="Point"/>), from <see cref="By"/>, until <see cref="End"/>.</summary>
sealed class Timed
{
    public required Lasting Kind;
    public CharacterId Who;
    public CharacterId By;
    public Vector3 Point;
    public Vector3 Aim;
    public long End;
    public long NextStep;
    public float Power;
    public int Faction = -1;
}

/// <summary>A character's bar as the interface shows it (published by the system, read on the host's thread).</summary>
sealed record BarView(CharacterId Id, string Name, ForcePath Path, int Tree, float Level, float Wisdom, float Pool, float PoolMax,
    IReadOnlyList<SlotView> Slots, IReadOnlyList<SpellView> Known, IReadOnlySet<int> Auto, string BrainwashFaction);

/// <summary>A clip a character plays for a skill (a Sith's strike, a cast's pose), from tick <see cref="Start"/> to <see cref="End"/>.</summary>
readonly record struct SkillMove(string Clip, long Start, long End);

/// <summary>A bar slot: its spell (null: empty), seconds of cooldown left and whether it can be cast now.</summary>
sealed record SlotView(Spell? Spell, float Cooldown, bool Usable);

/// <summary>A spell of the path in the grimoire: learned (level reached) or not.</summary>
sealed record SpellView(Spell Spell, bool Learned);
