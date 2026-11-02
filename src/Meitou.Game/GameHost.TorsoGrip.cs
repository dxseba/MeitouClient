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
        if (TorsoCarrierSupport(renderer, carrier, at, yaw) is not { } support) return placed;
        if (!fall.Grips.TryGetValue(side, out var grip) || grip.Holder != holder)
        {
            if (!Matrix4x4.Invert(placed, out var inverse)
                || renderer.ClosestTorsoSurfacePoint(key, cut, support, placement: placed, preferMiddle: true) is not { } contact) return placed;
            grip = (holder, Vector3.Transform(contact, inverse));
            fall.Grips[side] = grip;
        }
        // Keep the existing frozen pose and carry rotation; only move its cached central surface point onto the arm support.
        return placed * Matrix4x4.CreateTranslation(support - Vector3.Transform(grip.Point, placed));
    }

    Vector3? TorsoCarrierSupport(CharacterRenderer renderer, CharacterSnapshot carrier, Vector3 at, float yaw)
    {
        long key = DrawKey(carrier.Id);
        if (renderer.LastDrawn(key, "Bip01 L UpperArm") is not { } upper || renderer.BonePosition(key, "Bip01 L Forearm") is not { } forearm) return null;
        var rebase = Matrix4x4.Identity;
        if (carrier.Appearance is { } look && renderer.Rigs.For(look) is { } setup && Matrix4x4.Invert(upper.World, out var inverse))
            rebase = inverse * CharacterRigs.Placement(setup, at, yaw, carrier.Scale);
        Vector3 Move(Vector3 point) => Vector3.Transform(point, rebase);
        // Rebase the posed biceps to this frame's carrier node to remove walking/yaw lag.
        return CarryPose.BicepsSupport(Move(upper.Bone), Move(forearm));
    }
}
