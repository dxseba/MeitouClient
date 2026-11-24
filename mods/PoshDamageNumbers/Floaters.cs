using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.PoshDamageNumbers;

/// <summary>One number or word on screen, as the original keeps it: its text, colour, size factor, life, rise, lift, side and jitter, and its age.</summary>
sealed class Floater
{
    public required CharacterId Victim;
    public required string Text;
    public required Vector3 Colour;
    public required float Size, Life, Rise, Lift, Side, Jitter;
    public required bool Big, IsWord;
    public float Value, Age;
    public Vector2? Last;
    public float LastBody;
}

/// <summary>
/// The numbers' life on the host thread (<b>Verified</b>, <c>damage_numbers.lua</c> <c>spawn</c>, <c>addToFloater</c>, <c>animate</c>): a hit
/// opens a "-N" floater, or adds to its victim's last one while it is younger than the merge window + 0.05 s; each frame the floaters age by
/// real time once a frame (<see cref="Meitou.Modding.ModContext.AddFrame"/>, projection by <see cref="ModFrame.Project"/>) into
/// <see cref="Drawn"/>, which the overlay draws. Healing (<c>ShowHealing</c>) is summed per character over a second of real time and shown as a
/// green "+N" from <c>HealingThreshold</c>.
/// </summary>
static class Floaters
{
    const float TypicalHeight = 18, HeadShare = 0.95f;

    static readonly List<Floater> Active = [];
    static readonly Dictionary<CharacterId, Floater> LastOf = [];
    static readonly Dictionary<CharacterId, float> LastSide = [];
    static readonly Dictionary<CharacterId, (float Healed, double Since)> Healing = [];
    static readonly Stopwatch Clock = Stopwatch.StartNew();
    static readonly Random Jitter = new();
    static double s_lastFrame;

    /// <summary>The floaters as laid out this frame, for <see cref="NumbersUi"/> (host thread).</summary>
    public static IReadOnlyList<DrawnFloater> Drawn { get; private set; } = [];

    static DamageSettings Settings => PoshDamageNumbersMod.Settings;

    #region Spawning

    static void Take()
    {
        while (HitWatch.Queue.TryDequeue(out var shown))
        {
            if (!Watchlist.Reads(shown.Victim)) continue;

            if (shown is DamageShown d) Damage(d);
            else if (shown is HealShown h) Heal(h);
            else if (shown is WordShown w) Spawn(WordFloater(w));
        }
    }

    /// <summary>Healing adds up per character; once its second is over, a "+N" when it reached the threshold (the original's window).</summary>
    static void Heal(HealShown h)
    {
        var (healed, since) = Healing.GetValueOrDefault(h.Victim, (0, Clock.Elapsed.TotalSeconds));
        Healing[h.Victim] = (healed + h.Healed, since);
    }

    static void EndHealing(double now)
    {
        foreach (var (victim, (healed, since)) in Healing.Where(h => now - h.Value.Since >= 1).ToList())
        {
            Healing.Remove(victim);
            if (healed < Settings.HealingThreshold) continue;

            Spawn(NumberFloater(victim, MathF.Floor(healed + 0.5f), 0, "+", Settings.Heal));
        }
    }

    static void Damage(DamageShown d)
    {
        float value = MathF.Floor(d.Damage + 0.5f);
        if (LastOf.TryGetValue(d.Victim, out var f) && !f.IsWord && f.Age < Settings.MergeWindow + 0.05f && Active.Contains(f))
        {
            AddTo(f, value, d.Ratio, d.Squad);
            return;
        }

        f = NumberFloater(d.Victim, value, d.Ratio, "-", d.Squad ? Settings.Squad : Severity(d.Ratio));
        Spawn(f);
        LastOf[d.Victim] = f;
    }

    /// <summary>A "-N" or "+N" floater, its size and life by the ratio, on the side opposite the victim's last one.</summary>
    static Floater NumberFloater(CharacterId victim, float value, float ratio, string sign, Vector3 colour)
    {
        float side = -LastSide.GetValueOrDefault(victim, 1);
        LastSide[victim] = side;
        bool big = ratio >= 0.3f;
        return new Floater
        {
            Victim = victim, Text = sign + Whole(value), Colour = colour, Size = 0.86f + Math.Clamp(ratio, 0, 1) * 0.7f,
            Life = (big ? 1.35f : 1.05f) * Settings.Duration, Rise = 1.25f * Settings.Rise, Lift = 0, Side = side,
            Jitter = (Jitter.NextSingle() - 0.5f) * 0.35f, Big = big, IsWord = false, Value = value,
        };
    }

    /// <summary>Another hit within the merge window: the total grows, the number grows a little and pops again from mid-pop.</summary>
    static void AddTo(Floater f, float value, float ratio, bool squad)
    {
        f.Value += value;
        f.Text = "-" + Whole(f.Value);
        float grow = Math.Clamp(ratio, 0, 1);
        f.Size = MathF.Min(1.75f, MathF.Max(f.Size, 0.86f + grow * 0.7f) + 0.06f);
        if (!squad) f.Colour = Severity(MathF.Max(grow, (f.Size - 0.86f) / 0.7f));
        f.Big |= ratio >= 0.3f;
        f.Age = 0.05f;
        f.Life = MathF.Max(f.Life, (f.Big ? 1.35f : 1.05f) * Settings.Duration);
    }

    static Floater WordFloater(WordShown w) => new()
    {
        Victim = w.Victim,
        Text = w.Word switch { Word.Knockout => "KNOCKOUT", Word.Severed => "SEVERED", _ => "KILLED" },
        Colour = w.Word switch { Word.Knockout => Settings.Ko, Word.Severed => Settings.Limb, _ => Settings.Dead },
        Size = 1.05f, Life = 1.7f * Settings.Duration, Rise = 0.9f * Settings.Rise, Lift = 1.15f, Side = 0, Jitter = (Jitter.NextSingle() - 0.5f) * 0.35f,
        Big = w.Word != Word.Knockout, IsWord = true,
    };

