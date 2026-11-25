using System.Numerics;

namespace Meitou.FlowingHair;

/// <summary>
/// A tail as the original's reduced rod (<c>TailDynamics</c>, facts only), in the tail's frame (the Cat Mod's mesh turned so its tail hangs down
/// -Y as the Khajiit's does): two bend angles, pitch (away from the body) and side, each a spring <c>v += (16 (target - a) - 8 v) h</c> toward a
/// target from the force (pitch <c>-0.04 fz / r</c> in -0.04..1.35, side <c>0.017 fx / r</c> plus an idle swish in -0.65..0.65, with
/// <c>r = (0.5 + 0.015 rigidity)(1 + 0.65 wet) + 0.025 max(0, -fy)</c>), and a lag against the owner's turning that decays over
/// <c>0.28 - 0.0012 rigidity</c> s. Each point bends by the angles times its place along the tail. Stepped at 240 Hz, at most 16 steps, a frame
/// at most 1/15 s.
/// </summary>
sealed class TailRod
{
    const float Step = 1f / 240, MaxFrame = 1f / 15;

    /// <summary>A tail takes the owner's inertia and running airflow at a tenth (the original's <c>motionScale</c>).</summary>
    const float TailMotion = 0.1f;
    const int MaxSteps = 16;

    float _pitch, _side, _pitchSpeed, _sideSpeed;
    Vector3 _lag;
    double _time, _clock;
    readonly HairForce _force = new();
    bool _ready;
    Quaternion _lastTurn;
    readonly float _phase;

    public TailRod(float phase) => _phase = phase;

    public void Reset() => _ready = false;

    /// <summary>The tail's frame: the Cat Mod's mesh (x, y, z) as (x, z, -y), the Khajiit's as it is.</summary>
    public static Matrix4x4 Frame(HairPlan plan) => plan.Kind == HairKind.CatTail
        ? new Matrix4x4(1, 0, 0, 0, 0, 0, -1, 0, 0, 1, 0, 0, 0, 0, 0, 1)
        : Matrix4x4.Identity;

    /// <summary>Steps the rod to <paramref name="time"/> under gravity, wind and the owner's motion, and writes the plan's copies.</summary>
    public void Advance(HairPlan plan, FlowingHairSettings s, Matrix4x4[] bones, in Matrix4x4 world, double time, Vector3 wind, float rain)
    {
        var root = bones[plan.Dominant];
        var frame = Frame(plan);
        var turnNow = Turn(root * world);
        float dt = (float)(time - _clock);
        if (!_ready || !(dt >= 0) || dt > 0.25f || _force.Jumped(world))
        {
            (_pitch, _side, _pitchSpeed, _sideSpeed, _lag, _ready, _clock) = (0, 0, 0, 0, Vector3.Zero, true, time);
            _lastTurn = turnNow;
            _force.Start(world);
            Write(plan, bones, root, frame);
            return;
        }

        if (dt > 0)
        {
            _clock = time;
            var force = _force.Model(s, world, dt, wind, rain, TailMotion);
            var local = Matrix4x4.Invert(root, out var toBind) ? Vector3.TransformNormal(force, toBind) : Vector3.Zero;
            var turn = Vector3.TransformNormal(TurnDelta(turnNow), frame);
            Simulate(s, dt, Vector3.TransformNormal(local, frame), turn, _force.Wet);
        }

        _lastTurn = turnNow;
        Write(plan, bones, root, frame);
    }

