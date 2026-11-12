using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using BepuPhysics;
using Meitou.Simulation;
using Meitou.Data.Characters;

namespace Meitou.Brutality.Physics;

internal sealed record SavedFragmentPiece(int Key, string Stamp, string Definition, string[] Bones,
    RigidTransform[] Pose, Vector3[] Linear, Vector3[] Angular, bool[] Dynamic, bool Frozen, bool Carried, float Age)
{
    public int[]? Anchors { get; init; }
    public RigidTransform[]? Offsets { get; init; }
}

internal sealed partial class AdvancedFragmentScene
{
    sealed record DefinitionKey(string Value);
    static readonly ConditionalWeakTable<RagdollDefinition, DefinitionKey> savedDefinitionKeys = new();

    internal SavedFragmentPiece Capture(Piece piece, int key, string stamp, SkeletonRig? rig = null)
    {
        if (rig is not null) EnsureAnchors(piece, rig);
        Read(piece);
        var linear = new Vector3[piece.Bodies.Length]; var angular = new Vector3[piece.Bodies.Length];
        for (int b = 0; b < piece.Bodies.Length; b++)
            if (piece.Bodies[b] is { } handle)
            {
                var body = physics.Simulation.Bodies.GetBodyReference(handle);
                linear[b] = body.Velocity.Linear; angular[b] = body.Velocity.Angular;
            }
        return new(key, stamp, DefinitionKeyOf(piece.Definition), piece.Definition.Bodies.Select(body => body.Name).ToArray(),
            piece.Pose.ToArray(), linear, angular, piece.Dynamic.ToArray(), piece.Frozen, piece.Carried, piece.Age)
        { Anchors = piece.Anchors?.ToArray(), Offsets = piece.Offsets?.ToArray() };
    }
    internal bool Restore(Piece piece, SavedFragmentPiece saved, Vector3 offset = default)
    {
        if (!Valid(saved) || saved.Definition != DefinitionKeyOf(piece.Definition) || saved.Pose.Length != piece.Definition.Bodies.Length
            || !saved.Bones.SequenceEqual(piece.Definition.Bodies.Select(body => body.Name)) || !Finite(offset, 10_000_000)
            || saved.Anchors is { } anchors && anchors.Length != piece.Definition.BodyOfBone.Length) return false;
#if BRUTALITY_STOCK_MASTER
        if (!saved.Carried && saved.Dynamic.Any(dynamic => !dynamic))
        {
            var connected = piece.StockGripBodies ??= ConnectedRoot(piece.Definition);
            if (saved.Dynamic.Where((dynamic, b) => !dynamic && connected[b]).Any()) return false;
        }
#endif
        if (piece.Frozen) Impulse(piece, -1, Vector3.Zero);
        foreach (var joint in piece.Joints) physics.Simulation.Solver.Remove(joint);
        piece.Joints.Clear();
        var kinds = Shapes(piece.Definition);
        for (int b = 0; b < piece.Pose.Length; b++)
        {
            var at = saved.Pose[b] with { Position = saved.Pose[b].Position + offset };
            piece.Pose[b] = at; piece.Dynamic[b] = saved.Dynamic[b];
            if (piece.Bodies[b] is not { } handle) continue;
            var body = physics.Simulation.Bodies.GetBodyReference(handle);
            if (saved.Dynamic[b]) body.SetLocalInertia(kinds[b].Inertia); else body.BecomeKinematic();
            body.Pose = new RigidPose(at.Position, at.Rotation);
            body.Velocity = new BodyVelocity(saved.Linear[b], saved.Angular[b]); body.Awake = true;
        }
        piece.Age = saved.Age; piece.Carried = saved.Carried;
        if (saved.Anchors is not null)
        { piece.Anchors = saved.Anchors.ToArray(); piece.Offsets = saved.Offsets!.ToArray(); piece.Components = null; }
        RebuildCarryJoints(piece);
        if (saved.Frozen) Freeze(piece);
        return true;
    }
    internal static bool Valid(SavedFragmentPiece? saved) => saved is { Bones: not null, Pose: not null, Linear: not null, Angular: not null, Dynamic: not null }
        && saved.Key is 0 or 1 or 2 or 3 or 10 or 11 or 12 or 13 && saved.Stamp is { Length: > 0 and <= 256 }
        && saved.Definition is { Length: 16 } && saved.Bones.Length is > 0 and <= 128
        && saved.Pose.Length == saved.Bones.Length && saved.Linear.Length == saved.Pose.Length && saved.Angular.Length == saved.Pose.Length && saved.Dynamic.Length == saved.Pose.Length
        && saved.Bones.All(name => name is { Length: > 0 and <= 128 }) && saved.Bones.Distinct(StringComparer.Ordinal).Count() == saved.Bones.Length
        && float.IsFinite(saved.Age) && saved.Age is >= 0 and <= 1_000_000_000 && !(saved.Frozen && saved.Carried)
#if !BRUTALITY_STOCK_MASTER
        && (saved.Carried || saved.Dynamic.All(dynamic => dynamic))
#endif
        && saved.Pose.All(at => Finite(at.Position, 10_000_000) && float.IsFinite(at.Rotation.LengthSquared()) && MathF.Abs(at.Rotation.LengthSquared() - 1) <= .01f)
        && saved.Linear.All(v => Finite(v, 10000)) && saved.Angular.All(v => Finite(v, 10000))
        && (saved.Anchors is null && saved.Offsets is null || saved.Anchors is { Length: > 0 and <= 1024 } && saved.Offsets is not null
            && saved.Anchors.Length == saved.Offsets.Length && saved.Anchors.All(body => body >= 0 && body < saved.Pose.Length)
            && saved.Offsets.All(at => Finite(at.Position, 10_000_000) && float.IsFinite(at.Rotation.LengthSquared()) && MathF.Abs(at.Rotation.LengthSquared() - 1) <= .01f));
    static bool Finite(Vector3 value, float maximum) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z)
        && MathF.Abs(value.X) <= maximum && MathF.Abs(value.Y) <= maximum && MathF.Abs(value.Z) <= maximum;
    static string DefinitionKeyOf(RagdollDefinition definition) => savedDefinitionKeys.GetValue(definition, static source =>
    {
        var hash = new StateHasher(); hash.Add(source.Root); hash.Add(source.Bodies.Length); hash.Add(source.Joints.Length);
        void Transform(RigidTransform at) { hash.Add(at.Position); hash.Add(at.Rotation.X); hash.Add(at.Rotation.Y); hash.Add(at.Rotation.Z); hash.Add(at.Rotation.W); }
        foreach (var body in source.Bodies)
        {
            hash.Add(Rng.StableHash(body.Name)); hash.Add(body.Bone); hash.Add((int)body.Type); hash.Add(body.Size); hash.Add(body.Mass);
            Transform(body.Bind); Transform(body.BoneInBody); hash.Add(body.Hull?.Length ?? 0);
            if (body.Hull is { } hull) foreach (var point in hull) hash.Add(point);
        }
        foreach (var joint in source.Joints)
        {
            hash.Add(joint.A); hash.Add(joint.B); hash.Add((int)joint.Type); hash.Add(joint.OffsetA); hash.Add(joint.OffsetB);
            hash.Add(joint.BasisA.X); hash.Add(joint.BasisA.Y); hash.Add(joint.BasisA.Z); hash.Add(joint.BasisA.W);
            hash.Add(joint.BasisB.X); hash.Add(joint.BasisB.Y); hash.Add(joint.BasisB.Z); hash.Add(joint.BasisB.W);
            hash.Add(joint.Limits); hash.Add(joint.Low); hash.Add(joint.High); hash.Add(joint.Swing);
            hash.Add(joint.SpringStiffness); hash.Add(joint.SpringDamper); hash.Add(joint.SpringTarget);
        }
        return new(hash.Value.ToString("x16", CultureInfo.InvariantCulture));
    }).Value;
}
