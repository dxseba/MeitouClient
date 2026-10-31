namespace Meitou.Simulation.Wounds;

public enum WoundKind { None, Slash, Thrust, Crush }

[Flags]
public enum WoundAttack { None = 0, Front = 1, Back = 2, Left = 4, Right = 8, Top = 16, Bottom = 32 }

/// <summary>Anatomy layers a wound may open. Bone and organ are never removed by a melee stamp.</summary>
public enum WoundLayer { Bone, Muscle, Tendon, Fat, Organ, Skin, Fascia }

/// <summary>A cut's footprint: axis, width and depth 0 to 1.</summary>
public readonly record struct WoundShape(float Cosine = 1, float Sine = 0, float Aspect = 0.20f, float Depth = 0);

/// <summary>A sample of a cut: core within area within ring, each 0 to 1.</summary>
public readonly record struct WoundProfile(float Core, float Area, float Ring);

public readonly record struct WoundInput(int CutDirection, WoundAttack Attack, bool Harpoon, bool FrontTorso, float Cutting, float Blunt, float Pierce, float Maximum, float LocalDose = 0, int LocalHits = 0, float Size = 1);

public readonly record struct WoundResult(bool Accepted, WoundKind Kind, WoundShape Shape, float AcceptedDamage, float Dose, int Hits);

/// <summary>Which hits open a wound, how it grows and what it removes. Pure: visual state only, with no effect on the simulation. Ported from Kenshi Gore Lab's <c>MeleePolicy</c>.</summary>
public static class WoundPolicy
{
    public const int MaxHits = 8;
    public const float MaxDose = 4;

    static float Clamp(float x, float a, float b) => x < a ? a : x > b ? b : x;
    static float Smooth(float x) { x = Clamp(x, 0, 1); return x * x * (3 - 2 * x); }
    static float Frac(float x) => x - MathF.Floor(x);

    /// <summary>Cut directions: default 0, down 1, left 2, right 3, thrust 4, up 5.</summary>
    public static WoundShape ShapeForDirection(int direction, float depth)
    {
        float d = float.IsFinite(depth) ? Clamp(depth, 0, 1) : 0;
        return direction switch
        {
            1 => new WoundShape(0, -1, 0.20f, d),
            5 => new WoundShape(0, 1, 0.20f, d),
            2 => new WoundShape(-1, 0, 0.20f, d),
            4 => new WoundShape(1, 0, 0.55f, d),
            _ => new WoundShape(1, 0, 0.20f, d),
        };
    }

    public static WoundResult Evaluate(in WoundInput input)
    {
        // Side and back placement are refused until their skin charts exist; front with top stays a front hit.
        if (!input.FrontTorso || input.Harpoon || (input.Attack & WoundAttack.Front) == 0 || (input.Attack & ~(WoundAttack.Front | WoundAttack.Top)) != 0) return default;
        if (input.CutDirection is < 0 or > 5) return default;
        if (!float.IsFinite(input.Cutting) || !float.IsFinite(input.Blunt) || !float.IsFinite(input.Pierce) || !float.IsFinite(input.Maximum)
            || input.Maximum <= 0 || input.Cutting < 0 || input.Blunt < 0 || input.Pierce < 0) return default;

        WoundKind kind;
        float accepted;
        if (input.CutDirection == 4)
        {
            if (input.Cutting <= 0 && input.Pierce <= 0) return default;
            (kind, accepted) = (WoundKind.Thrust, input.Cutting + input.Pierce);
        }
        else if (input.Cutting > 0) (kind, accepted) = (WoundKind.Slash, input.Cutting);
        else if (input.Blunt > 0) (kind, accepted) = (WoundKind.Crush, input.Blunt);
        else return default;
        if (!float.IsFinite(accepted)) return default;

        accepted = Clamp(accepted, 0, 100000);
        float prior = float.IsFinite(input.LocalDose) ? Clamp(input.LocalDose, 0, MaxDose) : 0;
        float dose = Clamp(prior + Clamp(accepted / input.Maximum, 0, 1.75f), 0, MaxDose);
        var shape = ShapeForDirection(input.CutDirection, Clamp(dose / 1.2f, 0, 1));
        if (kind == WoundKind.Crush) shape = shape with { Aspect = 0.85f };
        return new WoundResult(true, kind, shape, accepted, dose, Math.Min(input.LocalHits + 1, MaxHits));
    }