    static Quaternion Turn(Matrix4x4 m)
    {
        var x = Vector3.Normalize(new Vector3(m.M11, m.M12, m.M13));
        var y = Vector3.Normalize(new Vector3(m.M21, m.M22, m.M23));
        var z = Vector3.Normalize(Vector3.Cross(x, y));
        y = Vector3.Cross(z, x);
        var r = new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(r));
    }

    /// <summary>The owner's turn since the last frame, as an axis times its angle in the root's frame.</summary>
    Vector3 TurnDelta(Quaternion now)
    {
        var delta = Quaternion.Normalize(Quaternion.Concatenate(now, Quaternion.Conjugate(_lastTurn)));
        if (delta.W < 0) delta = Quaternion.Negate(delta);

        float angle = 2 * MathF.Acos(Math.Clamp(delta.W, -1, 1));
        var axis = new Vector3(delta.X, delta.Y, delta.Z);
        return axis.LengthSquared() > 1e-12f ? Vector3.Normalize(axis) * angle : Vector3.Zero;
    }

    void Simulate(FlowingHairSettings s, float dt, Vector3 force, Vector3 turn, float wet)
    {
        float rigidity = Math.Clamp(s.Rigidity, 0, 100);
        if (turn.LengthSquared() > 1.44f || dt > 0.25f) (turn, _lag) = (Vector3.Zero, Vector3.Zero);

        turn.Z -= turn.Y * 0.55f;
        float lagTime = 0.28f - rigidity * 0.0012f, lagStrength = 0.9f - rigidity * 0.004f;
        float resistance = (0.5f + rigidity * 0.015f) * (1 + wet * 0.65f) + MathF.Max(0, -force.Y) * 0.025f;
        float frame = dt;
        float remaining = MathF.Min(dt, MaxFrame);
        for (int step = 0; remaining > 0 && step < MaxSteps; step++)
        {
            float h = MathF.Min(remaining, Step);
            remaining -= h;
            _time += h;
            float decay = MathF.Exp(-h / lagTime);
            _lag = _lag * decay - turn * (lagStrength * lagTime * (1 - decay) / frame);
            _lag = new Vector3(Math.Clamp(_lag.X, -0.08f, 0.30f), Math.Clamp(_lag.Y, -0.55f, 0.55f), Math.Clamp(_lag.Z, -0.40f, 0.40f));
            float idle = (0.085f * (float)Math.Sin(_time * 1.6 + _phase) + 0.025f * (float)Math.Sin(_time * 0.71 + _phase * 1.73))
                / (1 + rigidity * 0.009f + wet);
            float pitch = Math.Clamp(-force.Z * 0.04f / resistance, -0.04f, 1.35f);
            float side = Math.Clamp(force.X * 0.017f / resistance + idle, -0.65f, 0.65f);
            Spring(pitch, h, -0.04f, 1.35f, ref _pitch, ref _pitchSpeed);
            Spring(side, h, -0.65f, 0.65f, ref _side, ref _sideSpeed);
        }
    }

    static void Spring(float target, float h, float low, float high, ref float angle, ref float speed)
    {
        speed += (16 * (target - angle) - 8 * speed) * h;
        angle += speed * h;
        if (angle < low || angle > high) (angle, speed) = (Math.Clamp(angle, low, high), 0);
    }

    /// <summary>The bend at <paramref name="u"/> along the tail: the pitch and side angles times u, then the turn lag times u.</summary>
    Matrix4x4 Bend(float u)
    {
        var bend = Matrix4x4.CreateRotationX(_pitch * u) * Matrix4x4.CreateRotationZ(_side * u);
        float lag = _lag.Length() * u;
        return lag < 1e-6f ? bend : bend * Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(_lag), lag);
    }

    /// <summary>Each node's copy: the bent centreline's node, the bend at its place about the node's rest centre, carried by the root bone.</summary>
    void Write(HairPlan plan, Matrix4x4[] bones, in Matrix4x4 root, in Matrix4x4 frame)
    {
        int n = plan.Nodes;
        var back = Matrix4x4.Transpose(frame);
        var centre = new Vector3[n + 1];
        for (int j = 0; j <= n; j++) centre[j] = Vector3.Transform(plan.Rest[j], frame);

        var bent = centre[0];
        for (int j = 1; j <= n && j - 1 < plan.Slots.Length; j++)
        {
            bent += Vector3.TransformNormal(centre[j] - centre[j - 1], Bend((j - 0.5f) / n));
            int at = plan.Slots[j - 1] + plan.Bones;
            if (at >= bones.Length) continue;

            var node = Matrix4x4.CreateTranslation(-centre[j]) * Bend((float)j / n) * Matrix4x4.CreateTranslation(bent);
            bones[at] = frame * node * back * root;
        }
    }
}
