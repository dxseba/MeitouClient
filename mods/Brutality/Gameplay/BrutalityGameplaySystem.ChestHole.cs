using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;

namespace Meitou.Brutality;

public sealed partial class BrutalityGameplaySystem
{
    ChestHoleRule holeRule = ChestHoleRule.Default;

    /// <summary>The settings page writes on the interface thread; the tick reads one copy per tick, in a serial step.</summary>
    void ReadChestHoleRule() => holeRule = chestHoleRule?.Invoke() ?? ChestHoleRule.Default;

    void TryPunchThrough(World world, in HitWork work, Actor actor, BodyPartTemplate part, GoreHitBridge context, ulong counter)
    {
        var hit = work.Hit;
        float damage = hit.Damage.Cut + hit.Damage.Blunt + hit.Damage.Pierce;
        var input = new ChestHoleInput(hit.Source, context.Mechanism, context.WeaponCategory, part.StringId, damage,
            context.PartMaximumBeforeHit ?? work.Maximum, hit.VictimWasAlive, work.VictimDead, actor.ChestHole is not null,
            actor.Torso is not null, work.Robot, work.Animal);
        if (!ChestHolePolicy.Qualifies(input, holeRule)) return;

        ulong roll = Rng.Hash(world.Seed, Rng.Key(hit.Victim), GoreRandom.ChestHoleVisualPurpose, counter);
        int region = ChestHolePolicy.RegionOf(part.StringId);
        var shape = ChestHolePolicy.StateFor(hit.Travel, hit.VictimYaw, damage, input.PartMaximum, roll, region, holeRule.Size);
        if (shape is not { IsValid: true } hole) return;
        actor.ChestHole = hole;
        Add(hit, GoreCutKind.ChestHole, chestHole: hole);
    }

    /// <summary>Adds nothing without a hole, so worlds without one keep their earlier hashes.</summary>
    static void HashChestHole(ref StateHasher hash, ChestHoleState? hole)
    {
        if (hole is not { } h) return;
        hash.Add(0x484F4C45);
        hash.Add(h.Height); hash.Add(h.Across); hash.Add(h.Yaw); hash.Add(h.Radius); hash.Add(h.Entry); hash.Add(h.Seed);
        hash.Add(h.Region); hash.Add(h.Aspect); hash.Add(h.Tilt); hash.Add(h.Tear); hash.Add(h.Flare);
    }
}