    static float Hash(float x, float y)
    {
        float a = Frac(x * 0.1031f), b = Frac(y * 0.1031f), c = a;
        float d = a * (b + 33.33f) + b * (c + 33.33f) + c * (a + 33.33f);
        a += d; b += d; c += d;
        return Frac((a + b) * c);
    }

    static float Noise(float x, float y)
    {
        float ix = MathF.Floor(x), iy = MathF.Floor(y), fx = Smooth(Frac(x)), fy = Smooth(Frac(y));
        float a = Hash(ix, iy), b = Hash(ix + 1, iy), c = Hash(ix, iy + 1), d = Hash(ix + 1, iy + 1);
        float low = a + (b - a) * fx, high = c + (d - c) * fx;
        return low + (high - low) * fy;
    }

    static float Tear(float x, float y) => Noise(x * 6, y * 6) * 0.65f + Noise(x * 17 + 11.3f, y * 17 + 11.3f) * 0.35f - 0.5f;

    static float CutSize(float depth) => MathF.Max(MathF.Pow(Clamp(depth, 0, 1), 0.4f), 0.45f) * 1.5f;

    /// <summary>The ragged cut at a point of its local chart; the reference for the shader, not bit-exact with it.</summary>
    public static WoundProfile Sample(float qx, float qy, in WoundShape shape)
    {
        static bool Bad(float v) => !float.IsFinite(v) || MathF.Abs(v) > 1e10f;
        if (Bad(qx) || Bad(qy) || Bad(shape.Cosine) || Bad(shape.Sine) || Bad(shape.Aspect) || Bad(shape.Depth)) return default;
        float norm = shape.Cosine * shape.Cosine + shape.Sine * shape.Sine, cs = 1, sn = 0;
        if (norm > 1e-8f) { norm = 1 / MathF.Sqrt(norm); cs = shape.Cosine * norm; sn = shape.Sine * norm; }
        float size = CutSize(shape.Depth), tear = Tear(qx, qy);
        float x = (qx * cs + qy * sn) / (1.25f * size) * (1 - tear * 0.18f), y = (-qx * sn + qy * cs) / (Clamp(shape.Aspect, 0.08f, 1) * size);
        if (MathF.Abs(x) >= 1) return default;
        float lens = MathF.Max(1 - MathF.Pow(MathF.Abs(x), 2.4f), 0.025f);
        float d = MathF.Abs(y) / lens + tear * 0.48f, growth = Clamp((Clamp(shape.Depth, 0, 1) - 0.25f) * 0.1f, 0, 0.075f);
        float ends = Smooth((1 - MathF.Abs(x)) / 0.08f);
        return new WoundProfile(Clamp((0.42f + growth - d) / 0.15f, 0, 1) * ends, Clamp((0.82f + growth - d) / 0.15f, 0, 1) * ends, Clamp((1.25f + growth - d) / 0.40f, 0, 1) * ends);
    }

    public static bool RemovesLayer(WoundLayer layer, in WoundProfile p, float depth, int hits)
    {
        if (!float.IsFinite(depth) || !float.IsFinite(p.Core) || !float.IsFinite(p.Area) || !float.IsFinite(p.Ring)) return false;
        if (p.Core < 0 || p.Core > p.Area || p.Area > p.Ring || p.Ring > 1) return false;
        depth = Clamp(depth, 0, 1);
        return layer switch
        {
            WoundLayer.Skin => p.Area > 0.18f,
            WoundLayer.Fat => depth >= 0.10f && p.Area > 0.20f,
            WoundLayer.Fascia => depth >= 0.25f && p.Core > 0.30f,
            WoundLayer.Muscle => depth >= 0.40f && p.Core > 0.55f,
            WoundLayer.Tendon => depth >= 0.80f && hits >= 3 && p.Core > 0.86f,
            _ => false,
        };
    }
}
