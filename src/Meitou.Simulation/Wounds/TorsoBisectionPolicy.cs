using System.Numerics;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation.Combat;

namespace Meitou.Simulation.Wounds;

/// <summary>The delivered chest hit and the victim's state before it landed.</summary>
public readonly record struct TorsoBisectionInput(CombatHitSource Source, CombatWoundMechanism Mechanism,
    BodyPartTemplate Part, int CutDirection, float Cut, float Maximum, bool AliveBeforeHit = true,
    bool AlreadyBisected = false, bool Robot = false, bool Animal = false);

/// <summary>Meitou's rare, lethal sideways chest cleave; the supplied roll reads no existing random stream.</summary>
public static class TorsoBisectionPolicy
{
    public const float MinimumCut = 70, MaximumHpFraction = 0.75f, BaseChance = 0.10f, MaximumChance = 0.20f;
    public const float MinimumHeight = 11.5f, MaximumHeight = 13.5f, MaximumTiltDegrees = 30, AzimuthVariationDegrees = 30;
    public const float MaximumSlopeX = 0.55f, MaximumSlopeZ = 0.24f;
    // Conservative junction bounds measured on installed human/Shek male/female and worker/soldier/prince meshes.
    public const float LegJunctionCeiling = 11.35f, ShoulderJunctionFloor = 14.10f;
    public const float JunctionHalfWidth = 3, JunctionHalfDepth = 1.51f;

    public static float Chance(in TorsoBisectionInput input)
    {
        if (!input.AliveBeforeHit || input.AlreadyBisected || input.Robot || input.Animal
            || input.Source != CombatHitSource.Melee || input.Mechanism != CombatWoundMechanism.Slash
            || input.CutDirection is not (2 or 3 or 9)
            || input.Part is not { Type: BodyPartType.Torso, StringId: ArmourData.ChestPartId }
            || !float.IsFinite(input.Cut) || !float.IsFinite(input.Maximum) || input.Maximum <= 0) return 0;
        float threshold = MathF.Max(MinimumCut, MaximumHpFraction * input.Maximum);
        if (input.Cut < threshold) return 0;
        return BaseChance + (MaximumChance - BaseChance) * Math.Clamp(input.Cut / threshold - 1, 0, 1);
    }

    /// <summary>True for an eligible hit and a roll in [0, chance); callers may supply a fixed roll for a deterministic scene.</summary>
    public static bool ShouldBisect(in TorsoBisectionInput input, float roll) =>
        float.IsFinite(roll) && roll >= 0 && roll < Chance(input);

    /// <summary>The opening blow's own visual hash; it does not read or change the chance roll or any existing combat key.</summary>
    public static ulong VisualSeedFor(ulong worldSeed, CharacterId victim, CharacterId attacker, ulong counter) =>
        Rng.Hash(worldSeed, Rng.Key(victim) ^ Rng.Mix(Rng.Key(attacker)), RngPurpose.TorsoBisectionVisual, counter);

    /// <summary>A varied original-bind-space plane, upper half positive. Height and slope stay clear of the fixed upper arms and lower legs.</summary>
    public static Vector4 PlaneForDirection(int cutDirection, ulong visualSeed = 0, Vector2 travel = default, float victimYaw = 0)
    {
        ulong heightBits = Rng.Mix(visualSeed), tiltBits = Rng.Mix(heightBits), azimuthBits = Rng.Mix(tiltBits);
        float height = MinimumHeight + (MaximumHeight - MinimumHeight) * Rng.Float(heightBits);
        var local = new Vector2(0, -1);
        float length = travel.LengthSquared();
        if (float.IsFinite(length) && length > 1e-8f && float.IsFinite(victimYaw))
        {
            travel /= MathF.Sqrt(length);
            float sine = MathF.Sin(victimYaw), cosine = MathF.Cos(victimYaw);
            local = new(cosine * travel.X - sine * travel.Y, sine * travel.X + cosine * travel.Y);
        }
        var across = new Vector2(local.Y, -local.X) * (cutDirection == 2 ? 1 : -1);
        float angle = (Rng.Float(azimuthBits) * 2 - 1) * AzimuthVariationDegrees * MathF.PI / 180;
        var axis = new Vector2(across.X * MathF.Cos(angle) + across.Y * MathF.Sin(angle),
            across.Y * MathF.Cos(angle) - across.X * MathF.Sin(angle));
        float tilt = (4 + (MaximumTiltDegrees - 4) * Rng.Float(tiltBits)) * MathF.PI / 180;
        var slopes = axis * MathF.Tan(tilt);
        float scale = 1;
        if (MathF.Abs(slopes.X) > MaximumSlopeX) scale = MathF.Min(scale, MaximumSlopeX / MathF.Abs(slopes.X));
        if (MathF.Abs(slopes.Y) > MaximumSlopeZ) scale = MathF.Min(scale, MaximumSlopeZ / MathF.Abs(slopes.Y));
        float reach = JunctionHalfWidth * MathF.Abs(slopes.X) + JunctionHalfDepth * MathF.Abs(slopes.Y);
        float budget = MathF.Min(height - LegJunctionCeiling, ShoulderJunctionFloor - height);
        if (reach > budget) scale = MathF.Min(scale, budget / reach);
        slopes *= scale;
        var normal = Vector3.Normalize(new Vector3(slopes.X, 1, slopes.Y));
        return new(normal, -height * normal.Y);
    }
}
