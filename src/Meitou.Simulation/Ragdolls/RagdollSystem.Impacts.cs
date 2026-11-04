using System.Numerics;
using Meitou.Data.Gameplay.Bodies;

namespace Meitou.Simulation.Ragdolls;

public sealed partial class RagdollSystem
{
    readonly record struct HitImpact(CharacterId Id, int Actor, Vector3 LocalOffset, Vector3 Push);
    readonly List<HitImpact> hitImpacts = [];

    /// <summary>A landed hit on an existing unconscious body, consumed once before any newly severed parts inherit its motion.</summary>
    internal bool QueueHit(World world, CharacterId id, BodyPartTemplate part, Vector3 push)
    {
        if (options is not { AdvancedRagdolls: true } || !float.IsFinite(push.LengthSquared()) || push.LengthSquared() < 1e-6f
            || !world.Characters.TryResolveNext(id, out int slot) || world.Characters.Cold(slot) is not { Medical.Incapacitated: true } cold
            || Wanted(cold) != RagdollPart.Whole || !ragdolls.TryGetValue(id, out var r) || r.Mask != RagdollPart.Whole || !r.InScene) return false;
        if (!ImpactSite(r, part, out int actor, out var localOffset)) return false;
        hitImpacts.Add(new(id, actor, localOffset, push));
        return true;
    }

    /// <summary>The chosen anatomy site in last tick's actual physical pose; safe for parallel combat and pursuit reads.</summary>
    internal bool TryPartPosition(CharacterId id, BodyPartTemplate part, out Vector3 position)
    {
        position = default;
        if (!ragdolls.TryGetValue(id, out var r) || r.Mask != RagdollPart.Whole || !ImpactSite(r, part, out int actor, out var offset)) return false;
        position = r.Pose[actor].Apply(offset);
        return true;
    }

    static bool ImpactSite(Ragdoll r, BodyPartTemplate part, out int actor, out Vector3 localOffset)
    {
        localOffset = default;
        var rig = r.Setup.Rig;
        int? bone = rig.Handle(part.BoneName) ?? rig.Handle(part.BoneName2);
        if (bone is null)
        {
            string side = part.Side == BodySide.Left ? "L" : "R";
            bone = rig.Handle(part.Type switch
            {
                BodyPartType.Head => "Bip01 Head",
                BodyPartType.Arm => $"Bip01 {side} Forearm",
                BodyPartType.Leg => $"Bip01 {side} Calf",
                _ => "Bip01 Spine1",
            });
        }
        actor = bone is { } h && h < r.Definition.BodyOfBone.Length ? r.Definition.BodyOfBone[h] : -1;
        var bindPoint = bone is { } b ? rig.BindPosition[b] * r.Scale : r.Definition.Bodies[r.Definition.Root].Bind.Position;
        if (actor < 0)
        {
            float nearest = float.MaxValue;
            for (int i = 0; i < r.Bodies.Length; i++)
                if (r.Dynamic[i] && r.Bodies[i] is not null && Vector3.DistanceSquared(bindPoint, r.Definition.Bodies[i].Bind.Position) is var distance && distance < nearest)
                    (actor, nearest) = (i, distance);
        }
        if (actor < 0 || actor >= r.Bodies.Length || !r.Dynamic[actor] || r.Bodies[actor] is null) return false;
        localOffset = r.Definition.Bodies[actor].Bind.Inverse().Apply(bindPoint);
        return true;
    }

    void ApplyHitImpacts(World world)
    {
        foreach (var hit in hitImpacts)
        {
            if (options is not { AdvancedRagdolls: true } || !world.Characters.TryResolveNext(hit.Id, out int slot)
                || world.Characters.Cold(slot) is not { Medical.Incapacitated: true } cold || Wanted(cold) != RagdollPart.Whole
                || !ragdolls.TryGetValue(hit.Id, out var r) || r.Mask != RagdollPart.Whole
                || hit.Actor >= r.Bodies.Length || r.Bodies[hit.Actor] is null) continue;
            // Freeze removes joints and makes actors kinematic. Rebuild from the physical pose, never from animation.
            if (r.Frozen) Rebuild(r, r.Mask, (RigidTransform[])r.Pose.Clone(), soft: true, seed: Rng.Key(hit.Id));
            r.Age = 0;
            if (ToneOf(cold, r.Mask, true) is var tone && r.Tone != tone) SetTone(r, tone);
            var body = physics.Simulation.Bodies[r.Bodies[hit.Actor]!.Value];
            body.Awake = true;
            // Awake may move a sleeping island; reacquire the reference before reading its inertia or applying force.
            body = physics.Simulation.Bodies[r.Bodies[hit.Actor]!.Value];
            if (body.LocalInertia.InverseMass <= 0) continue;
            var push = hit.Push * RobotRagdollWeight.Response(cold.Race is { IsRobot: true });
            body.ApplyImpulse(push / body.LocalInertia.InverseMass, Vector3.Transform(hit.LocalOffset, body.Pose.Orientation));
        }
        hitImpacts.Clear();
    }

    void HashHitImpacts(ref StateHasher hasher)
    {
        if (hitImpacts.Count == 0) return;
        hasher.Add(0x48697452);
        hasher.Add(hitImpacts.Count);
        foreach (var hit in hitImpacts)
        {
            hasher.Add(hit.Id);
            hasher.Add(hit.Actor);
            hasher.Add(hit.LocalOffset);
            hasher.Add(hit.Push);
        }
    }
}