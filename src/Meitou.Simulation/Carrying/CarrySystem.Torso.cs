using System.Numerics;

namespace Meitou.Simulation.Carrying;

// A bisected corpse keeps one inventory/identity, with an independent carry partner for each plane side.
public sealed partial class CarrySystem
{
    static bool HasPositiveTorso(CharacterCold cold) => cold.TorsoBisection is { IsValid: true };

    /// <summary>Whole-person fixtures cannot move a shared corpse through one of its independently held halves.</summary>
    public static bool CarriesTorsoFragment(World world, CharacterCold carrier) =>
        world.Characters.TryResolveNext(carrier.Carrying, out int body) && world.Characters.Cold(body) is { TorsoBisection.IsValid: true };

    void SeedTorsoFallbackPositions(World world)
    {
        if (bodyOptions.AdvancedRagdolls) return;
        var table = world.Characters;
        for (int i = 0; i < table.HighWater; i++)
            if (table.Next[i].Alive && table.Cold(i) is { TorsoBisection.IsValid: true, TorsoPositivePosition: null } body)
            {
                body.TorsoPositivePosition = table.Next[i].Position;
                body.TorsoPositiveYaw = table.Next[i].Yaw;
            }
    }

    static bool OpensInventory(object? detail) => detail == OpenInventory || detail == PositiveInventory;
    static int LootSide(object? detail) => detail == PositiveLoot || detail == PositiveInventory ? 1 : 0;
    static bool ValidLootSide(CharacterCold body, int side) => side <= 0 || HasPositiveTorso(body);

    static CharacterId Holder(CharacterCold body, int torsoSide) => torsoSide > 0 ? body.TorsoPositiveCarriedBy : body.CarriedBy;

    /// <summary>Lift eligibility for an explicitly chosen torso half; the positive side requires a valid lethal cut.</summary>
    public static bool CanBeLifted(CharacterCold cold, int torsoSide) => torsoSide <= 0 ? CanBeLifted(cold)
        : HasPositiveTorso(cold) && cold is { Medical.Dead: true, Race.Carriable: true, IsImprisoned: false } && cold.TorsoPositiveCarriedBy.IsNone;

    public static bool CanBeLifted(World world, CharacterCold cold, int torsoSide) => torsoSide <= 0 ? CanBeLifted(world, cold) : CanBeLifted(cold, torsoSide);

    static bool Snatchable(CharacterCold cold, int torsoSide) => torsoSide <= 0 ? Snatchable(cold)
        : HasPositiveTorso(cold) && cold is { Medical.Dead: true, Race.Carriable: true, IsImprisoned: false } && !cold.TorsoPositiveCarriedBy.IsNone;

    static Vector3 TargetPosition(CharacterTable table, int target, int torsoSide) =>
        torsoSide > 0 && table.Cold(target)?.TorsoPositivePosition is { } at ? at : table.Next[target].Position;

    static float GapTo(in CharacterHot from, Vector3 to)
    {
        float x = from.Position.X - to.X, z = from.Position.Z - to.Z;
        return MathF.Sqrt(x * x + z * z);
    }

    void UnlinkPositive(World world, int carrier, int body, bool drop)
    {
        var table = world.Characters;
        var c = table.Cold(carrier)!;
        var b = table.Cold(body)!;
        ref readonly var heldBy = ref table.Next[carrier];
        var at = heldBy.Position;
        if (!drop)
        {
            var side = new Vector2(MathF.Cos(heldBy.Yaw), -MathF.Sin(heldBy.Yaw)) * Settings.PutDownSide;
            float x = at.X + side.X, z = at.Z + side.Y;
            if (world.Walkability.IsWalkable(x, z, at.Y)) at = new Vector3(x, world.Walkability.GroundHeight(x, z, at.Y), z);
        }
        c.Carrying = CharacterId.None;
        c.CarryingTorsoSide = 0;
        b.TorsoPositiveCarriedBy = CharacterId.None;
        b.TorsoPositiveCarryAttach = null;
        b.TorsoPositivePosition = at;
        b.TorsoPositiveYaw = heldBy.Yaw;
        b.TorsoPositiveReleaseAt = at;
    }
}
