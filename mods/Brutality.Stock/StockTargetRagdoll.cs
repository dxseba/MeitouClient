using System.Collections;
using System.Numerics;
using BepuPhysics;
using HarmonyLib;
using Meitou.Data.Characters;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Ragdolls;

namespace Meitou.Brutality;

/// <summary>Lets an explicit landed blow move an existing whole-body ragdoll at the actual struck bone.</summary>
internal static class StockTargetRagdoll
{
    internal const int MaxHitsPerTick = 12;
    const int MaxRestarts = 2, TileBudget = 4;
    const float TileReach = 12;
    static readonly bool trace = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_TRACE_HITS") == "1";

    static readonly Type systemType = typeof(RagdollSystem);
    static readonly Type bodyType = AccessTools.Inner(systemType, "Ragdoll");
    static readonly System.Reflection.FieldInfo map = AccessTools.Field(systemType, "_ragdolls"),
        scene = AccessTools.Field(systemType, "_physics"), environment = AccessTools.Field(systemType, "_environment"),
        options = AccessTools.Field(systemType, "_options");
    static readonly System.Reflection.FieldInfo simulation = AccessTools.Field(scene.FieldType, "Simulation"),
        mask = AccessTools.Field(bodyType, "Mask"), frozen = AccessTools.Field(bodyType, "Frozen"), age = AccessTools.Field(bodyType, "Age"),
        handles = AccessTools.Field(bodyType, "Bodies"), dynamic = AccessTools.Field(bodyType, "Dynamic"),
        pose = AccessTools.Field(bodyType, "Pose"), scale = AccessTools.Field(bodyType, "Scale"), definition = AccessTools.Field(bodyType, "Definition"),
        setup = AccessTools.Field(bodyType, "Setup");
    static readonly System.Reflection.MethodInfo covered = AccessTools.Method(scene.FieldType, "Covered", [typeof(Vector3), typeof(float)]),
        cover = AccessTools.Method(scene.FieldType, "Cover", [typeof(Vector3), typeof(float), typeof(IRagdollEnvironment), typeof(long), typeof(int).MakeByRefType()]),
        rebuild = AccessTools.Method(systemType, "Rebuild", [bodyType, typeof(RagdollPart), typeof(RigidTransform[]), typeof(RagdollBlow), typeof(bool), typeof(ulong)]),
        struck = AccessTools.Method(systemType, "Struck", [bodyType, typeof(string)]),
        start = AccessTools.Method(systemType, "StartVelocity", [bodyType, typeof(int), typeof(RagdollBlow), typeof(ValueTuple<int, float>), typeof(ulong)]);

    internal static Vector3? PartPoint(World world, CharacterId target, int part)
    {
        if (!world.Characters.TryResolveNext(target, out int slot) || world.Characters.Cold(slot)?.Medical is not { } medical
            || (uint)part >= (uint)medical.Parts.Count || world.Systems.OfType<RagdollSystem>().FirstOrDefault() is not { } ragdolls
            || map.GetValue(ragdolls) is not IDictionary bodies || bodies[target] is not { } body
            || (RagdollPart)mask.GetValue(body)! != RagdollPart.Whole || setup.GetValue(body) is not CharacterPoseSetup current
            || pose.GetValue(body) is not RigidTransform[] poses || definition.GetValue(body) is not RagdollDefinition def) return null;
        var template = medical.Parts[part].Template;
        var rig = current.Rig;
        string side = template.Side == BodySide.Left ? "L" : "R";
        int? bone = rig.Handle(template.BoneName) ?? rig.Handle(template.BoneName2) ?? rig.Handle(template.Type switch
        {
            BodyPartType.Head => "Bip01 Head", BodyPartType.Arm => $"Bip01 {side} Forearm",
            BodyPartType.Leg => $"Bip01 {side} Calf", _ => "Bip01 Spine1",
        });
        if (bone is not { } h || (uint)h >= (uint)def.BodyOfBone.Length) return null;
        var bind = rig.BindPosition[h] * (float)scale.GetValue(body)!;
        while (h >= 0 && def.BodyOfBone[h] < 0) h = rig.Parent[h];
        if (h < 0 || (uint)def.BodyOfBone[h] >= (uint)poses.Length) return null;
        int actor = def.BodyOfBone[h];
        var point = poses[actor].Apply(def.Bodies[actor].Bind.Inverse().Apply(bind));
        return float.IsFinite(point.LengthSquared()) ? point : null;
    }

