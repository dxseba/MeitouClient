using System.Numerics;

namespace Meitou.FlowingHair;

/// <summary>
/// One character's hair or beard as a Verlet chain (the original runs Verlet cloth on every vertex; the port runs it on the plan's few nodes and
/// skins the mesh to them), in the character's model space as the original works in the character's own frame: node 0 pinned to the head, the
/// others pulled by gravity, wind, the character's own acceleration (inertia) and the airflow of its movement, kept at their rest lengths,
/// against folding (bending), drawn back to the authored shape (shape stiffness, scaled by rigidity), no farther than the plan's displacement
/// from rest and out of the head. Stepped at 120 Hz in game time, at most 4 steps a frame, as the original's solver.
/// </summary>
sealed class HairChain
{
    const float Step = 1f / 120, MaxFrame = 1f / 15;
    const int MaxSteps = 4;

    Vector3[] _pos = [], _prev = [], _rest = [], _lastRest = [];
    bool _ready;
    double _time;
    readonly HairForce _force = new();
    Vector3 _headCentre;

    public void Reset() => _ready = false;

    /// <summary>Steps the chain to <paramref name="time"/> and writes the plan's copies into <paramref name="bones"/>.</summary>
    public void Advance(HairPlan plan, FlowingHairSettings s, Matrix4x4[] bones, in Matrix4x4 world, double time, Vector3 wind, float rain)
    {
        var head = bones[plan.Dominant];
        PoseRest(plan, head);
        float dt = (float)(time - _time);
        if (!_ready || !(dt >= 0) || dt > 0.25f || _force.Jumped(world))
        {
            Start(world, time);
            Write(plan, bones, head);
            return;
        }

        if (dt > 0)
        {
            _time = time;
            Simulate(plan, s, world, Math.Min(dt, MaxFrame), wind, rain);
        }

        Write(plan, bones, head);
        _rest.CopyTo(_lastRest, 0);
    }

    /// <summary>The copies equal the head bone: the mesh as the file has it.</summary>
    public static void Rigid(HairPlan plan, Matrix4x4[] bones)
    {
        if ((uint)plan.Dominant >= (uint)bones.Length) return;

        foreach (int slot in plan.Slots)
        {
            if (slot + plan.Bones < bones.Length) bones[slot + plan.Bones] = bones[plan.Dominant];
        }
    }

    void PoseRest(HairPlan plan, in Matrix4x4 head)
    {
        int n = plan.Rest.Length;
        if (_rest.Length != n)
        {
            (_pos, _prev, _rest, _lastRest) = (new Vector3[n], new Vector3[n], new Vector3[n], new Vector3[n]);
            _ready = false;
        }

        for (int j = 0; j < n; j++) _rest[j] = Vector3.Transform(plan.Rest[j], head);

        _headCentre = Vector3.Transform(plan.HeadCentre, head);
    }

    void Start(in Matrix4x4 world, double time)
    {
        _rest.CopyTo(_pos, 0);
        _rest.CopyTo(_prev, 0);
        _rest.CopyTo(_lastRest, 0);
        (_ready, _time) = (true, time);
        _force.Start(world);
    }

    void Simulate(HairPlan plan, FlowingHairSettings s, in Matrix4x4 world, float dt, Vector3 wind, float rain)
    {
        var accel = _force.Model(s, world, dt, wind, rain, 1);
        int steps = Math.Clamp((int)MathF.Ceiling(dt / Step), 1, MaxSteps);
        float h = dt / steps;
        var restNow = (Vector3[])_rest.Clone();
        for (int k = 1; k <= steps; k++)
        {
            float f = (float)k / steps;
            for (int j = 0; j < _rest.Length; j++) _rest[j] = Vector3.Lerp(_lastRest[j], restNow[j], f);

            Substep(plan, s, h, accel);
        }

        restNow.CopyTo(_rest, 0);
    }

    void Substep(HairPlan plan, FlowingHairSettings s, float h, Vector3 accel)
    {
        float keep = Math.Clamp(1 - s.Damping - _force.Wet * 0.05f, 0, 1);
        for (int j = 1; j < _pos.Length; j++)
        {
            var old = _pos[j];
            _pos[j] += (_pos[j] - _prev[j]) * keep + accel * (h * h);
            _prev[j] = old;
            if (!float.IsFinite(_pos[j].X + _pos[j].Y + _pos[j].Z)) (_pos[j], _prev[j]) = (_rest[j], _rest[j]);
        }

        _pos[0] = _rest[0];
        for (int i = 0; i < Math.Max(1, s.Iterations); i++)
        {
            Lengths(s);
            Collide(s);
            _pos[0] = _rest[0];
        }

        Shape(plan, s);
        Limit(plan);
        Collide(s);
        _pos[0] = _rest[0];
    }

