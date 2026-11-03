using System.Numerics;

namespace Meitou.Simulation.Ragdolls;

public sealed partial class RagdollSystem
{
    public int ElectricalSpasms { get; private set; }

    // Three finite jolts shortly after a mechanical severing; never keep a settled corpse alive indefinitely.
    void ApplyElectricalSpasms(World world)
    {
        if (options is not { AdvancedRagdolls: true }) return;
        int budget = 6;
        foreach (var id in torsoOrder)
        {
            var halves = torsoRagdolls[id];
            if (!world.Characters.TryResolveNext(id, out int slot) || world.Characters.Cold(slot) is not { Race.IsRobot: true }) continue;
            Pulse(halves.Positive, halves.PositiveCarried);
            Pulse(halves.Negative, halves.NegativeCarried);
        }
        foreach (var id in detachedOrder)
        {
            if (!world.Characters.TryResolveNext(id, out int slot) || world.Characters.Cold(slot) is not { Race.IsRobot: true } cold) continue;
            var owner = detachedOwners[id];
            foreach (var part in owner.Pieces) Pulse(part.Body, false);
            if (ragdolls.TryGetValue(id, out var body) && (body.Mask & RagdollPart.CarryMode) == 0
                && cold.Medical is { Incapacitated: true }) Pulse(body, !cold.CarriedBy.IsNone);
        }

        void Pulse(Ragdoll piece, bool carried)
        {
            if (piece.ElectricalPulses >= 3 || carried || piece.Bodies.Length == 0) return;
            if (piece.ElectricalBorn < 0) piece.ElectricalBorn = world.Tick;
            float age = (world.Tick - piece.ElectricalBorn) * world.TickSeconds;
            float due = piece.ElectricalPulses == 0 ? 0.65f : piece.ElectricalPulses == 1 ? 2.1f : 4.4f;
            if (age < due) return;
            if (age > 6) { piece.ElectricalPulses = 3; return; }
            if (budget <= 0 || piece.Frozen) return;
            piece.ElectricalPulses++;
            uint seed = unchecked((uint)piece.Id.Slot * 747796405u + (uint)piece.Id.Generation * 2891336453u
                + (uint)piece.Definition.Bodies.Length * 277803737u + piece.ElectricalPulses * 97u);
            int start = (int)(seed % (uint)piece.Bodies.Length);
            for (int k = 0; k < piece.Bodies.Length; k++)
            {
                int index = (start + k) % piece.Bodies.Length;
                if (!piece.Dynamic[index] || piece.Bodies[index] is not { } handle) continue;
                var body = physics.Simulation.Bodies[handle];
                body.Awake = true;
                body = physics.Simulation.Bodies[handle];
                if (body.LocalInertia.InverseMass <= 0) continue;
                float side = (seed & 1) == 0 ? 1 : -1;
                body.ApplyImpulse(new Vector3(side * 1.2f, 1.6f, -side * 0.7f) / body.LocalInertia.InverseMass, new Vector3(0.08f, 0, 0.05f));
                piece.Age = MathF.Min(piece.Age, 2);
                budget--;
                ElectricalSpasms++;
                break;
            }
        }
    }
}