using System.Numerics;
using Meitou.Simulation.Bodies;


namespace Meitou.Brutality;

/// <summary>Torso regions; <see cref="Back"/> collects torso hits from behind. Ported from Kenshi Gore Lab's <c>TorsoWounds</c>.</summary>
public enum WoundRegion { Unknown = -1, Chest, Abdomen, Back }

/// <summary>One wound. Segments 0–7 are the arm and leg sections, 8 is the scalp, 9 is the torso, and 10–15 are hands and face.</summary>
public readonly record struct Wound(int Segment, float T, Vector3 Side, WoundShape Shape, float Dose, int Hits, int Age, float Size = 1, long Born = 0);

/// <summary>Additional placement sites within the engine's existing arm and head hit regions.</summary>
internal static class WoundSegments
{
    internal const int LeftHand = 10, RightHand = 11, Cheek = 12, Jaw = 13, LeftDistalHand = 14, RightDistalHand = 15;

    internal static bool IsHand(int segment) => segment is LeftHand or RightHand || IsDistalHand(segment);
    internal static bool IsDistalHand(int segment) => segment is LeftDistalHand or RightDistalHand;
    internal static bool IsHead(int segment) => segment is 8 or Cheek or Jaw;
    internal static bool IsValid(int segment) => segment is >= 0 and <= RightDistalHand;
}

/// <summary>The visual wounds of one character: one per torso region, and up to <see cref="LimbSlots"/> on the limbs, a near hit deepening a wound and the oldest giving way.</summary>
public sealed partial class WoundBody
{
    public const int LimbSlots = 96;
    readonly Wound?[] torso = new Wound?[3];
    readonly Wound?[] limbs = new Wound?[LimbSlots];
    BodyWoundState? snapshot;
    int clock;

    public ReadOnlySpan<Wound?> Torso => torso;
    public ReadOnlySpan<Wound?> Limbs => limbs;

    /// <summary>A deep belly wound, the condition for exposed guts.</summary>
    public bool DeepAbdomen => BellyHits >= 10;
    int bellyHits;
    public int BellyHits { get => bellyHits; set { if (bellyHits != value) snapshot = null; bellyHits = value; } }

    /// <summary>Closes every wound by <paramref name="dose"/>, shallower as it heals; a closed one goes. True while any is left.</summary>
    public bool Heal(float dose)
    {
        snapshot = null;
        bool left = false;
        foreach (var wounds in new[] { torso, limbs })
            for (int i = 0; i < wounds.Length; i++)
                if (wounds[i] is { } w)
                {
                    float rest = w.Dose - dose;
                    wounds[i] = rest <= 0 ? null : w with { Dose = rest, Shape = w.Shape with { Depth = Math.Clamp(rest / 1.2f, 0, 1) } };
                    left |= rest > 0;
                }
        return left;
    }

    internal void Restore(int region, in Wound wound)
    {
        snapshot = null;
        clock = Math.Max(clock, wound.Age);
        if (region >= 0) torso[region] = wound;
        else if (Array.FindIndex(limbs, w => w is null) is >= 0 and var free) limbs[free] = wound;
    }

    public bool ApplyTorso(WoundRegion region, WoundInput input)
    {
        if (region == WoundRegion.Unknown) return false;
        var old = torso[(int)region];
        var r = WoundPolicy.Evaluate(input with { LocalDose = old?.Dose ?? 0, LocalHits = old?.Hits ?? 0 });
        if (!r.Accepted) return false;
        snapshot = null;
        torso[(int)region] = new Wound(-1, 0.5f, default, r.Shape, r.Dose, r.Hits, ++clock);
        return true;
    }

    /// <summary>The tick of the hit being applied.</summary>
    public long Now { get; set; }

    public bool ApplyLimb(int segment, float t, Vector3 side, float tilt, WoundInput input)
    {
        if (!WoundSegments.IsValid(segment) || !float.IsFinite(t)) return false;
        int target = -1;
        for (int i = 0; i < LimbSlots && target < 0; i++)
            if (limbs[i] is { } w && w.Segment == segment && MathF.Abs(w.T - t) < 0.03f && Vector3.Dot(w.Side, side) > 0.98f) target = i;
        bool merge = target >= 0;
        if (!merge) target = Array.FindIndex(limbs, w => w is null);
        if (target < 0)
        {
            target = 0;
            for (int i = 1; i < LimbSlots; i++) if (limbs[i]!.Value.Age < limbs[target]!.Value.Age) target = i;
        }
        var old = merge ? limbs[target] : null;
        var r = WoundPolicy.Evaluate(input with { LocalDose = old?.Dose ?? 0, LocalHits = old?.Hits ?? 0 });
        if (!r.Accepted) return false;
        // A merged wound keeps its drawn axis; a new one gets a tilt so limb cuts do not all lie on four axes.
        var shape = r.Shape;
        if (old is { } o) shape = shape with { Cosine = o.Shape.Cosine, Sine = o.Shape.Sine };
        else
        {
            float c = MathF.Cos(tilt), s = MathF.Sin(tilt);
            shape = shape with { Cosine = shape.Cosine * c - shape.Sine * s, Sine = shape.Cosine * s + shape.Sine * c };
        }
        snapshot = null;
        limbs[target] = new Wound(segment, old?.T ?? Math.Clamp(t, 0.25f, 0.75f), old?.Side ?? side, shape, r.Dose, r.Hits, ++clock, MathF.Max(old?.Size ?? 0, input.Size), old?.Born ?? Now);
        return true;
    }
}
