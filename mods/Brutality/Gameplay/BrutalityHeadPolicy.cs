using Meitou.Data.Characters;
using Meitou.Simulation.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.Brutality;

/// <summary>Attacker skill gates authored head openings; delivered force and the saved opening retain their own meaning.</summary>
public static class BrutalityHeadPolicy
{
    public const int HeavyHitsToOpen = 4;
    public const float HeavyForce = 15, SevereForce = 35;

    /// <param name="forceForm">Explicit visual fixtures only: permits the requested form without changing its real force or footprint.</param>
    public static SevereHeadWoundState Apply(in SevereHeadWoundState before, float brutality, float force,
        SevereHeadWoundKind requested, CombatWoundMechanism mechanism, int openingProfile = 0,
        HeadWoundPlacement? openingPlacement = null, bool forceForm = false)
    {
        if (!before.IsValid || !float.IsFinite(brutality) || !float.IsFinite(force) || force < HeavyForce
            || requested is < SevereHeadWoundKind.JawLost or > SevereHeadWoundKind.BrokenSkull) return before;
        int tier = BrutalityPolicy.HeadTier(brutality);
        if (tier == 0) return before;

        var head = before with { HeavyHits = before.HeavyHits < int.MaxValue ? before.HeavyHits + 1 : int.MaxValue };
        var allowed = forceForm || tier >= 2 && force >= SevereForce ? requested : SevereHeadWoundKind.Scalp;
        if (head.HeavyHits >= HeavyHitsToOpen)
        {
            if (head.Kind == SevereHeadWoundKind.None)
                head = Open(head, allowed, mechanism, openingProfile, openingPlacement);
            else if (head.Kind == SevereHeadWoundKind.Scalp && allowed != SevereHeadWoundKind.Scalp)
                head = head with { Kind = allowed, Profile = allowed == SevereHeadWoundKind.JawLost ? 0 : head.Profile,
                    Placement = allowed == SevereHeadWoundKind.JawLost ? null : head.Placement };
        }

        bool canEnlarge = head.Kind == SevereHeadWoundKind.Scalp || head.Kind == SevereHeadWoundKind.BrokenSkull && tier >= 2;
        return canEnlarge ? Enlarge(head, force) : head;
    }

    static SevereHeadWoundState Open(SevereHeadWoundState head, SevereHeadWoundKind kind, CombatWoundMechanism mechanism,
        int profile, HeadWoundPlacement? placement) => head with
    {
        Kind = kind,
        Mechanism = mechanism is >= CombatWoundMechanism.Slash and <= CombatWoundMechanism.Crush ? mechanism : CombatWoundMechanism.Slash,
        Profile = kind == SevereHeadWoundKind.JawLost ? 0 : Math.Clamp(profile, 0, 5),
        Placement = kind != SevereHeadWoundKind.JawLost && placement is { IsValid: true } ? placement : null,
    };

    static SevereHeadWoundState Enlarge(SevereHeadWoundState head, float force)
    {
        if (head.Profile >= 3) return head;
        if (head.Placement is { } placement) return head with { Placement = HeadWoundPlacementPolicy.Enlarge(placement, force) };
        return force >= SevereForce ? head with { Profile = head.Profile + 3 } : head;
    }
}
