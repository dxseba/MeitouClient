using System.Numerics;

namespace Meitou.Brutality.Physics;

/// <summary>Meitou material tuning: metal keeps its momentum but receives less speed from a blow; gravity is unchanged.</summary>
public static class RobotRagdollWeight
{
    public const float MassMultiplier = 2.5f, ImpactResponse = 0.35f;

    public static float Response(bool robot) => robot ? ImpactResponse : 1;

    /// <summary>A blow includes the previous movement plus its push. Attenuate only that added push.</summary>
    public static (Vector3 Velocity, Vector3 Push) Launch(bool robot, (Vector3 Velocity, Vector3 Push) start) =>
        !robot ? start : (start.Velocity - start.Push * (1 - ImpactResponse), start.Push * ImpactResponse);

#if !BRUTALITY_STOCK_MASTER
    /// <summary>Replace only a newly assigned native launch; preserve kept actors and all later gravity/contact changes.</summary>
    internal static FragmentBodyMotion[] CaptureMotion(bool robot, Meitou.Simulation.Ragdolls.RagdollHandoff? handoff)
    {
        if (handoff is null) return [];
        var result = new List<FragmentBodyMotion>(handoff.Bodies.Count);
        foreach (var body in handoff.Bodies)
        {
            if (!body.HasMotion) continue;
            var linear = body.Linear; var angular = body.Angular;
            if (robot && handoff.AppliedLaunch is { } raw && body.AssignedLaunch is { } assigned)
            {
                // Pin scales the added push before Rebuild clamps the complete launch to half MaxSpeed.
                var replacement = Launch(true, (raw.Velocity, raw.Push));
                var desired = replacement.Velocity;
                float limit = RagdollPhysics.MaxSpeed / 2;
                if (desired.LengthSquared() > limit * limit) desired = Vector3.Normalize(desired) * limit;
                linear += desired - assigned.Linear;
                // Native Spin saturates before its deterministic per-limb roll; preserve that roll and axis.
                float rawSpin = MathF.Min(new Vector2(raw.Push.X, raw.Push.Z).Length() * .15f, 6);
                float newSpin = MathF.Min(new Vector2(replacement.Push.X, replacement.Push.Z).Length() * .15f, 6);
                float ratio = rawSpin > 1e-8f ? newSpin / rawSpin : 1;
                angular += assigned.Angular * (ratio - 1);
            }
            result.Add(new(body.Bone, new(body.Body.Position, body.Body.Rotation), linear, angular));
        }
        return result.ToArray();
    }

#endif

    internal static RagdollDefinition Weight(RagdollDefinition source) => new()
    {
        Bodies = [.. source.Bodies.Select(body => new RagdollBody
        {
            Bone = body.Bone, Name = body.Name, Type = body.Type, Size = body.Size, Hull = body.Hull,
            Bind = body.Bind, BoneInBody = body.BoneInBody, Mass = body.Mass * MassMultiplier,
        })],
        Joints = source.Joints, BodyOfBone = source.BodyOfBone, Root = source.Root, Joined = source.Joined,
    };
}
