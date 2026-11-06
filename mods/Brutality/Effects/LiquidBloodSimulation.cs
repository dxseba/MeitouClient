using System.Numerics;

using Meitou.Simulation;

namespace Meitou.Brutality.Effects;

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
        public float Duration = FlowSeconds, Strength = 1;
        public Func<int, CutFluidContact?>? Follow;
        public float Credit;
        public byte Started;
        public readonly Jet[] Jets = [new(), new()];
    }
    readonly List<Source> sources = [];
    readonly List<FlyingBlood> drops = [];
    readonly List<FlyingBlood> impacts = [];
    readonly Queue<(CharacterId Owner, int Part)> history = [];
    readonly HashSet<(CharacterId Owner, int Part)> seen = [];
    double clock;
    bool clockSet;
    float decalCredit = 24;
    public int SourceCount => sources.Count;
    public IReadOnlyList<FlyingBlood> Drops => drops;
    public IReadOnlyList<FlyingBlood> Impacts => impacts;

    public bool Begin(CharacterId owner, double born, Vector2 along, uint colour) =>
        BeginTorso(owner, born, along, colour, mechanical: false);

    internal bool BeginTorso(CharacterId owner, double born, Vector2 along, uint colour, bool mechanical) =>
        Begin(owner, 0, born, along, colour, null, FlowSeconds, 1, mechanical);

    internal bool BeginLimb(CharacterId owner, int slot, double born, Vector2 along, uint colour, Func<int, CutFluidContact?> follow) =>
        slot is >= 0 and < 4 && Begin(owner, slot + 1, born, along, colour, follow, 1.55f, .63f);

    bool Begin(CharacterId owner, int part, double born, Vector2 along, uint colour, Func<int, CutFluidContact?>? follow,
        float duration, float strength, bool mechanical = false)
    {
        var key = (owner, part);
        if (!double.IsFinite(born) || !float.IsFinite(along.X) || !float.IsFinite(along.Y) || !seen.Add(key)) return false;
        history.Enqueue(key);
        if (history.Count > 128) seen.Remove(history.Dequeue());
        if (sources.Count == SourceLimit) sources.RemoveAt(0);
        sources.Add(new Source
        {
            Owner = owner,
            Born = born,
            Duration = duration,
            Strength = strength,
            Follow = follow,
            Along = along.LengthSquared() > 1e-6f ? Vector2.Normalize(along) : Vector2.UnitX,
            Colour = LiquidPigment(colour, mechanical)
        });
        return true;
    }

    internal void SplashHead(Vector3 at, Vector2 travel, float floor, uint blood) => Splash(at, travel, floor, blood, 48, 1);

#if BRUTALITY_STOCK_MASTER
    internal void SplashExit(CutFluidContact contact, float floor, uint blood)
    {
        if (!Finite(contact.Position) || !Finite(contact.Normal) || !float.IsFinite(floor)
            || contact.Normal.LengthSquared() < 1e-6f) return;
        var normal = Vector3.Normalize(contact.Normal);
        var colour = LiquidPigment(blood);
        for (int i = 0; i < 42 && drops.Count < DropLimit; i++)
        {
            var jitter = new Vector3(random.NextSingle() - .5f, random.NextSingle() * .55f, random.NextSingle() - .5f);
            var speed = Vector3.Normalize(normal + jitter * .65f) * (13 + random.NextSingle() * 23);
            drops.Add(new(contact.Position + normal * .10f + jitter * .25f, speed, colour,
                .055f + random.NextSingle() * .115f, 0, floor));
        }
    }

    internal void SplashImpact(Vector3 at, Vector2 travel, float floor, uint blood, float damage)
    {
        float strength = Math.Clamp(damage / 20, 0, 1);
        Splash(at, travel, floor, blood, 5 + (int)(strength * 13), .32f + strength * .35f);
    }
