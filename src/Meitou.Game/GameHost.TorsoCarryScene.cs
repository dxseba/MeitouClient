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
        return (corpse.Position + (corpse.TorsoPositivePosition ?? corpse.Position)) * 0.5f + new Vector3(0, 4, 0);
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
