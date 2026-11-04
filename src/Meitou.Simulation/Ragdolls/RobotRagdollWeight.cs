using System.Numerics;

namespace Meitou.Simulation.Ragdolls;

/// <summary>Meitou material tuning: metal keeps its momentum but receives less speed from a blow; gravity is unchanged.</summary>
public static class RobotRagdollWeight
{
    public const float MassMultiplier = 2.5f, ImpactResponse = 0.35f;

    public static float Response(bool robot) => robot ? ImpactResponse : 1;

    /// <summary>A blow includes the previous movement plus its push. Attenuate only that added push.</summary>
    public static (Vector3 Velocity, Vector3 Push) Launch(bool robot, (Vector3 Velocity, Vector3 Push) start) =>
        !robot ? start : (start.Velocity - start.Push * (1 - ImpactResponse), start.Push * ImpactResponse);

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
