using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Rendering;
using Meitou.Rendering.Characters;
using Meitou.Simulation;
using Meitou.Simulation.Carrying;

namespace Meitou.Game;

sealed partial class GameHost
{
    Matrix4x4 TorsoGripPlacement(TorsoFall fall, int side, CharacterId holder, CharacterSnapshot carrier,
        Vector3 at, float yaw, Matrix4x4 placed, CharacterRenderer renderer, long key, in CharacterTorsoCut cut)
    {
        if (TorsoCarrierPalm(renderer, carrier, at, yaw) is not { } palm) return placed;
        if (!fall.Grips.TryGetValue(side, out var grip) || grip.Holder != holder)
        {
            if (!Matrix4x4.Invert(placed, out var inverse)
                || renderer.ClosestTorsoSurfacePoint(key, cut, palm, placement: placed, preferUpperBand: true) is not { } contact) return placed;
            grip = (holder, Vector3.Transform(contact, inverse));
            fall.Grips[side] = grip;
        }
        // The frozen model-space point belongs to this pickup. Re-selecting a nearest point every frame would slide the hand along the fragment.
        return placed * Matrix4x4.CreateTranslation(palm - Vector3.Transform(grip.Point, placed));
    }

    Vector3? TorsoCarrierPalm(CharacterRenderer renderer, CharacterSnapshot carrier, Vector3 at, float yaw)
    {
        long key = DrawKey(carrier.Id);
        if (renderer.LastDrawn(key, "Bip01 L Hand") is not { } hand) return null;
        var rebase = Matrix4x4.Identity;
        if (carrier.Appearance is { } look && renderer.Rigs.For(look) is { } setup && Matrix4x4.Invert(hand.World, out var inverse))
            rebase = inverse * CharacterRigs.Placement(setup, at, yaw, carrier.Scale);
        Vector3 Move(Vector3 point) => Vector3.Transform(point, rebase);
        Vector3? finger = renderer.BonePosition(key, "Bip01 L Finger0") ?? renderer.BonePosition(key, "Bip01 L Finger1");
        Vector3? forearm = renderer.BonePosition(key, "Bip01 L Forearm");
        // Source is filled before the next renderer pose: rebase the posed hand to this frame's carrier node to remove walking/yaw lag.
        return CarryPose.PalmPoint(Move(hand.Bone), finger is { } f ? Move(f) : null, forearm is { } arm ? Move(arm) : null);
    }
}
