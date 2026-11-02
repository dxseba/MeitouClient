using System.Numerics;
using Meitou.Rendering.Characters;
using Meitou.Simulation;
using Meitou.Simulation.Carrying;

namespace Meitou.Game;

sealed partial class GameHost
{
    CharacterId torsoSceneOther = CharacterId.None;

    Vector3? StageTorsoCarry()
    {
        if (!IsSandbox || !g.SandboxBisect || g.Sandbox < 3)
            throw new ArgumentException("--scene torso-carry needs --sandbox 3 --sandbox-bisect");
        if (session.CurrentSnapshot.Characters.FirstOrDefault(c => c.Id == sandboxFoe)?.TorsoBisection is null)
            AdvanceScene((int)Math.Max(320 - session.World.Tick, 1));
        var corpse = session.CurrentSnapshot.Characters.First(c => c.Id == sandboxFoe);
        // Extra sandbox bystanders share the player's faction but are not normally controllable.
        torsoSceneOther = session.CurrentSnapshot.Characters.First(c => c.Id != sandboxHero && c.Id != sandboxFoe).Id;
        session.World.Characters.Cold(torsoSceneOther.Slot)!.IsPlayer = true;
        session.World.Commands.Enqueue(new DebugTeleport([sandboxHero], corpse.Position + new Vector3(6, 0, -5)) { Tick = session.World.Tick });
        session.World.Commands.Enqueue(new DebugTeleport([torsoSceneOther], (corpse.TorsoPositivePosition ?? corpse.Position) + new Vector3(6, 0, 5)) { Tick = session.World.Tick });
        AdvanceScene(2);
        return TorsoCarryFocus();
    }

    Vector3? TorsoCarryFocus()
    {
        if (session.CurrentSnapshot.Characters.FirstOrDefault(c => c.Id == sandboxFoe) is not { } corpse) return null;
        return (corpse.Position + (corpse.TorsoPositivePosition ?? corpse.Position)) * 0.5f
            + new Vector3(0, 4, 0);
    }

    // Compare real hand contact; the intact carry reference, rather than an invented biceps target, determines the body placement.
    void ReportTorsoGripContact(int frame)
    {
        if (gpu.Characters is not { } renderer
            || session.CurrentSnapshot.Characters.FirstOrDefault(c => c.Id == sandboxFoe) is not { TorsoBisection: { } cut } body) return;
        foreach (int side in new[] { 0, 1 })
        {
            var holder = side > 0 ? body.TorsoPositiveCarriedBy : body.CarriedBy;
            if (holder.IsNone) continue;
            long carrierKey = DrawKey(holder);
            if (renderer.BonePosition(carrierKey, "Bip01 L Hand") is not { } hand) continue;
            var finger = renderer.BonePosition(carrierKey, "Bip01 L Finger0") ?? renderer.BonePosition(carrierKey, "Bip01 L Finger1");
            var palm = CarryPose.PalmPoint(hand, finger, renderer.BonePosition(carrierKey, "Bip01 L Forearm"));
            long pieceKey = TorsoDrawKey(body.Id, side);
            int? component = null;
            var pose = side > 0 ? body.TorsoRagdoll?.Positive : body.TorsoRagdoll?.Negative;
            if (body.Appearance is { } look && renderer.Rigs.For(look) is { } setup && setup.Rig.Order.Length > 0
                && pose?.Components is { } groups && setup.Rig.Order[0] < groups.Length) component = groups[setup.Rig.Order[0]];
            var surface = renderer.ClosestTorsoSurfacePoint(pieceKey, new(cut.Plane, side > 0 ? 1 : -1, cut.FullBody), palm, component);
            if (surface is { } at)
                Console.WriteLine($"carry grip   frame {frame} side {side}: palm-to-retained-skin {Vector3.Distance(palm, at):F4}, palm {palm}, surface {at}; pelvis {renderer.BonePosition(pieceKey, "Bip01 Pelvis")}, L foot {renderer.BonePosition(pieceKey, "Bip01 L Foot")}, R foot {renderer.BonePosition(pieceKey, "Bip01 R Foot")}");
            if (torsoFalls.TryGetValue(body.Id, out var fall) && fall.CarryReferences.TryGetValue(side > 0 ? 1 : -1, out var held) && held.Reference is { } reference)
                Console.WriteLine($"carry reference   frame {frame} side {side}: reference-to-contact {Vector3.Distance(reference.ReferencePoint, reference.RetainedPoint + reference.Translation):F4}, translation {reference.Translation}, kept {reference.Translation == Vector3.Zero}");
        }
    }

    void StepTorsoCarryScene(int frame)
    {
        var world = session.World;
        if (frame is 1 or 2)
        {
            var carrier = frame == 1 ? sandboxHero : torsoSceneOther;
            int side = frame == 1 ? 1 : 0;
            world.Commands.Enqueue(new SelectCommand([carrier]) { Tick = world.Tick });
            AdvanceScene(1);
            var corpse = session.CurrentSnapshot.Characters.First(c => c.Id == sandboxFoe);
            var cut = corpse.TorsoBisection!.Value;
            var points = gpu.Characters?.TorsoCutBounds(TorsoDrawKey(sandboxFoe, side), new(cut.Plane, side == 1 ? 1 : -1, cut.FullBody)) ?? [];
            if (!player.ScenePickUpTorso(sandboxFoe, side, points))
                throw new InvalidOperationException($"The rendered torso half {side} could not be picked through its real context verb.");
            AdvanceScene(120);
            if (frame == 1)
            {
                var at = session.CurrentSnapshot.Characters.First(c => c.Id == sandboxHero).Position;
                world.Commands.Enqueue(new MoveOrder([sandboxHero], at + new Vector3(12, 0, -10)) { Tick = world.Tick });
                AdvanceScene(100);   // Keep the first holder from occluding the second rendered pickup.
            }
            if (frame == 2)
            {
                var at = session.CurrentSnapshot.Characters.First(c => c.Id == sandboxHero).Position;
                world.Commands.Enqueue(new MoveOrder([sandboxHero], at + new Vector3(-12, 0, -8)) { Tick = world.Tick });
                world.Commands.Enqueue(new MoveOrder([torsoSceneOther], at + new Vector3(12, 0, 8)) { Tick = world.Tick });
                AdvanceScene(60);   // Exercise turning and walking with both independently held halves.
            }
        }
        else if (frame is 3 or 4)
        {
            var carrier = frame == 3 ? sandboxHero : torsoSceneOther;
            world.Commands.Enqueue(new PutDownOrder([carrier]) { Tick = world.Tick });
            AdvanceScene(120);
        }
        var body = session.CurrentSnapshot.Characters.First(c => c.Id == sandboxFoe);
        var positiveHolder = frame is 1 or 2 ? sandboxHero : CharacterId.None;
        var negativeHolder = frame is 2 or 3 ? torsoSceneOther : CharacterId.None;
        if (body.TorsoPositiveCarriedBy != positiveHolder || body.CarriedBy != negativeHolder)
            throw new InvalidOperationException($"Independent torso carry state mismatch at frame {frame}.");
        Console.WriteLine($"torso carry   frame {frame}: positive holder {body.TorsoPositiveCarriedBy}, negative holder {body.CarriedBy}; positive {body.TorsoPositivePosition}, negative {body.Position}");
    }
}