    internal static void Apply(World world, IReadOnlyList<CombatHit> hits)
    {
        if (hits.Count == 0 || rebuild is null || struck is null || start is null || covered is null || cover is null
            || world.Systems.OfType<RagdollSystem>().FirstOrDefault() is not { } ragdolls
            || options.GetValue(ragdolls) is not BodyOptions { AdvancedRagdolls: true }
            || map.GetValue(ragdolls) is not IDictionary bodies || scene.GetValue(ragdolls) is not { } physics
            || simulation.GetValue(physics) is not BepuPhysics.Simulation sim) return;

        int budget = TileBudget, restarts = 0;
        foreach (var hit in hits)
        {
            if (!world.Characters.TryResolveNext(hit.Victim, out int slot)
                || world.Characters.Cold(slot) is not { InSomething: HeldState.None, CarriedBy.IsNone: true, Medical: { Incapacitated: true } medical } cold
                || bodies[hit.Victim] is not { } body || (RagdollPart)mask.GetValue(body)! != RagdollPart.Whole
                || (uint)hit.Part >= (uint)medical.Parts.Count) continue;
            float force = hit.Damage.Cut + hit.Damage.Blunt + hit.Damage.Pierce;
            var travel = new Vector3(hit.Travel.X, 0, hit.Travel.Z);
            if (!float.IsFinite(force) || force < 1 || travel.LengthSquared() is not (> 1e-6f and < float.MaxValue)) continue;
            float speed = Math.Clamp(force * .20f, 1.5f, 14f) * (cold.Race?.IsRobot == true ? .42f : 1);
            var impulse = (Vector3.Normalize(travel) + Vector3.UnitY * .10f) * speed;
            var template = medical.Parts[hit.Part].Template;
            var blow = new RagdollBlow(impulse, impulse, string.IsNullOrEmpty(template.BoneName) ? template.BoneName2 : template.BoneName);

            bool thawed = frozen.GetValue(body) is true;
            if (thawed)
            {
                if (restarts >= MaxRestarts || environment.GetValue(ragdolls) is not IRagdollEnvironment ground
                    || pose.GetValue(body) is not RigidTransform[] poses || definition.GetValue(body) is not RagdollDefinition def) continue;
                var root = poses[def.Root].Position;
                float reach = TileReach * (float)scale.GetValue(body)!;
                if (covered.Invoke(physics, [root, reach]) is not true)
                {
                    if (budget <= 0) continue;
                    object[] args = [root, reach, ground, world.Tick, budget];
                    cover.Invoke(physics, args);
                    budget = (int)args[4];
                    if (covered.Invoke(physics, [root, reach]) is not true) continue;
                }
                // The stock builder reuses the previous actors' poses. Rebuild their joints only for a genuinely frozen body.
                rebuild.Invoke(ragdolls, [body, RagdollPart.Whole, poses, default(RagdollBlow), true, world.Seed]);
                restarts++;
            }

            var focus = ((int, float))struck.Invoke(null, [body, blow.Bone])!;
            if (handles.GetValue(body) is not BodyHandle?[] actors || dynamic.GetValue(body) is not bool[] moving) continue;
            for (int index = 0; index < actors.Length; index++)
            {
                if (actors[index] is not { } actor || !moving[index]) continue;
                var reference = sim.Bodies.GetBodyReference(actor);
                if (!reference.Awake) reference.Awake = true;
                var add = (BodyVelocity)start.Invoke(null, [body, index, blow, focus, world.Seed])!;
                reference.Velocity.Linear += add.Linear;
                reference.Velocity.Angular += add.Angular;
            }
            age.SetValue(body, 0f);
            if (trace) Console.WriteLine($"Brutality stock target ragdoll victim={hit.Victim} part={hit.Part} bone={blow.Bone} thawed={thawed}");
        }
    }
}
