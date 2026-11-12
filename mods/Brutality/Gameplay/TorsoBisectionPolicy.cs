using Meitou.Data.Characters;
using Meitou.Simulation.Bodies;
using Meitou.Simulation;
using System.Numerics;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation.Combat;

namespace Meitou.Brutality;

public enum TorsoBisectionStyle { Auto = 0, Transverse = 1, Vertical = 2, Diagonal = 3, Oblique = 4, NearVertical = 5 }

/// <summary>The delivered chest or stomach hit and the victim's state before it landed.</summary>
public readonly record struct TorsoBisectionInput(CombatHitSource Source, CombatWoundMechanism Mechanism,
    BodyPartTemplate Part, int CutDirection, float Cut, float Maximum, bool AliveBeforeHit = true,
    bool AlreadyBisected = false, bool Robot = false, bool Animal = false)
{
    public float Brutality { get; init; }
    public bool Lethal { get; init; }
}

/// <summary>Meitou's skill-gated cleave of an already lethal torso hit; its roll reads no existing random stream.</summary>
public static class TorsoBisectionPolicy
{
    public const float MinimumCut = 70, MaximumHpFraction = 0.75f, BaseChance = 0.10f, MaximumChance = 0.95f;
    public const float MinimumHeight = 11.5f, MaximumHeight = 13.5f, MaximumTiltDegrees = 30, AzimuthVariationDegrees = 45;
    public const float MaximumSlopeX = 0.55f, MaximumSlopeZ = 0.24f;
    public const float StomachMinimumHeight = MinimumHeight, StomachMaximumHeight = 12.25f;
    // Conservative junction bounds measured on installed human/Shek male/female and worker/soldier/prince meshes.
    public const float LegJunctionCeiling = 11.35f, ShoulderJunctionFloor = 14.10f;
    public const float JunctionHalfWidth = 3, JunctionHalfDepth = 1.51f;
    public const float FullBodyAzimuthDegrees = 36, FullBodyOffsetX = 0.70f, FullBodyOffsetZ = 0.40f;
    public const float LongDiagonalMinimumTiltDegrees = 8, LongDiagonalMaximumTiltDegrees = 18;
    public const float ObliqueDiagonalMinimumTiltDegrees = 19, ObliqueDiagonalMaximumTiltDegrees = 38;
    public const float LongDiagonalPivotMinimum = 10.2f, LongDiagonalPivotMaximum = 11.5f, LongDiagonalOffsetScale = 0.8f;
    public const float ObliqueDiagonalPivotMinimum = 11.8f, ObliqueDiagonalPivotMaximum = 14, ObliqueDiagonalOffsetScale = 1.1f;
    public const float ObliqueMinimumTiltDegrees = 40, ObliqueMaximumTiltDegrees = 60;
    public const float ObliquePivotMinimum = 12, ObliquePivotMaximum = 13.2f, ObliqueOffsetScale = 0.25f;
    const float RadiansPerDegree = MathF.PI / 180;

    public static float Chance(in TorsoBisectionInput input)
    {
        if (!input.AliveBeforeHit || !input.Lethal || input.AlreadyBisected || input.Animal
            || input.Source != CombatHitSource.Melee || input.Mechanism != CombatWoundMechanism.Slash
            || input.CutDirection is not (1 or 2 or 3 or 7 or 9)
            || input.Part is not { Type: BodyPartType.Torso, StringId: ArmourData.ChestPartId or GorePartIds.Stomach }
            || !float.IsFinite(input.Cut) || !float.IsFinite(input.Maximum) || input.Maximum <= 0) return 0;
        float threshold = MathF.Max(MinimumCut, MaximumHpFraction * input.Maximum);
        if (input.Cut < threshold) return 0;
        return BrutalityPolicy.BisectionChance(input.Brutality);
    }

    /// <summary>True for an eligible hit and a roll in [0, chance); callers may supply a fixed roll for a deterministic scene.</summary>
    public static bool ShouldBisect(in TorsoBisectionInput input, float roll) =>
        float.IsFinite(roll) && roll >= 0 && roll < Chance(input);

    /// <summary>The opening blow's own visual hash; it does not read or change the chance roll or any existing combat key.</summary>
    public static ulong VisualSeedFor(ulong worldSeed, CharacterId victim, CharacterId attacker, ulong counter) =>
        Rng.Hash(worldSeed, Rng.Key(victim) ^ Rng.Mix(Rng.Key(attacker)), GoreRandom.TorsoVisualPurpose, counter);

    public static bool SupportsStyle(int cutDirection, TorsoBisectionStyle style) => style switch
    {
        TorsoBisectionStyle.Auto => cutDirection is 1 or 2 or 3 or 7 or 9,
        TorsoBisectionStyle.Transverse => cutDirection is 2 or 3 or 9,
        TorsoBisectionStyle.Vertical or TorsoBisectionStyle.NearVertical or TorsoBisectionStyle.Diagonal => cutDirection is 1 or 7,
        TorsoBisectionStyle.Oblique => cutDirection is 1 or 2 or 3 or 7 or 9,
        _ => false,
    };