    static void Spawn(Floater f)
    {
        while (Active.Count >= Settings.MaxActive) Active.RemoveAt(0);
        Active.Add(f);
    }

    /// <summary>Light to medium up to 12% of the part's health, then medium to heavy by 37%.</summary>
    static Vector3 Severity(float ratio)
    {
        var light = Settings.Light;
        var medium = Settings.Medium;
        var heavy = Settings.Heavy;
        return ratio < 0.12f ? Vector3.Lerp(light, medium, ratio / 0.12f) : Vector3.Lerp(medium, heavy, Math.Clamp((ratio - 0.12f) / 0.25f, 0, 1));
    }

    static string Whole(float v) => ((long)v).ToString(CultureInfo.InvariantCulture);

    #endregion

    #region Frame

    /// <summary>Once a frame: takes the new hits, ages the floaters by real time and lays them out over their characters.</summary>
    public static void Frame(ModFrame frame)
    {
        double now = Clock.Elapsed.TotalSeconds;
        float dt = (float)Math.Clamp(now - s_lastFrame, 0, 0.25);
        s_lastFrame = now;
        Watchlist.Scan(frame, Settings);
        Take();
        EndHealing(now);
        if (!Settings.Enabled)
        {
            Active.Clear();
            Drawn = [];
            return;
        }

        var characters = frame.Snapshot.Characters.ToDictionary(c => c.Id);
        var drawn = new List<DrawnFloater>(Active.Count);
        for (int i = 0; i < Active.Count; i++)
        {
            var f = Active[i];
            f.Age += dt;
            if (f.Age >= f.Life || !Place(frame, f, characters))
            {
                Active.RemoveAt(i--);
                if (LastOf.GetValueOrDefault(f.Victim) == f) LastOf.Remove(f.Victim);
                continue;
            }

            drawn.Add(Layout(f));
        }

        Drawn = drawn;
    }

    /// <summary>The floater's anchor over the victim's head and the victim's height on screen; the last ones once it is gone.</summary>
    static bool Place(ModFrame frame, Floater f, Dictionary<CharacterId, CharacterSnapshot> characters)
    {
        if (characters.TryGetValue(f.Victim, out var c)
            && frame.Project(c.Position) is { } feet
            && frame.Project(c.Position + new Vector3(0, TypicalHeight, 0)) is { } top)
        {
            f.LastBody = MathF.Abs(feet.Y - top.Y);
            f.Last = Vector2.Lerp(feet, top, HeadShare);
        }

        return f.Last is not null;
    }

    /// <summary>
    /// The floater as drawn now (<b>Verified</b>, <c>animate</c>): the pop (0.35 to 1.28 in 0.1 s, back to 1 by 0.24 s, then 12% smaller while
    /// fading from 62% of its life), the fade (in over 0.05 s, out by easeInQuad from 62%), the rise (easeOutQuad of the life), the sideways arc
    /// (side x 0.55 + jitter, easeOutCubic), the big hit's shake (4 px at 1080p for 0.18 s) and the white flash (0.12 s). The offsets are in
    /// multiples of the number's base height, which the interface works out from the character's height on screen.
    /// </summary>
    static DrawnFloater Layout(Floater f)
    {
        float t = f.Age, k = t / f.Life;
        float s = t < 0.10f ? Lerp(0.35f, 1.28f, EaseOutCubic(t / 0.10f))
            : t < 0.24f ? Lerp(1.28f, 1, EaseInOutSine((t - 0.10f) / 0.14f))
            : 1;
        float fadeStart = f.Life * 0.62f, alpha = t < 0.05f ? t / 0.05f : 1;
        if (t > fadeStart)
        {
            float fade = (t - fadeStart) / (f.Life - fadeStart);
            alpha = 1 - fade * fade;
            s *= 1 - 0.12f * fade;
        }

        float dy = -f.Rise * EaseOutQuad(k) - f.Lift;
        float dx = (f.Side * 0.55f + f.Jitter) * EaseOutCubic(MathF.Min(1, k * 1.25f));
        var colour = Settings.Flash && t < 0.12f ? Vector3.Lerp(Vector3.One, f.Colour, t / 0.12f) : f.Colour;
        return new(f.Last!.Value, f.LastBody, f.Size, s, new(dx, dy), Shake(f, t), colour, Math.Clamp(alpha, 0, 1), f.Text);
    }

    static Vector2 Shake(Floater f, float t)
    {
        if (!f.Big || !Settings.Shake || t >= 0.18f) return Vector2.Zero;

        float amp = (1 - t / 0.18f) * 4;
        return new((Jitter.NextSingle() * 2 - 1) * amp, (Jitter.NextSingle() * 2 - 1) * amp);
    }

    static float Lerp(float a, float b, float t) => a + (b - a) * t;
    static float EaseOutCubic(float t) => 1 - (1 - t) * (1 - t) * (1 - t);
    static float EaseOutQuad(float t) => 1 - (1 - t) * (1 - t);
    static float EaseInOutSine(float t) => 0.5f - 0.5f * MathF.Cos(MathF.PI * t);

    #endregion
}

/// <summary>A floater as laid out this frame: its anchor and the character's height on screen, size, pop, offset, shake, colour, opacity and text.</summary>
sealed record DrawnFloater(Vector2 At, float Body, float Size, float Pop, Vector2 Offset, Vector2 Shake, Vector3 Colour, float Alpha, string Text);
