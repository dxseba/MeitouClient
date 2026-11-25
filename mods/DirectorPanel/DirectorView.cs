using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;

namespace Meitou.DirectorPanel;

/// <summary>One body part as the Body tab shows it: its template id and name, flesh and maximum, limb state, and whether it is an arm or leg.</summary>
internal sealed record PartView(string Id, string Name, float Flesh, float Max, LimbState Limb, bool Robotic, bool IsLimb);

/// <summary>The panel's character: everything its tabs show of it, as of the last publish.</summary>
internal sealed record TargetView(
    CharacterId Id, string Name, Vector3 Position, float Yaw, int Faction, int Squad, bool IsPlayer, bool Animal, float Age,
    IReadOnlyList<float> Stats, IReadOnlyList<PartView> Parts, float HungerHud, bool Unconscious, bool Dead, int Bounty,
    IReadOnlyList<string> Packages, bool PanelChangedBehaviour);

/// <summary>
/// What the panel shows, published by the system in a serial step (immutable; read on the interface's thread): the character, the player's
/// faction, relations of the player's faction both ways, the watched pair, the finished research and the player's characters.
/// </summary>
internal sealed record DirectorView(
    TargetView? Target, int PlayerFaction, IReadOnlyList<float> ToPlayer, IReadOnlyList<float> FromPlayer, float PairAB, float PairBA,
    IReadOnlySet<string> Researched, IReadOnlyList<CharacterId> PlayerCharacters, long Money);