    /// <summary>Eligible blows may cleave across the torso; other sideways cuts retain the transverse fit and downward cuts can continue lengthwise.</summary>
    public static TorsoBisectionState StateForDirection(int cutDirection, ulong visualSeed = 0, Vector2 travel = default,
        float victimYaw = 0, TorsoBisectionStyle style = TorsoBisectionStyle.Auto) =>
        StateForDirection(cutDirection, visualSeed, travel, victimYaw, style, stomach: false, lowStrike: false);

    /// <summary>The struck part and low-strike flag guide the cut's height without changing its chance or visual hash.</summary>
    public static TorsoBisectionState StateForHit(BodyPartTemplate part, int cutDirection, ulong visualSeed = 0,
        Vector2 travel = default, float victimYaw = 0, TorsoBisectionStyle style = TorsoBisectionStyle.Auto, bool lowStrike = false) =>
        part is { Type: BodyPartType.Torso, StringId: ArmourData.ChestPartId or GorePartIds.Stomach }
            ? StateForDirection(cutDirection, visualSeed, travel, victimYaw, style, part.StringId == GorePartIds.Stomach, lowStrike)
            : default;

    static TorsoBisectionState StateForDirection(int cutDirection, ulong visualSeed, Vector2 travel,
        float victimYaw, TorsoBisectionStyle style, bool stomach, bool lowStrike)
    {
        if (!SupportsStyle(cutDirection, style)) return default;
        ulong styleBits = Rng.Mix(visualSeed ^ 0x4C4F4E4743555453UL);
        if (style == TorsoBisectionStyle.Auto)
        {
            float choice = Rng.Float(styleBits);
            if (cutDirection is 1 or 7)
                style = choice switch
                {
                    < 1f / 6 => TorsoBisectionStyle.Vertical,
                    < 1f / 3 => TorsoBisectionStyle.NearVertical,
                    < 2f / 3 => TorsoBisectionStyle.Diagonal,
                    _ => TorsoBisectionStyle.Oblique,
                };
            else style = choice < 1f / 3 ? TorsoBisectionStyle.Oblique : TorsoBisectionStyle.Transverse;
        }
        if (style == TorsoBisectionStyle.Transverse)
            return new(PlaneForDirection(cutDirection, visualSeed, travel, victimYaw, stomach, lowStrike));

        ulong offsetBits = Rng.Mix(styleBits), angleBits = Rng.Mix(offsetBits), pivotBits = Rng.Mix(angleBits), slopeBits = Rng.Mix(pivotBits);
        var local = LocalTravel(travel, victimYaw);
        var across = new Vector2(local.Y, -local.X);
        float azimuth = (Rng.Float(angleBits) * 2 - 1) * FullBodyAzimuthDegrees * RadiansPerDegree;
        var horizontal = Rotate(across, azimuth);
        // Allow more lateral displacement than depth, without preferring any incoming direction.
        float radius = 1 / MathF.Sqrt(horizontal.X * horizontal.X / (FullBodyOffsetX * FullBodyOffsetX)
            + horizontal.Y * horizontal.Y / (FullBodyOffsetZ * FullBodyOffsetZ));
        float pivot = 0, tilt = 0, offsetScale = 1;
        float pivotRoll = HeightRoll(pivotBits, lowStrike);
        float tiltRoll = Rng.Float(slopeBits);
        if (style == TorsoBisectionStyle.NearVertical)
        {
            pivot = stomach ? Mix(StomachMinimumHeight, StomachMaximumHeight, pivotRoll) : Mix(12, MaximumHeight, pivotRoll);
            tilt = Mix(2, 7, tiltRoll);
            offsetScale = .8f;
        }
        else if (style == TorsoBisectionStyle.Diagonal)
        {
            bool oblique = Rng.Float(Rng.Mix(slopeBits ^ 0x4F424C4951554544UL)) >= 0.5f;
            float minimumTilt = oblique ? ObliqueDiagonalMinimumTiltDegrees : LongDiagonalMinimumTiltDegrees;
            float maximumTilt = oblique ? ObliqueDiagonalMaximumTiltDegrees : LongDiagonalMaximumTiltDegrees;
            float minimumPivot = oblique ? ObliqueDiagonalPivotMinimum : LongDiagonalPivotMinimum;
            float maximumPivot = oblique ? ObliqueDiagonalPivotMaximum : LongDiagonalPivotMaximum;
            if (stomach)
                (minimumPivot, maximumPivot) = oblique ? (StomachMinimumHeight, StomachMaximumHeight)
                    : (minimumPivot - .2f, maximumPivot - .2f);
            pivot = Mix(minimumPivot, maximumPivot, pivotRoll);
            tilt = Mix(minimumTilt, maximumTilt, tiltRoll);
            offsetScale = oblique ? ObliqueDiagonalOffsetScale : LongDiagonalOffsetScale;
        }
        else if (style == TorsoBisectionStyle.Oblique)
        {
            float minimumPivot = stomach ? StomachMinimumHeight : ObliquePivotMinimum;
            float maximumPivot = stomach ? StomachMaximumHeight : ObliquePivotMaximum;
            pivot = Mix(minimumPivot, maximumPivot, pivotRoll);
            tilt = Mix(ObliqueMinimumTiltDegrees, ObliqueMaximumTiltDegrees, tiltRoll);
            offsetScale = ObliqueOffsetScale;
        }
        float slope = MathF.Tan(tilt * RadiansPerDegree);
        if (NegativeLean(cutDirection, Rng.Mix(slopeBits))) slope = -slope;
        float offset = (Rng.Float(offsetBits) * 2 - 1) * radius * offsetScale;
        var normal = Vector3.Normalize(new Vector3(horizontal.X, slope, horizontal.Y));
        var anchor = new Vector3(horizontal.X * offset, pivot, horizontal.Y * offset);
        return new(new Vector4(normal, -Vector3.Dot(normal, anchor)), FullBody: true);
    }

