using System.Numerics;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;

namespace Meitou.DirectorPanel;

/// <summary>The panel's commands: applied by <see cref="DirectorSystem"/> at once while paused too (the original acts on the game's thread at once).</summary>
public abstract record DirectorCommand : SimCommand, IPausedCommand;

/// <summary>The character the panel works with (its view is published), and the faction pair the Relations tab shows.</summary>
public sealed record Watch(CharacterId Target, int PairA, int PairB) : DirectorCommand;

public sealed record AddMoney(long Amount) : DirectorCommand;

public sealed record SetStats(IReadOnlyList<CharacterId> Characters, IReadOnlyList<StatsEnumerated> Stats, float Value) : DirectorCommand;

/// <summary>NPC Spawn: <see cref="Count"/> of <see cref="Record"/> <see cref="Distance"/> metres in front of <see cref="Near"/>.</summary>
public sealed record Spawn(string Record, int Count, float Distance, CharacterId Near, int Faction, bool JoinSquad, bool Hostile) : DirectorCommand;

/// <summary>Relation of <see cref="A"/> to <see cref="B"/> (and back when <see cref="Both"/>).</summary>
public sealed record SetRelation(int A, int B, float Value, bool Both) : DirectorCommand;

public sealed record Learn(string Research) : DirectorCommand;

/// <summary>The character's squad runs <see cref="Package"/> alone; null puts back the packages it had before the panel's first change.</summary>
public sealed record SetBehaviour(CharacterId Character, string? Package, bool Separate = false) : DirectorCommand;

public sealed record Strike(CharacterId Character, string Part, float Cut, float Blunt) : DirectorCommand;

public enum BodyState
{
    KnockOut,
    Coma,
    NearDeath,
    Heal,
}

public sealed record SetBodyState(CharacterId Character, BodyState State, float Seconds = 0) : DirectorCommand;

public sealed record SetHunger(CharacterId Character, float Hud) : DirectorCommand;

public sealed record SetLimb(CharacterId Character, string Part, bool Sever) : DirectorCommand;

/// <summary>Blood on the ground round the character: <see cref="Times"/> splashes of <see cref="Power"/>, flung from <see cref="Direction"/>.</summary>
public sealed record Splatter(CharacterId Character, int Direction, float Power, int Times, int Cut = 0) : DirectorCommand;

public sealed record Teleport(IReadOnlyList<CharacterId> Characters, Vector3 At) : DirectorCommand;

public sealed record SetBounty(CharacterId Character, int Faction, int Amount) : DirectorCommand;

public sealed record ClearBounties(CharacterId Character) : DirectorCommand;

public sealed record HealSquads : DirectorCommand;

public sealed record SetAnimalAge(CharacterId Character, float Age) : DirectorCommand;

/// <summary>Opens <see cref="Target"/>'s inventory in the loot window, <see cref="Looter"/> holding it (walking there first); or closes it.</summary>
public sealed record OpenInventory(CharacterId Looter, CharacterId Target, bool Close) : DirectorCommand;

/// <summary>"Moving items does not count as stealing".</summary>
public sealed record SetNoTheft(bool On) : DirectorCommand;

public enum Stance
{
    Draw,
    Sheathe,
    LeaveCombat,
}

public sealed record SetStance(CharacterId Character, Stance Stance) : DirectorCommand;

/// <summary>"Keep weapon ready": drawn again each tick while on.</summary>
public sealed record KeepReady(CharacterId Character, bool On) : DirectorCommand;

/// <summary>The character's idle pose (an ANIMATION record name, as the editor's Idle stance slider).</summary>
public sealed record SetPose(CharacterId Character, string Pose) : DirectorCommand;

/// <summary>"Paint worn items": every worn item takes the COLOR_DATA <see cref="Scheme"/>.</summary>
public sealed record PaintWorn(CharacterId Character, string Scheme) : DirectorCommand;

/// <summary>"Bloodbath" mode: more blood on the ground from every cutting hit.</summary>
public sealed record SetBloodbath(bool On) : DirectorCommand;
