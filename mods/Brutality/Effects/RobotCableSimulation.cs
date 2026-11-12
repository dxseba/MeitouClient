using Meitou.Game.Rendering.Frame;
using System.Numerics;

namespace Meitou.Brutality.Effects;

internal readonly record struct RobotCableContact(long Key, CutFluidContact Contact);

/// <summary>The pinned seven-node anchored Verlet tail and five-sided mesh, owned only by the presentation thread.</summary>
internal sealed class RobotCableSimulation
{
    public const int Capacity = 32, NodeCount = 7, CableSides = 5, VertexCount = (NodeCount - 1) * CableSides * 6;
    sealed class RobotCable
    {
        public required Vector3[] Nodes, Previous;
        public readonly Vector3[] LastNodes = new Vector3[NodeCount];
        public readonly Vertex[] Vertices = new Vertex[VertexCount];
        public required long GpuKey;
        public float Clock, Still;
        public bool Sleeping;
        public long Version;
    }
    readonly Dictionary<long, RobotCable> cables = [];
    readonly HashSet<long> active = [];
    readonly List<BowelRenderPiece> rendered = [];
    static long nextKey;
    public int Count => cables.Count;
    public int Moving => cables.Values.Count(c => !c.Sleeping);
    public IReadOnlyList<BowelRenderPiece> Rendered => rendered;
    public bool Accepts(long key, int scheduled) => cables.ContainsKey(key) || cables.Count < Capacity && scheduled < Capacity;

    public void Advance(double clock, IReadOnlyList<RobotCableContact> contacts, Func<Vector3, float> ground, bool enabled = true)
    {
        rendered.Clear(); active.Clear();
        if (!enabled || !double.IsFinite(clock)) { cables.Clear(); return; }
        foreach (var request in contacts)
        {
            var contact = request.Contact;
            if (!float.IsFinite(contact.Position.LengthSquared()) || !float.IsFinite(contact.Normal.LengthSquared()) || contact.Normal.LengthSquared() < 1e-8f) continue;
            float floor = ground(contact.Position);
            if (!float.IsFinite(floor) || !active.Add(request.Key)) continue;
            if (!cables.TryGetValue(request.Key, out var cable))
            {
                if (cables.Count >= Capacity) continue;
                var nodes = new Vector3[NodeCount];
                for (int i = 0; i < nodes.Length; i++) nodes[i] = contact.Position + contact.Normal * (i * .14f) - Vector3.UnitY * (i * .16f);
                cable = new() { Nodes = nodes, Previous = (Vector3[])nodes.Clone(), Clock = (float)clock,
                    GpuKey = Interlocked.Decrement(ref nextKey) };
                cables.Add(request.Key, cable);
            }
            float dt = Math.Clamp((float)clock - cable.Clock, 0, .1f); cable.Clock = (float)clock;
            if (Vector3.DistanceSquared(cable.Nodes[0], contact.Position) > 25)
            {
                cable.Sleeping = false; cable.Still = 0;
                var delta = contact.Position - cable.Nodes[0];
                for (int i = 0; i < cable.Nodes.Length; i++) { cable.Nodes[i] += delta; cable.Previous[i] += delta; }
            }
            if (dt > 0) StepRobotCable(cable, contact, floor, dt);
            bool changed = cable.Version == 0;
            for (int i = 0; i < NodeCount; i++) changed |= Vector3.DistanceSquared(cable.LastNodes[i], cable.Nodes[i] - contact.Position) > .000001f;
            if (changed)
            {
                for (int i = 0; i < NodeCount; i++) cable.LastNodes[i] = cable.Nodes[i] - contact.Position;
                FillCable(cable, contact.Position); cable.Version++;
            }
            rendered.Add(new(cable.GpuKey, contact.Position, cable.Vertices, 0, cable.Version) { Mechanical = true });
        }
        foreach (long key in cables.Keys.Where(key => !active.Contains(key)).ToArray()) cables.Remove(key);
    }

    static void StepRobotCable(RobotCable cable, CutFluidContact contact, float floor, float dt)
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
    static void FillCable(RobotCable cable, Vector3 centre)
    {
        int written = 0;
        for (int segment = 0; segment < NodeCount - 1; segment++)
        {
            var a = cable.Nodes[segment] - centre;
            var b = cable.Nodes[segment + 1] - centre;
            var axis = b - a;
            if (axis.LengthSquared() < 1e-10f) axis = Vector3.UnitY;
            axis = Vector3.Normalize(axis);
            var right = Vector3.Normalize(Vector3.Cross(axis, MathF.Abs(axis.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
            var up = Vector3.Cross(axis, right);
            for (int side = 0; side < CableSides; side++)
            {
                var n0 = right * MathF.Cos(side * MathF.Tau / CableSides) + up * MathF.Sin(side * MathF.Tau / CableSides);
                var n1 = right * MathF.Cos((side + 1) * MathF.Tau / CableSides) + up * MathF.Sin((side + 1) * MathF.Tau / CableSides);
                Add(a + n0 * 0.055f, n0);
                Add(b + n1 * 0.055f, n1);
                Add(b + n0 * 0.055f, n0);
                Add(a + n0 * 0.055f, n0);
                Add(a + n1 * 0.055f, n1);
                Add(b + n1 * 0.055f, n1);
            }
        }
        void Add(Vector3 at, Vector3 normal) => cable.Vertices[written++] = new Vertex { Position = at, Normal = normal, Colour = new(.035f, .043f, .047f, 1) };
    }

    public void Clear() { cables.Clear(); active.Clear(); rendered.Clear(); }
}
