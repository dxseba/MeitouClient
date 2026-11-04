using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.Game;

sealed partial class GameHost
{
    readonly RobotBreakExplosionTracker robotExplosions = new();
    int robotExplosionBursts;
    bool RobotExplosionsEnabled => !o.NoParticles || IsFlat && g.FlatCombatParticles;

    void EmitRobotBreakExplosion(in CombatEvent hit, CharacterCold? victim)
    {
        if (!RobotExplosionsEnabled || gpu?.Particles is null || RobotBreakExplosionTracker.Kind(hit, victim) < 0) return;
        var part = victim!.Medical!.Parts[hit.Part].Template;
        var fallback = hit.Position + Vector3.UnitY * RobotCutEffects.FallbackHeight(part);
        if (robotExplosions.Observe(hit, victim, RobotDischargeClock, fallback, session.World.Seed)) robotExplosionBursts++;
    }

    void UpdateRobotBreakExplosions(World world)
    {
        if (gpu.Particles is not { } particles) return;
        particles.RobotBreakExplosions.Clear();
        if (!RobotExplosionsEnabled) { robotExplosions.Clear(); return; }
        robotExplosions.Prune(RobotDischargeClock, world.Characters.IsAlive);
        foreach (var burst in robotExplosions.Active)
        {
            var medical = world.Characters.Cold(burst.Victim.Slot)?.Medical;
            if (medical is null || (uint)burst.Part >= (uint)medical.Parts.Count) continue;
            var part = medical.Parts[burst.Part].Template;
            if (burst.Position is null)
            {
                var origin = RobotBreakOrigin(burst, part);
                if (origin is { } posed) burst.Capture(posed);
                else if (RobotDischargeClock - burst.Born > .15) burst.Capture(burst.Fallback);
            }
            // The initial flash stays at the impact; soot keeps emerging from the current moving fragment.
            if (RobotBreakOrigin(burst, part, followFragment: true) is { } smoke) burst.FollowSmoke(smoke);
            if (burst.Position is { } at)
                particles.RobotBreakExplosions.Add(new(at, (float)(RobotDischargeClock - burst.Born), burst.Seed, burst.Scale)
                    { SmokePosition = burst.SmokePosition });
        }
    }

    Vector3? RobotBreakOrigin(RobotBreakBurst burst, Meitou.Data.Gameplay.Bodies.BodyPartTemplate part, bool followFragment = false)
    {
        if (gpu.Characters is not { } renderer) return null;
        long key = burst.Kind == 2 ? TorsoDrawKey(burst.Victim, 0) : DrawKey(burst.Victim);
        if (burst.Kind == 0 || followFragment && burst.Kind == 1)
        {
            var owner = session.CurrentSnapshot.Characters.FirstOrDefault(c => c.Id == burst.Victim);
            int target = burst.Kind == 1 ? -1 : part.LimbSlot;
            if (owner is not null)
                foreach (var piece in owner.DetachedRagdolls)
                {
                    if (piece.Part != target) continue;
                    long detachedKey = DetachedDrawKey(key, piece.Part, piece.FragmentId);
                    var cutContact = renderer.CutContact(detachedKey) ?? (target >= 0 ? renderer.LimbCutContact(detachedKey, target) : null);
                    if (cutContact is { } rim) return rim.Position + rim.Normal * .06f;
                    if (followFragment && renderer.BonePosition(detachedKey, RobotCutEffects.ImpactBone(part, burst.LowStrike)) is { } boneAt)
                        return boneAt;
                }
            if (followFragment)
                foreach (var piece in heads)
                {
                    if (piece.From != key || (burst.Kind == 1 ? piece.PartOnly != 0 : piece.PartOnly >> 8 != (uint)(target + 1))) continue;
                    if (renderer.CutContact(piece.Key) is { } rim) return rim.Position + rim.Normal * .06f;
                    if (renderer.BonePosition(piece.Key, RobotCutEffects.ImpactBone(part, burst.LowStrike)) is { } boneAt) return boneAt;
                }
        }
        var contact = renderer.CutContact(key) ?? (burst.Kind == 0 ? renderer.LimbCutContact(key, part.LimbSlot) : null);
        if (contact is { } surface) return surface.Position + surface.Normal * .06f;
        string bone = burst.Kind == 1 ? "Bip01 Neck" : RobotCutEffects.ImpactBone(part, burst.LowStrike);
        return renderer.BonePosition(key, bone);
    }

    void ClearRobotBreakExplosions()
    {
        robotExplosions.Clear(); robotExplosionBursts = 0;
        gpu?.Particles?.RobotBreakExplosions.Clear();
    }
}
