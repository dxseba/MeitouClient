using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Rendering;
using Meitou.Rendering.Characters;
using Meitou.Simulation;
using Meitou.Simulation.Carrying;

namespace Meitou.Game;

sealed partial class GameHost
{
    CharacterInstance TorsoCarryReferenceInstance(TorsoFall fall, int side, CharacterId holder, CharacterSnapshot carrier,
        in CharacterInstance instance, Vector3 carrierAt, float yaw, Vector3 heldAt, Quaternion heldRotation,
        CharacterRenderer renderer, long key, in CharacterTorsoCut cut)
    {
        if (renderer.Rigs.For(instance.Appearance) is not { } setup) return instance with { Key = key, TorsoCut = cut };
        if (side < 0)
        {
            // The ordinary draw instance already contains its interpolated CarryAttach placement.
            heldAt = instance.Position;
            heldRotation = instance.Rotation ?? Quaternion.CreateFromAxisAngle(Vector3.UnitY, instance.Yaw);
        }
        var world = CharacterRigs.Placement(setup, heldAt, heldRotation, instance.Scale);
        var poses = instance.Poses;
        var bones = instance.Bones;
        if (side > 0)
        {
            if (!poses.Any(p => p.Animation == CarrySystem.CarriedClip)) poses = [new(CarrySystem.CarriedClip, 0, 1)];
            // The other side's world ragdoll belongs to another placement; the virtual intact positive reference uses its carried clip.
            bones = null;
        }
        var reference = instance with { Key = key, Position = heldAt, World = world, Rotation = null, FreezeFrom = null, Poses = poses, Bones = bones, TorsoCut = null };
        if (!fall.GroundPalettes.ContainsKey(side) && renderer.CaptureFrozenTorsoPose(key) is { } original) fall.GroundPalettes[side] = original;
        var cached = fall.CarryReferences.GetValueOrDefault(side);
        if (cached.Holder != holder) cached = (holder, null);
        var context = cached.Reference;
        // Even a first frame without the carrier hand must restore this side's floor pose on release.
        fall.CarryReferences[side] = (holder, context);
        if (TorsoCarrierPalm(renderer, carrier, carrierAt, yaw) is not { } palm) return reference with { TorsoCut = cut };
        bool prepared = renderer.PrepareTorsoCarryReference(reference, cut, palm, ref context, out var carried, out var translation);
        fall.CarryReferences[side] = (holder, context);
        if (!prepared) return reference with { TorsoCut = cut };
        return reference with
        {
            Position = heldAt + translation, World = world * Matrix4x4.CreateTranslation(translation), Bones = carried,
            TorsoCut = cut, FullRateAnimation = true,
        };
    }

    static void RestoreTorsoGroundPose(TorsoFall fall, int side, long key, CharacterRenderer renderer)
    {
        if (fall.CarryReferences.Remove(side) && fall.GroundPalettes.TryGetValue(side, out var original)) renderer.RestoreFrozenTorsoPose(key, original);
    }

    Vector3? TorsoCarrierPalm(CharacterRenderer renderer, CharacterSnapshot carrier, Vector3 at, float yaw)
    {
        long key = DrawKey(carrier.Id);
        if (renderer.LastDrawn(key, "Bip01 L Hand") is not { } hand) return null;
        var rebase = Matrix4x4.Identity;
        if (carrier.Appearance is { } look && renderer.Rigs.For(look) is { } setup && Matrix4x4.Invert(hand.World, out var inverse))
            rebase = inverse * CharacterRigs.Placement(setup, at, yaw, carrier.Scale);
        Vector3 Move(Vector3 point) => Vector3.Transform(point, rebase);
        var finger = renderer.BonePosition(key, "Bip01 L Finger0") ?? renderer.BonePosition(key, "Bip01 L Finger1");
        var forearm = renderer.BonePosition(key, "Bip01 L Forearm");
        return CarryPose.PalmPoint(Move(hand.Bone), finger is { } f ? Move(f) : null, forearm is { } arm ? Move(arm) : null);
    }
}