    /// <summary>A varied original-bind-space plane, upper half positive. Height and slope stay clear of the fixed upper arms and lower legs.</summary>
    public static Vector4 PlaneForDirection(int cutDirection, ulong visualSeed = 0, Vector2 travel = default, float victimYaw = 0) =>
        PlaneForDirection(cutDirection, visualSeed, travel, victimYaw, stomach: false, lowStrike: false);

    static Vector4 PlaneForDirection(int cutDirection, ulong visualSeed, Vector2 travel, float victimYaw, bool stomach, bool lowStrike)
    {
        ulong heightBits = Rng.Mix(visualSeed), tiltBits = Rng.Mix(heightBits), azimuthBits = Rng.Mix(tiltBits);
        float maximumHeight = stomach ? StomachMaximumHeight : MaximumHeight;
        float height = Mix(MinimumHeight, maximumHeight, HeightRoll(heightBits, lowStrike));
        var local = LocalTravel(travel, victimYaw);
        var across = new Vector2(local.Y, -local.X) * (NegativeLean(cutDirection, Rng.Mix(azimuthBits)) ? -1 : 1);
        float angle = (Rng.Float(azimuthBits) * 2 - 1) * AzimuthVariationDegrees * RadiansPerDegree;
        var axis = Rotate(across, -angle);
        float tilt = Mix(.5f, MaximumTiltDegrees, Rng.Float(tiltBits)) * RadiansPerDegree;
        var slopes = axis * MathF.Tan(tilt);
        float scale = 1;
        if (MathF.Abs(slopes.X) > MaximumSlopeX) scale = MathF.Min(scale, MaximumSlopeX / MathF.Abs(slopes.X));
        if (MathF.Abs(slopes.Y) > MaximumSlopeZ) scale = MathF.Min(scale, MaximumSlopeZ / MathF.Abs(slopes.Y));
        float reach = JunctionHalfWidth * MathF.Abs(slopes.X) + JunctionHalfDepth * MathF.Abs(slopes.Y);
        // Keep the whole height range available. A cut nearer the hips or shoulders has less room to tilt.
        float budget = MathF.Min(height - LegJunctionCeiling, ShoulderJunctionFloor - height);
        if (reach > budget) scale = MathF.Min(scale, budget / reach);
        slopes *= scale;
        var normal = Vector3.Normalize(new Vector3(slopes.X, 1, slopes.Y));
        return new(normal, -height * normal.Y);
    }

    static bool NegativeLean(int direction, ulong bits) =>
        direction == 3 || direction != 2 && Rng.Float(bits) < .5f;

    static float HeightRoll(ulong bits, bool lowStrike) => Rng.Float(bits) * (lowStrike ? .72f : 1);

    static float Mix(float minimum, float maximum, float roll) => minimum + (maximum - minimum) * roll;

    static Vector2 Rotate(Vector2 direction, float angle)
    {
        float sine = MathF.Sin(angle), cosine = MathF.Cos(angle);
        return new(cosine * direction.X - sine * direction.Y, sine * direction.X + cosine * direction.Y);
    }

    static Vector2 LocalTravel(Vector2 travel, float yaw)
    {
        float length = travel.LengthSquared();
        return float.IsFinite(length) && length > 1e-8f && float.IsFinite(yaw)
            ? Rotate(travel / MathF.Sqrt(length), yaw)
            : new(0, -1);
    }
}
