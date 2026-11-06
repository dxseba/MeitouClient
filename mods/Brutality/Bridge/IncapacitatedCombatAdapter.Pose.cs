using System.Collections;
using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Brutality.Physics;
using Meitou.Data.Characters;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Native = Meitou.Simulation.Ragdolls;

namespace Meitou.Brutality;

public sealed partial class IncapacitatedCombatAdapter
{
    static readonly Type NativeBodyType = typeof(Native.RagdollSystem).GetNestedType("Ragdoll", BindingFlags.NonPublic)!;
    static readonly FieldInfo NativeMap = AccessTools.Field(typeof(Native.RagdollSystem), "ragdolls");
    static readonly FieldInfo NativeSetup = AccessTools.Field(NativeBodyType, "Setup"), NativeDefinition = AccessTools.Field(NativeBodyType, "Definition"),
        NativeScale = AccessTools.Field(NativeBodyType, "Scale"), NativePose = AccessTools.Field(NativeBodyType, "Pose");

    static bool TryPartPosition(World world, CharacterId id, CharacterCold cold, int part, out Vector3 position)
    {
        position = default;
        var template = cold.Medical!.Parts[part].Template;
        if (world.Systems.OfType<FragmentPhysicsSystem>().FirstOrDefault() is { } fragments && fragments.TryPartPosition(id, template, out position)) return true;
        if (world.Systems.OfType<Native.RagdollSystem>().FirstOrDefault() is not { } physics || !physics.WasWhole(id)
            || ((IDictionary)NativeMap.GetValue(physics)!)[id] is not { } body) return false;
        var setup = (CharacterPoseSetup)NativeSetup.GetValue(body)!;
        var definition = (Native.RagdollDefinition)NativeDefinition.GetValue(body)!;
        var poses = (Native.RigidTransform[])NativePose.GetValue(body)!;
        float scale = (float)NativeScale.GetValue(body)!;
        var rig = setup.Rig;
        int? bone = rig.Handle(template.BoneName) ?? rig.Handle(template.BoneName2);
        if (bone is null)
        {
            string side = template.Side == BodySide.Left ? "L" : "R";
            bone = rig.Handle(template.Type switch { BodyPartType.Head => "Bip01 Head", BodyPartType.Arm => $"Bip01 {side} Forearm",
                BodyPartType.Leg => $"Bip01 {side} Calf", _ => "Bip01 Spine1" });
        }
        int actor = bone is { } h && h < definition.BodyOfBone.Length ? definition.BodyOfBone[h] : -1;
        var bind = bone is { } b ? rig.BindPosition[b] * scale : definition.Bodies[definition.Root].Bind.Position;
        if (actor < 0)
        {
            float nearest = float.MaxValue;
            for (int i = 0; i < definition.Bodies.Length; i++)
                if (Vector3.DistanceSquared(bind, definition.Bodies[i].Bind.Position) is var distance && distance < nearest) (actor, nearest) = (i, distance);
        }
        if ((uint)actor >= (uint)poses.Length) return false;
        position = poses[actor].Apply(definition.Bodies[actor].Bind.Inverse().Apply(bind));
        return float.IsFinite(position.X) && float.IsFinite(position.Y) && float.IsFinite(position.Z);
    }
}
