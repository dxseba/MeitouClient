using System.Numerics;
using Meitou.Data.Characters;
using Meitou.Data.Physics;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Ragdolls;
using Meitou.Simulation.Wounds;
using static Meitou.Tests.Bodies.BodyFixtures;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Physics;

public partial class RagdollInstalledSkinTests
{
    [Theory]
    [InlineData(TorsoBisectionStyle.Oblique, 2, false)]
    [InlineData(TorsoBisectionStyle.Oblique, 2, true)]
    [InlineData(TorsoBisectionStyle.Vertical, 1, false)]
    [InlineData(TorsoBisectionStyle.Vertical, 1, true)]
    [Slow]
    public void Installed_cut_skin_and_sections_keep_shape_when_carriers_move_halves_apart(TorsoBisectionStyle style, int direction, bool missingLeg)
    {
        var (rig, definition, model, setup, rigs, look) = Installed();
        using var physics = new RagdollSystem(rigs, new Flat(), BodyOptions.Default with { AdvancedRagdolls = true }, _ => setup, (_, _, _) => definition);
        using var world = new SimWorld(new WorldSettings { Seed = 41 }, new OpenGroundWalkability((_, _) => 0), [physics]);
        const float yaw = -MathF.PI / 2;
        var cut = TorsoBisectionPolicy.StateForDirection(direction, 1, Vector2.UnitX, yaw, style);
        var cold = new CharacterCold
        {
            Name = "Carried skin test corpse", Race = Human, Medical = Healthy(), Appearance = look, TorsoBisection = cut,
            Blow = (new(50, 10, 0), new(50, 10, 0)),
        };
        if (missingLeg) cold.Medical.Sever(LArm + 2, Context(), false);
        cold.Medical.KillFromBisection();
        var id = world.Characters.Spawn(new() { Position = new(1000, 0, 1000), Yaw = yaw }, cold, 0);
        world.RunTicks(320);
        CharacterId Carrier(Vector3 at, int side)
        {
            var carrier = new CharacterCold { Name = $"Carrier {side}", Race = Human, Medical = Healthy(), Appearance = look, Carrying = id, CarryingTorsoSide = side };
            return world.Characters.Spawn(new() { Position = at, Health = 100, Yaw = side == 0 ? 0.6f : -0.9f }, carrier, world.Tick);
        }
        cold.CarriedBy = Carrier(new(1100, 0, 1000), 0);
        cold.TorsoPositiveCarriedBy = Carrier(new(900, 0, 1000), 1);
        CheckCutSkin(rig, model, setup, world, id, cut, style, [321, 350]);
        world.Commands.Enqueue(new DebugTeleport([cold.CarriedBy], new(1300, 0, 1100)) { Tick = world.Tick });
        world.Commands.Enqueue(new DebugTeleport([cold.TorsoPositiveCarriedBy], new(700, 0, 900)) { Tick = world.Tick });
        CheckCutSkin(rig, model, setup, world, id, cut, style, [351, 400]);
    }
}
