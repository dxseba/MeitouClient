using System.Numerics;

namespace Meitou.Brutality.Effects;

public sealed partial class LiquidBloodSimulation
{
    public const int StreamSegmentLimit = 512;
    const float JetInterval = 1f / 30, JetLife = 0.28f;
    const float ConnectedReach = 4.8f;
    sealed class Jet
    {
        public readonly List<FlyingBlood> Nodes = [];
        public Vector3 Anchor, Velocity, Motion;
        public float Credit, Phase;
        public int Breaks;
        public bool Ready;
    }
    readonly List<LiquidBloodStream> streamSegments = [];
    public IReadOnlyList<LiquidBloodStream> Streams => streamSegments;

    void UpdateStream(Source source, int half, CutFluidContact contact, float floor, float age, float dt)
    {
        var jet = source.Jets[half];
        var anchor = contact.Position + contact.Normal * 0.06f;
        if (!jet.Ready) jet.Phase = random.NextSingle() * MathF.Tau;
        var along = new Vector3(source.Along.X, 0, source.Along.Y);
        var side = Vector3.Cross(contact.Normal, MathF.Abs(contact.Normal.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX);
        float pulse = 0.76f + 0.24f * MathF.Sin(age * 14 + jet.Phase);
        float pressure = MathF.Exp(-age * 0.85f);
        var direction = contact.Normal + along * 0.10f + side * (0.10f * MathF.Sin(age * 11 + jet.Phase));
        direction = direction.LengthSquared() > 1e-6f ? Vector3.Normalize(direction) : -Vector3.UnitY;
        var motion = jet.Ready ? (anchor - jet.Anchor) / MathF.Max(dt, 0.001f) : Vector3.Zero;
        float motionSpeed = motion.Length();
        jet.Motion = !float.IsFinite(motionSpeed) ? Vector3.Zero
            : motionSpeed > 100 ? motion * (100 / motionSpeed) : motion;
        var velocity = direction * ((2 + 10 * pressure * pulse) * source.Strength) + jet.Motion;
        float width = (0.045f + 0.29f * pressure) * pulse * source.Strength;
        StepJet(jet, dt);
        SampleJet(jet, anchor, velocity, source.Colour, floor, width, dt);
        ConnectJet(jet, anchor, floor, width, source.Colour, Math.Clamp((source.Duration - age) * 4, 0, 1));
        (jet.Anchor, jet.Velocity, jet.Ready) = (anchor, velocity, true);
    }

    void StepJet(Jet jet, float dt)
    {
        for (int i = jet.Nodes.Count - 1; i >= 0; i--)
        {
            var node = jet.Nodes[i];
            node.Age += dt;
            node.At += node.Velocity * dt - Vector3.UnitY * (17.5f * dt * dt);
            node.Velocity -= Vector3.UnitY * (35 * dt);
            if (node.Age <= JetLife) { jet.Nodes[i] = node; continue; }
            if (++jet.Breaks % 3 == 0 && drops.Count < DropLimit && node.At.Y > node.Floor + 0.06f)
                drops.Add(node with { Age = 0, Width = node.Width * 0.65f });
            jet.Nodes.RemoveAt(i);
        }
    }

    static void SampleJet(Jet jet, Vector3 anchor, Vector3 velocity, Vector3 colour, float floor, float width, float dt)
    {
        jet.Credit += dt;
        int samples = Math.Min(9, (int)(jet.Credit / JetInterval));
        if (!jet.Ready) samples = 1;
        jet.Credit %= JetInterval;
        for (int sample = samples - 1; sample >= 0; sample--)
        {
            float flight = jet.Ready ? sample * JetInterval : 0;
            float fraction = Math.Clamp(flight / MathF.Max(dt, 0.001f), 0, 1);
            var at = jet.Ready ? Vector3.Lerp(anchor, jet.Anchor, fraction) : anchor;
            var speed = jet.Ready ? Vector3.Lerp(velocity, jet.Velocity, fraction) : velocity;
            at += speed * flight - Vector3.UnitY * (17.5f * flight * flight);
            jet.Nodes.Insert(0, new FlyingBlood(at, speed - Vector3.UnitY * (35 * flight), colour, width, flight, floor));
        }
        if (jet.Nodes.Count > 10) jet.Nodes.RemoveRange(10, jet.Nodes.Count - 10);
    }

    void ConnectJet(Jet jet, Vector3 anchor, float floor, float width, Vector3 colour, float fade)
    {
        var previous = anchor;
        float previousWidth = width, connectedLength = 0;
        for (int i = 0; i < jet.Nodes.Count; i++)
        {
            var node = jet.Nodes[i];
            var end = node.At;
            bool landed = end.Y <= floor + 0.06f;
            if (landed)
            {
                float share = Math.Clamp((previous.Y - floor - 0.06f) / MathF.Max(previous.Y - end.Y, 0.001f), 0, 1);
                end = Vector3.Lerp(previous, end, share);
            }
            float distance = Vector3.Distance(previous, end);
            if (distance > 3)
            {
                ReleaseTail(jet, i);
                break;
            }
            float tailWidth = node.Width * MathF.Sqrt(Math.Clamp(1 - node.Age / (JetLife + 0.1f), 0.08f, 1));
            if (connectedLength + distance > ConnectedReach)
            {
                // Older liquid keeps flying as a separate short filament, not a tether to the moving wound.
                previous = end;
                previousWidth = tailWidth;
                connectedLength = 0;
                continue;
            }
            connectedLength += distance;
            if (distance > 0.005f && streamSegments.Count < StreamSegmentLimit)
                streamSegments.Add(new LiquidBloodStream(previous, end, previousWidth,
                    new Vector4(colour, 0.92f * fade), tailWidth));
            previous = end;
            previousWidth = tailWidth;
            if (!landed) continue;
            if (impacts.Count < ImpactLimit && decalCredit >= 1)
            {
                impacts.Add(node with { At = end with { Y = floor } });
                decalCredit--;
            }
            jet.Nodes.RemoveRange(i, jet.Nodes.Count - i);
            break;
        }
    }

    void ReleaseTail(Jet jet, int first)
    {
        for (int i = first; i < jet.Nodes.Count; i += 3)
        {
            var node = jet.Nodes[i];
            if (drops.Count < DropLimit && node.At.Y > node.Floor + 0.06f)
                drops.Add(node with { Age = 0, Width = node.Width * 0.6f });
        }
        jet.Nodes.RemoveRange(first, jet.Nodes.Count - first);
    }

}
