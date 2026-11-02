using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.Game;

sealed partial class GameHost
{
    int robotSparkBursts;

    void EmitRobotCutSparks(in CombatEvent hit, CharacterCold? victim)
    {
        if (!RobotCutEffects.EmitsSparks(hit, victim?.Race?.IsRobot == true)
            || gpu?.Particles is not { } particles || victim?.Medical is not { } medical
            || hit.Part < 0 || hit.Part >= medical.Parts.Count) return;
        var part = medical.Parts[hit.Part].Template;
        var at = gpu.Characters?.BonePosition(DrawKey(hit.Victim), RobotCutEffects.ImpactBone(part, hit.LowStrike))
            ?? hit.Position + new Vector3(0, RobotCutEffects.FallbackHeight(part), 0);
        if (!float.IsFinite(at.LengthSquared())) return;
        float length = hit.Direction.LengthSquared();
        var travel = float.IsFinite(length) && length > 1e-6f ? hit.Direction / MathF.Sqrt(length) : Vector2.UnitX;
        var along = Vector3.Normalize(new Vector3(travel.X, 0.45f, travel.Y));
        float force = Math.Clamp(hit.Damage.Cut / 70, 0, 1);
        particles.Burst(SparksSystem, at, Turn(Vector3.UnitZ, along), 0.16f, 0.8f + force * 0.5f, 1.2f);
        robotSparkBursts++;
    }

    void ReportRobotScene(int frame)
    {
        if (sandboxFoe.IsNone || !session.World.Characters.IsAlive(sandboxFoe)
            || session.World.Characters.Cold(sandboxFoe.Slot)?.Race?.IsRobot != true) return;
        Console.WriteLine($"robot cut frame {frame}: spark bursts {robotSparkBursts}, organic bowel props {gutFalls.Count}");
    }
}
