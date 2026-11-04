using System.Numerics;

using Meitou.Rendering.Characters;
using Meitou.Simulation;

namespace Meitou.Game;

public record struct FlyingBlood(Vector3 At, Vector3 Velocity, Vector3 Colour, float Width, float Age, float Floor);

/// <summary>Bounded cosmetic cut fluid. Uses shown game time and a private visual RNG, never combat state.</summary>
public sealed partial class LiquidBloodSimulation(Random random)
{
    public const int SourceLimit = 32, DropLimit = 768, ImpactLimit = 8;
    public const float FlowSeconds = 3.2f;
    sealed class Source
    {
        public required CharacterId Owner;
        public required Vector2 Along;
        public required Vector3 Colour;
        public double Born;
        public float Credit;
        public byte Started;
        public readonly Jet[] Jets = [new(), new()];
    }
    readonly List<Source> sources = [];
    readonly List<FlyingBlood> drops = [];
    readonly List<FlyingBlood> impacts = [];
    readonly Queue<CharacterId> history = [];
    readonly HashSet<CharacterId> seen = [];
    double clock;
    bool clockSet;
    float decalCredit = 24;
    public int SourceCount => sources.Count;
    public IReadOnlyList<FlyingBlood> Drops => drops;
    public IReadOnlyList<FlyingBlood> Impacts => impacts;

    public bool Begin(CharacterId owner, double born, Vector2 along, uint colour)
    {
        if (!double.IsFinite(born) || !float.IsFinite(along.X) || !float.IsFinite(along.Y) || !seen.Add(owner)) return false;
        history.Enqueue(owner);
        if (history.Count > 128) seen.Remove(history.Dequeue());
        if (sources.Count == SourceLimit) sources.RemoveAt(0);
        sources.Add(new Source
        {
            Owner = owner,
            Born = born,
            Along = along.LengthSquared() > 1e-6f ? Vector2.Normalize(along) : Vector2.UnitX,
            Colour = new Vector3((colour >> 16) & 255, (colour >> 8) & 255, colour & 255) / 255f
        });
        return true;
    }

    public void Advance(double now, Func<CharacterId, int, CharacterCutContact?> contact, Func<Vector3, float> ground)
    {
        impacts.Clear();
        if (!double.IsFinite(now)) return;
        float dt = clockSet ? (float)Math.Clamp(now - clock, 0, 0.5) : 0;
        clock = now;
        clockSet = true;
        if (dt <= 0) return;
        decalCredit = MathF.Min(24, decalCredit + dt * 16);
        streamSegments.Clear();
        Step(dt, ground); // Existing drops advance; newly emitted drops are born at the current cut surface.
        for (int i = sources.Count - 1; i >= 0; i--)
        {
            var source = sources[i];
            float age = (float)(now - source.Born);
            if (age > FlowSeconds || age < -0.5f) { sources.RemoveAt(i); continue; }
            if (age >= 0) Emit(source, age, dt, contact, ground);
        }
    }

    void Emit(Source source, float age, float dt, Func<CharacterId, int, CharacterCutContact?> contact, Func<Vector3, float> ground)
    {
        source.Credit += dt * 24;
        int drip = (int)source.Credit;
        bool emitted = false;
        for (int half = 0; half < 2; half++)
        {
            bool initial = (source.Started & 1 << half) == 0;
            if (contact(source.Owner, half) is not { } at) continue;
            float floor = ground(at.Position);
            if (!float.IsFinite(floor) || !Finite(at.Position) || !Finite(at.Normal)) continue;
            UpdateStream(source, half, at, floor, age, dt);
            if (initial || drip > 0) EmitHalf(source, at, floor, initial ? 12 : drip, initial, age, source.Jets[half].Motion);
            source.Started |= (byte)(1 << half);
            emitted = true;
        }
        if (emitted) source.Credit -= drip;
    }

    void EmitHalf(Source source, CharacterCutContact contact, float floor, int count, bool initial, float age, Vector3 motion)
    {
        for (int i = 0; i < count && drops.Count < DropLimit; i++)
        {
            var jitter = new Vector3(random.NextSingle() - 0.5f, random.NextSingle() - 0.2f, random.NextSingle() - 0.5f);
            var along = new Vector3(source.Along.X, 0, source.Along.Y);
            float speed = initial ? 8 + random.NextSingle() * 10 : MathF.Max(1, 9 * (1 - age / FlowSeconds));
            // Follow the open face. An upward bias would cancel the upper half's downward cut normal.
            var direction = contact.Normal * 1.5f + along * 0.30f + jitter * 0.6f;
            if (direction.LengthSquared() < 1e-6f) direction = Vector3.UnitY;
            drops.Add(new FlyingBlood(contact.Position + contact.Normal * 0.12f + jitter * 0.3f,
                Vector3.Normalize(direction) * speed + motion, source.Colour, 0.05f + random.NextSingle() * (initial ? 0.13f : 0.08f), 0, floor));
        }
    }

    void Step(float dt, Func<Vector3, float> ground)
    {
        for (int i = drops.Count - 1; i >= 0; i--)
        {
            var drop = drops[i];
            drop.Age += dt;
            drop.At += drop.Velocity * dt - Vector3.UnitY * (17.5f * dt * dt);
            drop.Velocity -= Vector3.UnitY * (35 * dt);
            if (drop.At.Y <= drop.Floor + 0.06f || drop.Age > 2.5f)
            {
                if (drop.At.Y <= drop.Floor + 0.06f && impacts.Count < ImpactLimit && decalCredit >= 1)
                {
                    float floor = ground(drop.At);
                    if (float.IsFinite(floor))
                    {
                        drop.At = drop.At with { Y = floor };
                        impacts.Add(drop);
                        decalCredit--;
                    }
                }
                drops[i] = drops[^1];
                drops.RemoveAt(drops.Count - 1);
            }
            else drops[i] = drop;
        }
    }

    static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    public void Clear()
    {
        sources.Clear();
        drops.Clear();
        impacts.Clear();
        streamSegments.Clear();
        seen.Clear();
        history.Clear();
        clockSet = false;
        decalCredit = 24;
    }
}