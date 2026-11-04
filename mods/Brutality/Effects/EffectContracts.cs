using System.Numerics;
using Meitou.Simulation;

namespace Meitou.Brutality.Effects;

public readonly record struct CutFluidContact(Vector3 Position, Vector3 Normal);
public readonly record struct LiquidBloodStream(Vector3 Head, Vector3 Tail, float Width, Vector4 Colour, float TailWidth = 0);
public readonly record struct LiquidBloodDrop(Vector3 Head, Vector3 Tail, float Width, Vector4 Colour);

/// <summary>A committed rupture's immutable presentation inputs. Kind: limb 0, head 1, torso 2.</summary>
internal readonly record struct GoreEffectCut(CharacterId Owner, int Part, int Kind, int StableKey, long Tick,
    Vector2 Travel, float Cut, uint BloodColour, ulong Seed, bool Robot, Vector3 Fallback)
{
    public uint ExplosionSeed { get; init; }
    public bool Rupture { get; init; } = true;
    public bool Discharge { get; init; } = true;
}

internal sealed class RobotCutSource(GoreEffectCut cut, double born, double expires, long serial)
{
    public readonly GoreEffectCut Cut = cut;
    public readonly double Born = born, Expires = expires;
    public readonly long Serial = serial;
    public Vector3? Flash;
    public Vector3? Smoke;
}

/// <summary>Bounded paused-game-time effect lifecycle. Medical death is never a reason to end a mechanical short circuit.</summary>
internal sealed class GoreEffectsSimulation(Random random)
{
    public const int RobotCapacity = 64, ExplosionCapacity = 16, ExplosionHistory = 256;
    public const double MinimumDischargeSeconds = 20, MaximumDischargeSeconds = 30;
    public readonly LiquidBloodSimulation Blood = new(random);
    readonly Dictionary<(CharacterId, int), RobotCutSource> robots = [];
    readonly HashSet<(CharacterId, int, int)> exploded = [];
    readonly Queue<(CharacterId, int, int)> history = [];
    readonly List<RobotCutSource> explosions = [];
    readonly List<RobotDischargeAnchor> anchors = [];
    readonly List<RobotBreakExplosion> bursts = [];
    long serial;
    public IReadOnlyCollection<RobotCutSource> RobotSources => robots.Values;
    public IReadOnlyList<RobotDischargeAnchor> Anchors => anchors;
    public IReadOnlyList<RobotBreakExplosion> Explosions => bursts;
    public IReadOnlyList<BowelRenderPiece> Bowels { get; set; } = [];
    public IReadOnlyList<BowelRenderPiece> Cables { get; set; } = [];
    public int ExplosionHistoryCount => history.Count;
    public int ExplosionCount => explosions.Count;

    public bool Begin(in GoreEffectCut cut, double born)
    {
        if (cut.Owner.IsNone || cut.Part < 0 || !double.IsFinite(born) || born < 0 || !float.IsFinite(cut.Fallback.LengthSquared())) return false;
        bool blood = cut.Kind == 2 && Blood.Begin(cut.Owner, born, cut.Travel, cut.BloodColour);
        if (!cut.Robot) return blood;
        var key = (cut.Owner, cut.Part);
        bool duplicate = cut.Discharge && robots.TryGetValue(key, out var previous) && previous.Cut.Seed == cut.Seed;
        if (duplicate) return false;
        double lifetime = MinimumDischargeSeconds + (MaximumDischargeSeconds - MinimumDischargeSeconds) * (uint)RobotVisualRandom.Mix(cut.Seed) / uint.MaxValue;
        var source = new RobotCutSource(cut, born, born + lifetime, ++serial);
        if (cut.Discharge)
        {
            if (!robots.ContainsKey(key) && robots.Count >= RobotCapacity) robots.Remove(robots.MinBy(p => p.Value.Serial).Key);
            robots[key] = source;
        }
        var marker = (cut.Owner, cut.Part, cut.Kind);
        if (cut.Rupture && exploded.Add(marker))
        {
            if (history.Count >= ExplosionHistory) exploded.Remove(history.Dequeue());
            history.Enqueue(marker);
            if (explosions.Count >= ExplosionCapacity) explosions.RemoveAt(0);
            explosions.Add(source);
        }
        return true;
    }

    public void Advance(double now, Func<CharacterId, int, CutFluidContact?> bloodContact,
        Func<GoreEffectCut, int, CutFluidContact?> robotContact, Func<CharacterId, bool> validOwner, Func<Vector3, float> ground)
    {
        if (!double.IsFinite(now) || now < 0) return;
        Blood.Advance(now, bloodContact, ground);
        foreach (var key in robots.Where(p => now >= p.Value.Expires || !validOwner(p.Key.Item1)).Select(p => p.Key).ToArray()) robots.Remove(key);
        explosions.RemoveAll(p => now - p.Born >= RobotBreakExplosionGeometry.Lifetime || !validOwner(p.Cut.Owner));
        anchors.Clear(); bursts.Clear();
        foreach (var source in robots.Values.OrderByDescending(p => p.Serial))
        {
            float age = (float)(now - source.Born);
            if (age < 0) continue;
            for (int half = 0; half < 2 && anchors.Count < RobotDischargeGeometry.AnchorLimit; half++)
            {
                var point = robotContact(source.Cut, half);
                if (point is null && half == 0 && age > .15f) point = new(source.Cut.Fallback, Vector3.UnitY);
                if (point is not { } contact) continue;
                anchors.Add(new(contact.Position, age, (float)(source.Expires - source.Born),
                    (uint)(source.Cut.Seed ^ source.Cut.Seed >> 32) ^ (uint)half * 0x9E3779B9u, .9f + Math.Clamp(source.Cut.Cut / 70, 0, 1) * .7f));
            }
        }
        foreach (var source in explosions)
        {
            var retained = robotContact(source.Cut, 0);
            var detached = source.Cut.Kind is 0 or 1 ? robotContact(source.Cut, 1) : null;
            // Torso/head flashes begin on the retained wound; only a severed limb starts on its loose piece.
            var initial = source.Cut.Kind == 0 ? detached ?? retained : retained;
            if (initial is { } contact) { source.Flash ??= contact.Position; source.Smoke ??= contact.Position; }
            if (source.Flash is null && now - source.Born > .15f)
            { source.Flash = source.Cut.Fallback; source.Smoke ??= source.Cut.Fallback; }
            // Soot follows detached heads/limbs, while a torso rupture keeps emerging from retained half0.
            var following = source.Cut.Kind == 2 ? retained : detached ?? retained;
            if (following is { } smoke) source.Smoke = smoke.Position;
            if (source.Flash is not { } flash) continue;
            bursts.Add(new(flash, (float)(now - source.Born), source.Cut.ExplosionSeed,
                source.Cut.Kind == 2 ? 1.4f : source.Cut.Kind == 1 ? 1 : .8f) { SmokePosition = source.Smoke });
        }
    }

    public void Clear()
    {
        Blood.Clear(); robots.Clear(); explosions.Clear(); exploded.Clear(); history.Clear(); anchors.Clear(); bursts.Clear(); Bowels = []; Cables = []; serial = 0;
    }
}
