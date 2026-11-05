using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Rendering;

namespace Meitou.Brutality.Rendering;

internal readonly record struct PresentedPiece(int StableKey, CharacterInstance Instance);

/// <summary>Copied final presentation, held as one family until every replacement asset is ready. No simulation ownership lives here.</summary>
internal sealed class CutPresentationFamily
{
    const double BlendSeconds = .120;
    PresentedPiece[] pieces = [];
    readonly Dictionary<int, BonePose> starts = [];
    readonly Dictionary<int, (BonePose[] Poses, int Next)> blends = [];
    double transition = double.NegativeInfinity;
    internal bool IsHeld { get; private set; }
    internal IReadOnlyList<PresentedPiece> Pieces => pieces;
    internal bool HasPresentation => pieces.Length > 0;

    internal static bool Compatible(CharacterAppearance previous, CharacterAppearance current) =>
        ReferenceEquals(previous.Race, current.Race) && previous.Female == current.Female
            && string.Equals(previous.BodyMesh, current.BodyMesh, StringComparison.OrdinalIgnoreCase);

    internal static void RetireUnused(IEnumerable<RuntimeCharacterGeometry> previous, IReadOnlySet<RuntimeCharacterGeometry> next,
        Func<RuntimeCharacterGeometry, bool> displayed)
    {
        foreach (var geometry in previous) if (!next.Contains(geometry) && !displayed(geometry)) geometry.Retire();
    }

    internal static bool Ready(CharacterDrawList list, IReadOnlyList<PresentedPiece> candidate) => Ready(candidate, list.IsPrepared);
    internal static bool Ready(IReadOnlyList<PresentedPiece> candidate, Func<CharacterAppearance, RuntimeCharacterGeometry?, bool> ready) =>
        candidate.Count > 0 && candidate.All(piece => ready(piece.Instance.Appearance, piece.Instance.Geometry));

    internal bool Hold(CharacterAppearance current, Func<CharacterInstance, BonePose> worldBones)
    {
        if (pieces.Length == 0 || !Compatible(pieces[0].Instance.Appearance, current)) return false;
        if (!IsHeld)
        {
            starts.Clear();
            foreach (var piece in pieces) starts[piece.StableKey] = Copy(worldBones(piece.Instance), null)!;
            IsHeld = true;
        }
        return true;
    }

    internal void Accept(double now)
    {
        if (!IsHeld) return;
        IsHeld = false; transition = now;
    }

    internal void Capture(IReadOnlyList<PresentedPiece> source)
    {
        if (IsHeld) return;
        if (pieces.Length != source.Count) pieces = new PresentedPiece[source.Count];
        for (int i = 0; i < source.Count; i++)
        {
            var next = source[i]; var previous = pieces[i].Instance;
            var pose = Copy(next.Instance.Bones, previous.Bones);
            var clips = Copy(next.Instance.Poses, previous.Poses);
            var bolts = Copy(next.Instance.Bolts, previous.Bolts);
            pieces[i] = next with { Instance = next.Instance with { Bones = pose, Poses = clips, Bolts = bolts } };
        }
    }

    internal BonePose? Blend(int stableKey, BonePose? target, Matrix4x4 placement, double now)
    {
        if (target is null || !target.AbsolutePositions || now - transition >= BlendSeconds
            || !starts.TryGetValue(stableKey, out var from) && !starts.TryGetValue(0, out from)
            || from.Set.Length != target.Set.Length || !Matrix4x4.Invert(placement, out var inverse)) return target;
        float alpha = Math.Clamp((float)((now - transition) / BlendSeconds), 0, 1);
        alpha *= alpha * (3 - 2 * alpha);
        var turn = Rotation(placement);
        var unturn = Quaternion.Inverse(turn);
        if (!blends.TryGetValue(stableKey, out var scratch) || scratch.Poses[0].Set.Length != target.Set.Length)
            scratch = (new[] { new BonePose(target.Set.Length), new BonePose(target.Set.Length) }, 0);
        var result = scratch.Poses[scratch.Next ^= 1]; blends[stableKey] = scratch;
        result.Weight = target.Weight; result.AbsolutePositions = true; result.Components = target.Components;
        for (int bone = 0; bone < result.Set.Length; bone++)
        {
            result.Set[bone] = target.Set[bone];
            result.Position[bone] = target.Set[bone] && from.Set[bone]
                ? Vector3.Lerp(Vector3.Transform(from.Position[bone], inverse), target.Position[bone], alpha) : target.Position[bone];
            result.Rotation[bone] = target.Set[bone] && from.Set[bone]
                ? Quaternion.Slerp(Quaternion.Normalize(unturn * from.Rotation[bone]), target.Rotation[bone], alpha) : target.Rotation[bone];
        }
        return result;
    }

