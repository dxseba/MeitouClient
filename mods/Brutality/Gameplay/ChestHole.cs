using System.Numerics;
using System.Text.Json.Serialization;
using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.Brutality;

/// <summary>
/// A punch-through torso wound (chest or stomach) in the original body's bind frame. <see cref="Height"/> runs along the Pelvis to Neck
/// bind line; <see cref="Across"/> and <see cref="Radius"/> are fractions of that line's length, so the hole follows each race's rig.
/// <see cref="Yaw"/> turns the axis from straight through the torso; <see cref="Entry"/> is +1 for a blow from the front, -1 from behind.
/// The opening is an ellipse of <see cref="Aspect"/> (area kept) turned by <see cref="Tilt"/>; <see cref="Tear"/> scales its ragged edge
/// and <see cref="Flare"/> widens it towards the exit.
/// </summary>
public readonly record struct ChestHoleState(float Height, float Across, float Yaw, float Radius, int Entry, int Seed,
    int Region = ChestHoleState.Chest, float Aspect = 1, float Tilt = 0, float Tear = 1, float Flare = 1)
{
    public const int Chest = 0, Abdomen = 1;
    public const float MaxAcross = .06f, MaxYaw = .35f, MinRadius = .04f, MaxRadius = .2f;
    public const float MinAspect = .75f, MaxAspect = 1.34f, MinTear = .5f, MaxTear = 1.4f, MaxFlare = 1.3f;
    public const int MaxSeed = 996;

    [JsonIgnore]
    public bool IsValid => Finite && HeightFitsRegion && MathF.Abs(Across) <= MaxAcross && MathF.Abs(Yaw) <= MaxYaw
        && Radius is >= MinRadius and <= MaxRadius && Entry is -1 or 1 && Seed is >= 0 and <= MaxSeed
        && Aspect is >= MinAspect and <= MaxAspect && MathF.Abs(Tilt) <= MathF.PI / 2 + 1e-4f
        && Tear is >= MinTear and <= MaxTear && Flare is >= 1 and <= MaxFlare;

    bool Finite => float.IsFinite(Height) && float.IsFinite(Across) && float.IsFinite(Yaw) && float.IsFinite(Radius)
        && float.IsFinite(Aspect) && float.IsFinite(Tilt) && float.IsFinite(Tear) && float.IsFinite(Flare);

    bool HeightFitsRegion => Region switch
    {
        Chest => Height is >= .5f and <= .8f,
        Abdomen => Height is >= .2f and <= .5f,
        _ => false,
    };
}

/// <summary>The player's punch-through settings, copied into the simulation in a serial step. Size scales new holes only.</summary>
public readonly record struct ChestHoleRule(bool Enabled, bool LethalOnly, float DamageFraction, float Size = 1)
{
    public static ChestHoleRule Default { get; } = new(true, false, .5f);
}

/// <summary>One landed blow after armour, with the victim's state before it.</summary>
public readonly record struct ChestHoleInput(CombatHitSource Source, CombatWoundMechanism Mechanism, int WeaponCategory, string? PartId,
    float Damage, float PartMaximum, bool AliveBeforeHit, bool Lethal, bool Holed, bool Bisected, bool Robot, bool Animal);

/// <summary>Which bare-handed blows punch through the chest or stomach, and where. Visual state only: it changes no combat outcome.</summary>
public static class ChestHolePolicy
{
    /// <summary>The chest's medical part; the stomach is <see cref="GorePartIds.Stomach"/>.</summary>
    public const string ChestPartId = ArmourData.ChestPartId;

    /// <summary>A blow more than this far (radians) from straight through the torso makes no hole.</summary>
    const float MaxApproach = 1f;

    /// <summary>The widest reach of the ragged, flared ellipse, as a fraction of the spine: keeps the hole and its lining inside the torso.</summary>
    const float MaxReach = .24f;

    /// <summary>The region a struck part opens, or -1 for a part that is not the chest or the stomach.</summary>
    public static int RegionOf(string? partId) => partId switch
    {
        ChestPartId => ChestHoleState.Chest,
        GorePartIds.Stomach => ChestHoleState.Abdomen,
        _ => -1,
    };

    public static bool Qualifies(in ChestHoleInput input, in ChestHoleRule rule)
    {
        if (!rule.Enabled || input.Source != CombatHitSource.Melee || !input.AliveBeforeHit) return false;
        if (input.Holed || input.Bisected || input.Animal) return false;
#if BRUTALITY_STOCK_MASTER
        if (input.Robot && !input.Lethal) return false;
#else
        if (input.Robot) return false;
#endif
        if (input.Mechanism != CombatWoundMechanism.Crush || input.WeaponCategory != (int)WeaponCategory.Unarmed) return false;
        if (RegionOf(input.PartId) < 0 || rule.LethalOnly && !input.Lethal) return false;

        float fraction = float.IsFinite(rule.DamageFraction) ? Math.Clamp(rule.DamageFraction, 0, 10) : 1;
        return float.IsFinite(input.Damage) && float.IsFinite(input.PartMaximum) && input.PartMaximum > 0
            && input.Damage >= input.PartMaximum * fraction;
    }

