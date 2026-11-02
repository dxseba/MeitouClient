using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Carrying;
using Meitou.Simulation.Wounds;

namespace Meitou.Game;

sealed partial class GameHost
{
    Vector3 carryReferenceAt;
    float carryReferenceYaw;

    // A real intact lift followed by each clipped half on the same carrier. No authored poses are substituted.
    Vector3? StageCarryReference()
    {
        if (!IsSandbox || !g.SandboxBisect || g.Sandbox < 2)
            throw new ArgumentException("--scene carry-reference needs --sandbox 2 --sandbox-bisect");
        var world = session.World;
        world.Characters.Cold(sandboxFoe.Slot)!.Medical!.KillFromBisection();
        AdvanceScene(120);
        world.Commands.Enqueue(new PickUpOrder([sandboxHero], sandboxFoe) { Tick = world.Tick });
        AdvanceScene(180);
        var hero = session.CurrentSnapshot.Characters.First(c => c.Id == sandboxHero);
        if (session.CurrentSnapshot.Characters.First(c => c.Id == sandboxFoe).CarriedBy != sandboxHero)
            throw new InvalidOperationException("The intact reference body was not lifted.");
        carryReferenceAt = hero.Position;
        carryReferenceYaw = hero.Yaw;
        return carryReferenceAt + new Vector3(0, 10, 0);
    }

    void StepCarryReference(int frame)
    {
        var world = session.World;
        if (frame == 1)
        {
            // The fixture clips the already carried reference so its surviving support is directly comparable.
            world.Characters.Cold(sandboxFoe.Slot)!.TorsoBisection = TorsoBisectionPolicy.StateForDirection(
                g.SandboxCutDirection ?? 2, g.SandboxBisectVariant ?? 1, style: g.SandboxBisectStyle);
            AdvanceScene(120);
        }
        else if (frame == 2)
        {
            world.Commands.Enqueue(new PutDownOrder([sandboxHero]) { Tick = world.Tick });
            AdvanceScene(120);
            world.Commands.Enqueue(new PickUpOrder([sandboxHero], sandboxFoe) { Tick = world.Tick, TorsoSide = 1 });
            AdvanceScene(180);
            // Keep the same camera-facing location; the real carry animation continues normally.
            world.Characters.Place(sandboxHero.Slot, carryReferenceAt, carryReferenceYaw);
            AdvanceScene(120);
        }
        var body = session.CurrentSnapshot.Characters.First(c => c.Id == sandboxFoe);
        bool held = frame < 2 ? body.CarriedBy == sandboxHero : body.TorsoPositiveCarriedBy == sandboxHero;
        if (!held) throw new InvalidOperationException($"The reference carry state failed at frame {frame}.");
        Console.WriteLine($"carry reference   frame {frame}: {(frame == 0 ? "whole" : frame == 1 ? "negative half" : "positive half")}; carrier {carryReferenceAt}, yaw {carryReferenceYaw:F4}");
    }
}