    internal static BonePose WorldBones(in CharacterInstance instance, CharacterPoseSetup setup, CharacterRigs rigs)
    {
        var shaped = instance.Build is { } build ? rigs.Reshape(instance.Appearance, build) : null;
        var rig = setup.Rig;
        var shape = shaped?.Shape ?? setup.Shape;
        float groundOffset = shaped?.GroundOffset ?? setup.GroundOffset;
        var layers = new List<ActiveLayer>();
        foreach (var clip in instance.Poses)
            if (rig.Layer(rigs.Database, clip.Animation, setup.Animal) is { } layer) layers.Add(new(layer, clip.Time, clip.Weight));
        foreach (var (layer, time) in setup.Fixed) layers.Add(new(layer, time, 1));
        var pose = rig.Rent();
        try
        {
            pose.PoseDerived(shape, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(layers), instance.Bones);
            var ordinary = instance.Rotation is { } rotation
                ? Matrix4x4.CreateTranslation(0, -groundOffset, 0) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(instance.Position)
                : Matrix4x4.CreateRotationY(instance.Yaw) * Matrix4x4.CreateTranslation(instance.Position - Vector3.UnitY * groundOffset);
            float scale = instance.Scale > 0 ? instance.Scale : 1;
            var placed = instance.World ?? (scale == 1 ? ordinary : Matrix4x4.CreateScale(scale) * ordinary);
            var turn = Rotation(placed);
            var result = new BonePose(rig.BoneCount) { AbsolutePositions = true };
            for (int bone = 0; bone < rig.BoneCount; bone++)
            {
                result.Set[bone] = true;
                result.Position[bone] = Vector3.Transform(pose.DerivedPosition[bone], placed);
                result.Rotation[bone] = Quaternion.Normalize(turn * pose.DerivedRotation[bone]);
            }
            return result;
        }
        finally { rig.Return(pose); }
    }

    internal static CharacterWorldBounds? Bounds(BonePose pose, Matrix4x4 placement)
    {
        Vector3 min = new(float.PositiveInfinity), max = new(float.NegativeInfinity);
        bool any = false;
        for (int bone = 0; bone < pose.Set.Length; bone++)
            if (pose.Set[bone])
            {
                var point = Vector3.Transform(pose.Position[bone], placement);
                min = Vector3.Min(min, point); max = Vector3.Max(max, point); any = true;
            }
        return any ? new((min + max) * .5f, MathF.Max(1, Vector3.Distance(min, max) * .5f + 3)) : null;
    }

    static Quaternion Rotation(Matrix4x4 placed) => Matrix4x4.Decompose(placed, out _, out var turn, out _)
        ? Quaternion.Normalize(turn) : Quaternion.Identity;

    static BonePose? Copy(BonePose? source, BonePose? previous)
    {
        if (source is null) return null;
        var copy = previous is not null && previous.Set.Length == source.Set.Length ? previous : new BonePose(source.Set.Length);
        source.Set.CopyTo(copy.Set, 0); source.Position.CopyTo(copy.Position, 0); source.Rotation.CopyTo(copy.Rotation, 0);
        copy.AbsolutePositions = source.AbsolutePositions; copy.Weight = source.Weight; copy.Components = source.Components;
        return copy;
    }

    static T[] Copy<T>(IReadOnlyList<T> source, IReadOnlyList<T>? previous)
    {
        var copy = previous is T[] old && old.Length == source.Count ? old : new T[source.Count];
        for (int i = 0; i < copy.Length; i++) copy[i] = source[i];
        return copy;
    }
}
