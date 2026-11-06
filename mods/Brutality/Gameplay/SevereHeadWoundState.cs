using Meitou.Data.Characters;
using Meitou.Simulation.Bodies;
using Meitou.Simulation;
using System.Numerics;
using Meitou.Simulation.Combat;

namespace Meitou.Brutality;

public enum SevereHeadWoundKind { None, JawLost, Scalp, BrokenSkull }

/// <summary>The fixed wound frame in the body's bind space, independent of its later world position or pose.</summary>
public readonly record struct HeadWoundPlacement(Vector3 Point, Vector3 Normal, Vector3 Tangent, float LengthScale = 1, float WidthScale = 1)
{
    public bool IsValid
    {
        get
        {
            static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
            if (!Finite(Point) || !Finite(Normal) || !Finite(Tangent)) return false;
            if (!float.IsFinite(LengthScale) || !float.IsFinite(WidthScale) || LengthScale is < 0.5f or > 2 || WidthScale is < 0.5f or > 2) return false;
            float normal = Normal.LengthSquared(), tangent = Tangent.LengthSquared();
            if (!float.IsFinite(normal) || !float.IsFinite(tangent) || normal < 1e-8f || tangent < 1e-8f) return false;
            // A parallel tangent cannot define the surface frame.
            float cross = Vector3.Cross(Normal / MathF.Sqrt(normal), Tangent / MathF.Sqrt(tangent)).LengthSquared();
            return float.IsFinite(cross) && cross > 1e-6f;
        }
    }
}

/// <summary>The severe visual head state, including heavy blows before an opening exists. Profile 0..5 selects the blade axis and force size.</summary>
public readonly record struct SevereHeadWoundState(int HeavyHits, SevereHeadWoundKind Kind = SevereHeadWoundKind.None, int Profile = 0, HeadWoundPlacement? Placement = null, CombatWoundMechanism Mechanism = CombatWoundMechanism.Slash)
{
    public bool IsValid => HeavyHits >= 0 && Kind is >= SevereHeadWoundKind.None and <= SevereHeadWoundKind.BrokenSkull
        && Profile is >= 0 and < 6 && (Kind is SevereHeadWoundKind.Scalp or SevereHeadWoundKind.BrokenSkull || Profile == 0)
        && Mechanism is >= CombatWoundMechanism.Slash and <= CombatWoundMechanism.Crush
        && (Placement is not { } placement || placement.IsValid);

    /// <summary>The renderer's jaw bit 6, scalp/skull bits 7/8, profile bits 9..11 and impact mechanism bits 12..13.</summary>
    public uint RendererFlags => Kind switch
    {
        SevereHeadWoundKind.JawLost => 1u << 6,
        SevereHeadWoundKind.Scalp => 1u << 7 | (uint)Profile << 9 | (uint)Mechanism << 12,
        SevereHeadWoundKind.BrokenSkull => 3u << 7 | (uint)Profile << 9 | (uint)Mechanism << 12,
        _ => 0,
    };

    public static SevereHeadWoundState FromRendererFlags(int heavyHits, uint flags, HeadWoundPlacement? placement = null)
    {
        var kind = (flags & (1u << 6)) != 0 ? SevereHeadWoundKind.JawLost
            : (flags >> 7 & 3) >= 2 ? SevereHeadWoundKind.BrokenSkull
            : (flags & (1u << 7)) != 0 ? SevereHeadWoundKind.Scalp : SevereHeadWoundKind.None;
        int profile = kind is SevereHeadWoundKind.Scalp or SevereHeadWoundKind.BrokenSkull ? (int)(flags >> 9 & 7) : 0;
        var mechanism = (CombatWoundMechanism)(flags >> 12 & 3);
        if (mechanism == CombatWoundMechanism.Unspecified) mechanism = CombatWoundMechanism.Slash;
        return new(heavyHits, kind, profile, placement, mechanism);
    }
}
