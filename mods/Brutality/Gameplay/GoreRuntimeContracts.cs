using System.Numerics;
using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.Brutality;

public static class GorePartIds { public const string Stomach = "100-gamedata.quack"; }

// Mod-owned domains replace the fork's extra native RngPurpose values without sharing a vanilla random stream.
public static class GoreRandom
{
    public static readonly RngPurpose HeadCutVisualPurpose = Purpose("meitou.brutality.head-cut");
    public static readonly RngPurpose TorsoVisualPurpose = Purpose("meitou.brutality.torso-cut");
    public static readonly RngPurpose HeadWoundVisualPurpose = Purpose("meitou.brutality.head-wound");
    public static readonly RngPurpose ChestHoleVisualPurpose = Purpose("meitou.brutality.chest-hole");
    static RngPurpose Purpose(string name) => (RngPurpose)(uint)(Rng.StableHash(name) | 0x80000000UL);
}

public readonly record struct GoreHitKey(long Tick, CombatHitSource Source, CharacterId Attacker, CharacterId Victim, int Sequence, int Blow)
{
    public static GoreHitKey From(in CombatHit hit) => new(hit.Tick, hit.Source, hit.Attacker, hit.Victim, hit.Sequence, hit.Blow);
}

/// <summary>Exact data supplied by a before-damage bridge; null fields remain explicitly unsupported rather than inferred after a knockout or severance.</summary>
public sealed record GoreHitBridge
{
    public CombatWoundMechanism Mechanism { get; init; }
    public int WeaponCategory { get; init; } = -1;
    public float? UsefulRemainingBeforeHit { get; init; }
    public float? ChallengeFactor { get; init; }
    public float? PreArmourCut { get; init; }
    public float? PartMaximumBeforeHit { get; init; }
    public bool? OrganicBeforeHit { get; init; }
    public bool? HeadSeveranceAccepted { get; init; }
    internal HeadCutState? MartialArtsBurst { get; init; }
    public bool? AllowsMedicalSeverance { get; init; }
    /// <summary>The native bridge has evaluated the opposite limb, including a correctly blocked follow-through.</summary>
    public bool CounterpartResolved { get; init; }
    public DamagePacket? UnarmouredPacket { get; init; }
    public IReadOnlyList<int> SeveredLimbSlots { get; init; } = [];
}

[Flags]
public enum GoreIntegrationGap { None = 0, PreHitContext = 1, HeadMedicalGate = 2, CounterpartDamage = 4 }
public enum GoreCutKind { SevereHead, Head, Torso, Limb, PairedLimbAttempt, ChestHole }

/// <summary>A mod-owned immutable request. PairedLimbAttempt requires the native damage bridge to verify and commit the second loss; it is not itself an amputation.</summary>
public readonly record struct GoreCutRequest(long Sequence, long Tick, CharacterId Victim, GoreCutKind Kind,
    SevereHeadWoundState? SevereHead = null, HeadCutState? Head = null, TorsoBisectionState? Torso = null,
    LimbCutState? Limb = null, int LimbSlot = -1, GoreHitBridge? Bridge = null)
{
    public CombatHit? Hit { get; init; }
    public ChestHoleState? ChestHole { get; init; }
}

public sealed record BrutalityActorView(CharacterId Character, float Experience, float Level, float Progress,
    SevereHeadWoundState SevereHead, HeadCutState? Head, TorsoBisectionState? Torso, IReadOnlyList<LimbCutState> Limbs)
{
    public BodyWoundState Wounds { get; init; } = BodyWoundState.Empty;
    public ChestHoleState? ChestHole { get; init; }
}

/// <summary>Persistence uses an externally resolved durable character key, never a runtime slot/generation.</summary>
public sealed record BrutalityDurableState(string Key, float Experience, SevereHeadWoundState SevereHead,
    HeadCutState? Head, TorsoBisectionState? Torso, LimbCutState[] Limbs)
{
    public BodyWoundState Wounds { get; init; } = BodyWoundState.Empty;
    public ChestHoleState? ChestHole { get; init; }
}
