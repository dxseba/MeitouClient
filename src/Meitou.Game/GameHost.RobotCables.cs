using System.Numerics;

using Meitou.Rendering;
using Meitou.Rendering.Characters;
using Meitou.Simulation;

namespace Meitou.Game;

sealed partial class GameHost
{
    sealed class RobotCable
    {
        public required long Key;
        public required Vector3[] Nodes, Previous;
        public required RobotCablePose Pose;
        public float Clock, Still;
        public bool Sleeping;
    }
    readonly Dictionary<long, RobotCable> robotCables = [];
    readonly HashSet<long> activeRobotCableKeys = [];
    readonly List<long> staleRobotCableKeys = [];

    void UpdateRobotCables(World world, float clock)
    {
        if (!GoreEnabled) return;
        if (o.NoParticles && !(IsFlat && g.FlatCombatParticles) || gpu?.Characters is not { } renderer || gpu.Debris is not { } debris) return;
        activeRobotCableKeys.Clear();
        foreach (var owner in session.CurrentSnapshot.Characters)
        {
            if (robotCables.Count >= DebrisRenderer.RobotCableLimit && owner.TorsoBisection is null && owner.DetachedRagdolls.Count == 0) continue;
            if (world.Characters.Cold(owner.Id.Slot)?.Race?.IsRobot != true) continue;
            if (owner.TorsoBisection is not null)
            {
                AttachRobotCable(world, renderer, TorsoDrawKey(owner.Id, 0), clock);
                AttachRobotCable(world, renderer, TorsoDrawKey(owner.Id, 1), clock);
            }
            foreach (var part in owner.DetachedRagdolls)
                AttachRobotCable(world, renderer, DetachedDrawKey(DrawKey(owner.Id), part.Part, part.FragmentId), clock);
        }
        staleRobotCableKeys.Clear();
        foreach (var pair in robotCables) if (!activeRobotCableKeys.Contains(pair.Key)) staleRobotCableKeys.Add(pair.Key);
        foreach (long key in staleRobotCableKeys) robotCables.Remove(key);
        debris.PrepareRobotCables(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(gpu.DebrisPieces));

    }

    void AttachRobotCable(World world, CharacterRenderer renderer, long key, float clock)
    {
        if (renderer.CutContact(key) is not { } contact) return;
        activeRobotCableKeys.Add(key);
        if (!robotCables.TryGetValue(key, out var cable))
        {
            if (robotCables.Count >= DebrisRenderer.RobotCableLimit) return;
            var nodes = new Vector3[DebrisRenderer.RobotCableNodes];
            for (int i = 0; i < nodes.Length; i++) nodes[i] = contact.Position + contact.Normal * (i * 0.14f) - Vector3.UnitY * (i * 0.16f);
            cable = new RobotCable
            {
                Key = key,
                Nodes = nodes,
                Previous = (Vector3[])nodes.Clone(),
                Clock = clock,
                Pose = new RobotCablePose(key, contact.Position, new Vector3[nodes.Length])
            };
            robotCables.Add(key, cable);
        }
        float dt = Math.Clamp(clock - cable.Clock, 0, 0.1f);
        cable.Clock = clock;
        float floor = world.Walkability.GroundHeight(contact.Position.X, contact.Position.Z, contact.Position.Y);
        if (!float.IsFinite(floor)) floor = gpu.Terrain.HeightAt(contact.Position.X, contact.Position.Z);
        if (Vector3.DistanceSquared(cable.Nodes[0], contact.Position) > 25)
        {
            cable.Sleeping = false;
            cable.Still = 0;
            var delta = contact.Position - cable.Nodes[0];
            for (int i = 0; i < cable.Nodes.Length; i++) { cable.Nodes[i] += delta; cable.Previous[i] += delta; }
        }
        if (dt > 0) StepRobotCable(cable, contact, floor, dt);
        cable.Nodes.CopyTo(cable.Pose.Nodes, 0);
        var pose = cable.Pose with { Centre = contact.Position };
        gpu.DebrisPieces.Add(new DebrisPiece(DebrisRenderer.RobotCable, 1, Quaternion.Identity, contact.Position, Cable: pose));
    }

    static void StepRobotCable(RobotCable cable, CharacterCutContact contact, float floor, float dt)
    {
        var anchor = contact.Position + contact.Normal * 0.04f;
        if (cable.Sleeping && Vector3.DistanceSquared(cable.Nodes[0], anchor) < 0.0004f) return;
        cable.Sleeping = false;
        int steps = Math.Clamp((int)MathF.Ceiling(dt * 60), 1, 6);
        float h = dt / steps;
        for (int step = 0; step < steps; step++)
        {
            cable.Nodes[0] = cable.Previous[0] = anchor;
            for (int i = 1; i < cable.Nodes.Length; i++)
            {
                var old = cable.Nodes[i];
                var velocity = (old - cable.Previous[i]) * MathF.Pow(0.80f, h * 60);
                cable.Nodes[i] = old + velocity - Vector3.UnitY * (42 * h * h);
                cable.Nodes[i].Y = MathF.Max(cable.Nodes[i].Y, floor + 0.06f);
                cable.Previous[i] = old;
            }
            for (int iteration = 0; iteration < 5; iteration++)
                for (int i = 1; i < cable.Nodes.Length; i++)
                {
                    var delta = cable.Nodes[i] - cable.Nodes[i - 1];
                    float length = delta.Length();
                    if (length <= 1e-6f) continue;
                    var correction = delta * ((length - 0.32f) / length);
                    if (i == 1) cable.Nodes[i] -= correction;
                    else { cable.Nodes[i - 1] += correction * 0.5f; cable.Nodes[i] -= correction * 0.5f; }
                }
            for (int i = 1; i < cable.Nodes.Length; i++)
                cable.Nodes[i].Y = MathF.Max(cable.Nodes[i].Y, floor + 0.06f);
        }
        float motion = 0;
        for (int i = 1; i < cable.Nodes.Length; i++) motion = MathF.Max(motion, Vector3.DistanceSquared(cable.Nodes[i], cable.Previous[i]));
        cable.Still = motion < 0.0001f ? cable.Still + dt : 0;
        cable.Sleeping = cable.Still > 0.6f;
    }
}