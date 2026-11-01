using System.Numerics;
using Meitou.Simulation;

namespace Meitou.Game;

sealed partial class GameHost
{
    float downedSceneFlesh;

    Vector3? StageDownedAttack()
    {
        if (!IsSandbox || g.Sandbox < 2 || g.SandboxDummy)
            throw new ArgumentException("--scene downed-attack needs --sandbox 2 without dummy wounds");
        var body = session.World.Characters.Cold(sandboxFoe.Slot)?.Medical
            ?? throw new InvalidOperationException("The staged enemy has no medical state.");
        body.KnockOut(600);
        AdvanceScene(180);
        downedSceneFlesh = body.Parts.Sum(p => p.Flesh);
        session.World.Commands.Enqueue(new SelectCommand([sandboxHero]) { Tick = session.World.Tick });
        AdvanceScene(1);
        return session.CurrentSnapshot.Characters.First(c => c.Id == sandboxFoe).Position;
    }

    void StepDownedAttackScene(int frame)
    {
        var body = session.World.Characters.Cold(sandboxFoe.Slot)!.Medical!;
        if (frame == 1)
        {
            if (!player.SceneAttackDowned(sandboxFoe)) throw new InvalidOperationException("The living downed enemy has no Attack body part / Chest action.");
            long orderedAt = session.World.Tick;
            bool HitLanded() => woundQueue.Any(hit => hit.Tick >= orderedAt && hit.Kind == Meitou.Simulation.Combat.CombatEventKind.Hit
                && hit.Source == Meitou.Simulation.Combat.CombatHitSource.Melee && hit.Attacker == sandboxHero && hit.Victim == sandboxFoe
                && hit.Damage.Cut + hit.Damage.Blunt + hit.Damage.Pierce > 0);
            int remaining = 360;
            while (remaining-- > 0 && !HitLanded()) AdvanceScene(1);
            if (!HitLanded())
                throw new InvalidOperationException("The explicit attack did not damage the living downed enemy.");
        }
        else if (frame > 1) AdvanceScene(60);
        var hero = session.CurrentSnapshot.Characters.First(c => c.Id == sandboxHero);
        var foe = session.CurrentSnapshot.Characters.First(c => c.Id == sandboxFoe);
        Console.WriteLine($"downed attack   frame {frame}: flesh {body.Parts.Sum(p => p.Flesh):0.00}/{downedSceneFlesh:0.00}, dead {body.Dead}, distance {Vector3.Distance(hero.Position, foe.Position):0.00}, animation {string.Join(",", hero.Animations.Select(a => a.Name))}");
    }
}