    /// <summary>Projects a finishing strike onto a viable through-body axis, keeping its entry side and obliquity.</summary>
    public static ChestHoleState? StateFor(Vector3 travel, float victimYaw, float damage, float maximum, ulong roll,
        int region = ChestHoleState.Chest, float size = 1, bool lethalSide = false)
    {
        if (!float.IsFinite(victimYaw)) return null;
        var forward = new Vector2(MathF.Sin(victimYaw), MathF.Cos(victimYaw));
        var flat = new Vector2(travel.X, travel.Z);
        if (!float.IsFinite(flat.LengthSquared())) return null;
        if (flat.LengthSquared() < 1e-8f)
        {
            if (!lethalSide) return null;
            flat = -forward;
        }

        float along = Vector2.Dot(flat, forward);
        float lateral = forward.Y * flat.X - forward.X * flat.Y;
        float approach = MathF.Atan2(lateral, MathF.Abs(along));
        if (MathF.Abs(approach) > MaxApproach && !lethalSide) return null;

        var dice = new Dice(roll);
        float excess = maximum > 0 ? Math.Clamp((damage / maximum - .5f) / 1.5f, 0, 1) : 0;
        float height = region == ChestHoleState.Abdomen ? .28f + dice.Next() * .14f : .58f + dice.Next() * .14f;
        float across = (dice.Next() - .5f) * .1f;
        float scale = float.IsFinite(size) ? Math.Clamp(size, .25f, 2) : 1;
        float radius = (.085f + .035f * excess + (dice.Next() - .5f) * .02f) * scale;
        var shape = Shape(ref dice);
        radius = Fit(radius, shape.Aspect, shape.Tear, shape.Flare);
        int seed = (int)(dice.Bits() % (ChestHoleState.MaxSeed + 1));
        return new(height, across, Math.Clamp(approach, -ChestHoleState.MaxYaw, ChestHoleState.MaxYaw), radius, along > 0 ? -1 : 1, seed,
            region, shape.Aspect, shape.Tilt, shape.Tear, shape.Flare);
    }

    /// <summary>An oval at any angle (area kept), a calm or deeply torn edge, and an exit mostly a little wider than the entry.</summary>
    static (float Aspect, float Tilt, float Tear, float Flare) Shape(ref Dice dice)
    {
        float aspect = MathF.Exp((dice.Next() - .5f) * .44f);
        float tilt = (dice.Next() - .5f) * MathF.PI;
        float tear = .6f + dice.Next() * .7f;
        float flare = 1 + dice.Next() * dice.Next() * .28f;
        return (aspect, tilt, tear, flare);
    }

    /// <summary>Shrinks a hole whose widest ragged, flared reach would leave the torso.</summary>
    static float Fit(float radius, float aspect, float tear, float flare)
    {
        float reach = radius * MathF.Max(aspect, 1 / aspect) * (1 + .22f * tear) * flare;
        float fitted = reach > MaxReach ? radius * MaxReach / reach : radius;
        return Math.Clamp(fitted, ChestHoleState.MinRadius, ChestHoleState.MaxRadius);
    }

    /// <summary>Fixed developer shapes: round from the front, a torn tilted oval at an angle, and a flared upright oval from behind.</summary>
    public static ChestHoleState Preview(int variant, int region = ChestHoleState.Chest)
    {
        float height = region == ChestHoleState.Abdomen ? .35f : .64f;
        return (Math.Abs(variant) % 3) switch
        {
            1 => new(height, .03f, .3f, .1f, 1, 417, region, 1.2f, .5f, 1.25f, 1.15f),
            2 => new(height, 0, 0, .095f, -1, 733, region, .82f, 0, .8f, 1.3f),
            _ => new(height, 0, 0, .115f, 1, 211, region),
        };
    }

    /// <summary>Successive 16-bit draws from a blow's roll, then from its mix.</summary>
    struct Dice(ulong roll)
    {
        ulong word = roll;
        int used;

        public float Next()
        {
            if (used == 4) { word = Rng.Mix(word); used = 0; }
            return (word >> (16 * used++) & 0xFFFF) / 65535f;
        }

        public ulong Bits() => Rng.Mix(word ^ (ulong)used);
    }
}
