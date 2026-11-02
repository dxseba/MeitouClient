using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation.Combat;

namespace Meitou.Game;

/// <summary>Display-only contact effects: one cutting hit emits once, not again for its sever/death notifications.</summary>
internal static class RobotCutEffects
{
    public static bool EmitsSparks(in CombatEvent hit, bool robot) => robot && hit.Kind == CombatEventKind.Hit
        && hit.Source is CombatHitSource.Melee or CombatHitSource.Execution
        && float.IsFinite(hit.Damage.Cut) && hit.Damage.Cut > 0
        && (hit.Mechanism == CombatWoundMechanism.Slash
            || hit.Mechanism == CombatWoundMechanism.Unspecified && hit.CutDirection != 4);

    public static string ImpactBone(BodyPartTemplate part, bool lowStrike = false) => part.Type switch
    {
        BodyPartType.Head => "Bip01 Head",
        BodyPartType.Torso => part.StringId == BodyPartTemplate.StomachPartId ? "Bip01 Spine" : "Bip01 Spine2",
        _ => part.LimbSlot switch
        {
            0 => lowStrike ? "Bip01 L Forearm" : "Bip01 L UpperArm",
            1 => lowStrike ? "Bip01 R Forearm" : "Bip01 R UpperArm",
            2 => lowStrike ? "Bip01 L Calf" : "Bip01 L Thigh",
            3 => lowStrike ? "Bip01 R Calf" : "Bip01 R Thigh",
            _ => "Bip01 Spine",
        },
    };

    public static float FallbackHeight(BodyPartTemplate part) => part.Type == BodyPartType.Head ? 17
        : part.Type == BodyPartType.Torso ? part.StringId == BodyPartTemplate.StomachPartId ? 11 : 14
        : part.LimbSlot is 2 or 3 ? 6 : 12;
}
