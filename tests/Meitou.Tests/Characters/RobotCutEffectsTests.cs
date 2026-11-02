using System.Numerics;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Game;
using Meitou.Simulation;
using Meitou.Simulation.Combat;
using static Meitou.Tests.Bodies.BodyFixtures;

namespace Meitou.Tests.Characters;

public class RobotCutEffectsTests
{
    static CombatEvent Slash => new(CombatEventKind.Hit, 4, CombatHitSource.Melee, new CharacterId(1, 1), new CharacterId(2, 1),
        0, -1, Vector3.Zero, Vector2.UnitX, new(25, 0, 0), CutDirection: 2, Mechanism: CombatWoundMechanism.Slash);

    [Fact]
    public void Only_delivered_robot_blade_contacts_emit_and_medical_notifications_cannot_duplicate_the_spark()
    {
        Assert.True(RobotCutEffects.EmitsSparks(Slash, robot: true));
        Assert.True(RobotCutEffects.EmitsSparks(Slash with { Source = CombatHitSource.Execution }, robot: true));
        Assert.False(RobotCutEffects.EmitsSparks(Slash, robot: false));
        CombatEvent[] excluded =
        [
            Slash with { Source = CombatHitSource.Bolt }, Slash with { Mechanism = CombatWoundMechanism.Thrust },
            Slash with { Mechanism = CombatWoundMechanism.Crush }, Slash with { Damage = new(0, 200, 100) },
            Slash with { Damage = new(-1, 0, 0) }, Slash with { Damage = new(float.NaN, 0, 0) },
            Slash with { Damage = new(float.PositiveInfinity, 0, 0) },
        ];
        foreach (var contact in excluded) Assert.False(RobotCutEffects.EmitsSparks(contact, robot: true));
        foreach (var kind in Enum.GetValues<CombatEventKind>().Where(k => k != CombatEventKind.Hit))
            Assert.False(RobotCutEffects.EmitsSparks(Slash with { Kind = kind }, robot: true));
        Assert.True(RobotCutEffects.EmitsSparks(Slash with { Mechanism = CombatWoundMechanism.Unspecified }, robot: true));
        Assert.False(RobotCutEffects.EmitsSparks(Slash with { Mechanism = CombatWoundMechanism.Unspecified, CutDirection = 4 }, robot: true));
    }

    [Fact]
    public void Sparks_anchor_to_the_struck_robot_part_including_lower_limb_and_stomach_contacts()
    {
        var stomach = Stomach with { StringId = BodyPartTemplate.StomachPartId };
        Assert.Equal("Bip01 Head", RobotCutEffects.ImpactBone(Head));
        Assert.Equal("Bip01 Spine", RobotCutEffects.ImpactBone(stomach));
        Assert.Equal("Bip01 Spine2", RobotCutEffects.ImpactBone(Chest));
        Assert.Equal("Bip01 L UpperArm", RobotCutEffects.ImpactBone(LeftArm));
        Assert.Equal("Bip01 R Forearm", RobotCutEffects.ImpactBone(RightArm, lowStrike: true));
        Assert.Equal("Bip01 L Thigh", RobotCutEffects.ImpactBone(LeftLeg));
        Assert.Equal("Bip01 R Calf", RobotCutEffects.ImpactBone(RightLeg, lowStrike: true));
        Assert.True(RobotCutEffects.FallbackHeight(Head) > RobotCutEffects.FallbackHeight(Chest));
        Assert.True(RobotCutEffects.FallbackHeight(Chest) > RobotCutEffects.FallbackHeight(stomach));
        Assert.True(RobotCutEffects.FallbackHeight(LeftArm) > RobotCutEffects.FallbackHeight(LeftLeg));
        foreach (var part in new[] { Head, Chest, stomach, LeftArm, RightArm, LeftLeg, RightLeg })
            Assert.InRange(RobotCutEffects.FallbackHeight(part), 1, 20);
    }
}