    /// <summary>The segments at their rest lengths (node 0 fixed), and the bending links two nodes apart at the bending stiffness.</summary>
    void Lengths(FlowingHairSettings s)
    {
        for (int j = 1; j < _pos.Length; j++) Link(j - 1, j, 1);

        for (int j = 2; j < _pos.Length; j++) Link(j - 2, j, Math.Clamp(s.Bending, 0, 1));
    }

    void Link(int a, int b, float stiffness)
    {
        float rest = Vector3.Distance(_rest[a], _rest[b]);
        var d = _pos[b] - _pos[a];
        float length = d.Length();
        if (length < 1e-6f || stiffness <= 0) return;

        var correction = d * ((length - rest) / length * stiffness);
        if (a == 0)
        {
            _pos[b] -= correction;
            return;
        }

        _pos[a] += correction * 0.5f;
        _pos[b] -= correction * 0.5f;
    }

    /// <summary>The pull back to the authored shape: <c>stiffness x 4^((rigidity - 50) / 50)</c>, 0.25x to 4x (the original's rigidity scale).</summary>
    void Shape(HairPlan plan, FlowingHairSettings s)
    {
        float baseStiffness = plan.Kind == HairKind.Beard ? s.BeardShapeStiffness : s.ShapeStiffness;
        float scale = MathF.Pow(4, (Math.Clamp(s.Rigidity, 0, 100) - 50) / 50);
        float pull = Math.Clamp(baseStiffness * scale, 0, 1);
        for (int j = 1; j < _pos.Length; j++) _pos[j] += (_rest[j] - _pos[j]) * pull;
    }

    void Limit(HairPlan plan)
    {
        for (int j = 1; j < _pos.Length; j++)
        {
            var d = _pos[j] - _rest[j];
            float length = d.Length();
            if (length > plan.MaxDisplacement) _pos[j] = _rest[j] + d * (plan.MaxDisplacement / length);
        }
    }

    /// <summary>The face collider: a node comes no nearer the head's centre than 0.9 of its rest distance.</summary>
    void Collide(FlowingHairSettings s)
    {
        if (!s.FaceCollision) return;

        var centre = _headCentre;
        for (int j = 1; j < _pos.Length; j++)
        {
            float least = 0.9f * Vector3.Distance(_rest[j], centre);
            var d = _pos[j] - centre;
            float length = d.Length();
            if (length < least && length > 1e-6f) _pos[j] = centre + d * (least / length);
        }
    }

    /// <summary>
    /// Each node's copy: the head bone, then the turn of the segment ending at the node from its rest direction to its simulated one about the
    /// node's rest place, then the move to the node's simulated place.
    /// </summary>
    void Write(HairPlan plan, Matrix4x4[] bones, in Matrix4x4 head)
    {
        for (int j = 1; j < _pos.Length && j - 1 < plan.Slots.Length; j++)
        {
            int at = plan.Slots[j - 1] + plan.Bones;
            if (at >= bones.Length) continue;

            var turn = FromTo(_rest[j] - _rest[j - 1], _pos[j] - _pos[j - 1]);
            var move = Matrix4x4.CreateTranslation(-_rest[j]) * Matrix4x4.CreateFromQuaternion(turn) * Matrix4x4.CreateTranslation(_pos[j]);
            bones[at] = head * move;
        }
    }

    static Quaternion FromTo(Vector3 a, Vector3 b)
    {
        if (a.LengthSquared() < 1e-12f || b.LengthSquared() < 1e-12f) return Quaternion.Identity;

        a = Vector3.Normalize(a);
        b = Vector3.Normalize(b);
        float d = Vector3.Dot(a, b);
        var axis = Vector3.Cross(a, b);
        if (d > 0.999999f || axis.LengthSquared() < 1e-12f) return Quaternion.Identity;

        return Quaternion.Normalize(new Quaternion(axis, 1 + d));
    }
}
