using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Rendering;

namespace Meitou.Brutality.Effects;

/// <summary>The host's build-adjusted shape and placement, kept together so a fluid anchor follows the rendered skin.</summary>
internal sealed class EffectSkinPose(SkeletonRig rig,
    Func<CharacterAppearance, BodyBuild, (BodyShapeData? Shape, float GroundOffset)?> reshape)
{
    public readonly Matrix4x4[] Bones = new Matrix4x4[rig.BoneCount];
    public BodyShapeData? Shape { get; private set; }
    public Matrix4x4 World { get; private set; }
    CharacterPoseSetup? source;
    BodyBuild? applied;
    float groundOffset;

    public void Update(in CharacterInstance instance, CharacterPoseSetup setup)
    {
        bool sameBuild = instance.Build is { } next ? applied is { } previous && previous.Near(next) : applied is null;
        if (!ReferenceEquals(source, setup) || !sameBuild)
        {
            source = setup; applied = instance.Build;
            var shaped = instance.Build is { } build ? reshape(instance.Appearance, build) : null;
            Shape = shaped?.Shape ?? setup.Shape;
            groundOffset = shaped?.GroundOffset ?? setup.GroundOffset;
        }
        if (instance.World is { } physical) { World = physical; return; }
        var placed = instance.Rotation is { } turn
            ? Matrix4x4.CreateTranslation(0, -groundOffset, 0) * Matrix4x4.CreateFromQuaternion(turn) * Matrix4x4.CreateTranslation(instance.Position)
            : Matrix4x4.CreateRotationY(instance.Yaw) * Matrix4x4.CreateTranslation(instance.Position - Vector3.UnitY * groundOffset);
        float scale = instance.Scale > 0 ? instance.Scale : 1;
        World = scale == 1 ? placed : Matrix4x4.CreateScale(scale) * placed;
    }
}