#endif

    void Splash(Vector3 at, Vector2 travel, float floor, uint blood, int count, float strength)
    {
        if (!Finite(at) || !float.IsFinite(floor) || !float.IsFinite(travel.LengthSquared())) return;
        var along = travel.LengthSquared() > 1e-6f ? Vector2.Normalize(travel) : Vector2.UnitX;
        var colour = LiquidPigment(blood);
        for (int i = 0; i < count && drops.Count < DropLimit; i++)
        {
            float angle = MathF.Tau * random.NextSingle();
            var radial = new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle));
            bool near = i < count / 4;
            float speed = near ? 3 + random.NextSingle() * 6 : 8 + random.NextSingle() * 17;
            float push = near ? 4 : 11;
            var velocity = radial * speed + new Vector3(along.X * push, 3 + random.NextSingle() * 16, along.Y * push);
            drops.Add(new(at + radial * (.4f * strength), velocity * strength, colour, (.05f + random.NextSingle() * .15f) * strength, 0, floor));
        }
    }

    public void Advance(double now, Func<CharacterId, int, CutFluidContact?> contact, Func<Vector3, float> ground)
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
            if (age > source.Duration || age < -0.5f) { sources.RemoveAt(i); continue; }
            if (age >= 0) Emit(source, age, dt, contact, ground);
        }
    }

    void Emit(Source source, float age, float dt, Func<CharacterId, int, CutFluidContact?> contact, Func<Vector3, float> ground)
    {
        source.Credit += dt * 24;
        int drip = (int)source.Credit;
        bool emitted = false;
        for (int half = 0; half < 2; half++)
        {
            bool initial = (source.Started & 1 << half) == 0;
            if ((source.Follow is { } follow ? follow(half) : contact(source.Owner, half)) is not { } at) continue;
            float floor = ground(at.Position);
            if (!float.IsFinite(floor) || !Finite(at.Position) || !Finite(at.Normal)) continue;
            UpdateStream(source, half, at, floor, age, dt);
            if (initial || drip > 0) EmitHalf(source, at, floor, initial ? 12 : drip, initial, age, source.Jets[half].Motion);
            source.Started |= (byte)(1 << half);
            emitted = true;
        }
        if (emitted) source.Credit -= drip;
    }

    void EmitHalf(Source source, CutFluidContact contact, float floor, int count, bool initial, float age, Vector3 motion)
    {
        for (int i = 0; i < count && drops.Count < DropLimit; i++)
        {
            var jitter = new Vector3(random.NextSingle() - 0.5f, random.NextSingle() - 0.2f, random.NextSingle() - 0.5f);
            var along = new Vector3(source.Along.X, 0, source.Along.Y);
            float speed = (initial ? 8 + random.NextSingle() * 10 : MathF.Max(1, 9 * (1 - age / source.Duration))) * source.Strength;
            // Follow the open face. An upward bias would cancel the upper half's downward cut normal.
            var direction = contact.Normal * 1.5f + along * 0.30f + jitter * 0.6f;
            if (direction.LengthSquared() < 1e-6f) direction = Vector3.UnitY;
            drops.Add(new FlyingBlood(contact.Position + contact.Normal * 0.12f + jitter * 0.3f,
                Vector3.Normalize(direction) * speed + motion, source.Colour, (0.05f + random.NextSingle() * (initial ? 0.13f : 0.08f)) * source.Strength, 0, floor));
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

#if BRUTALITY_STOCK_MASTER
    // The stock particle pass lights raw RGB directly. Give default organic blood a deeper warm red,
    // leaving authored alternate species colours and mechanical fluids intact.
    internal static readonly Vector3 DefaultOrganicPigment = new(.265f, .020f, .015f);
#endif

    static Vector3 LiquidPigment(uint blood, bool mechanical = false)
    {
#if BRUTALITY_STOCK_MASTER
        if (!mechanical && (blood & 0xFFFFFF) == 0x800000) return DefaultOrganicPigment;
#endif
        return new Vector3((blood >> 16) & 255, (blood >> 8) & 255, blood & 255) / 255f;
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