using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Data.Ui;
using Meitou.Game.Host;
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
/// real time and are laid out in the HUD bindings by a postfix on <c>PlayerInterface.BindUi</c> (projection by its <c>Project</c>).
/// </summary>
static class Floaters
{
    public const string Key = "posh.dmg.";
    public const int Slots = 40;
    const float TypicalHeight = 18, HeadShare = 0.95f;

    static readonly Vector3 Light = Hex(0xFFF5DB), Medium = Hex(0xFFD140), Heavy = Hex(0xFF7A1F), Squad = Hex(0xFF4040);
    static readonly Vector3 Ko = Hex(0x96BFFF), Limb = Hex(0xFF593D), Dead = Hex(0xD91A1A);

    static readonly List<Floater> Active = [];
    static readonly Dictionary<CharacterId, Floater> LastOf = [];
    static readonly Dictionary<CharacterId, float> LastSide = [];
    static readonly Stopwatch Clock = Stopwatch.StartNew();
    static readonly Random Jitter = new();
    static double s_lastFrame;
    static FieldInfo? s_sessionField;
    static MethodInfo? s_project;

    static DamageSettings Settings => PoshDamageNumbersMod.Settings;

    static Vector3 Hex(uint rgb) => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);

    public static void Install(ModContext context)
    {
        var type = AccessTools.TypeByName("Meitou.Game.PlayerInterface") ?? throw new TypeLoadException("Meitou.Game.PlayerInterface");
        s_sessionField = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(f => f.FieldType == typeof(WorldSession));
        s_project = AccessTools.Method(type, "Project", [typeof(Vector3)]) ?? throw new MissingMethodException(type.FullName, "Project");
        var bind = AccessTools.Method(type, "BindUi") ?? throw new MissingMethodException(type.FullName, "BindUi");
        context.Harmony.Patch(bind, postfix: new HarmonyMethod(typeof(Floaters), nameof(Bound)));
    }

    #region Spawning

    static void Take()
    {
        while (HitWatch.Queue.TryDequeue(out var shown))
        {
            if (shown is DamageShown d) Damage(d);
            else if (shown is WordShown w) Spawn(WordFloater(w));
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

        float side = -LastSide.GetValueOrDefault(d.Victim, 1);
        LastSide[d.Victim] = side;
        float ratio = Math.Clamp(d.Ratio, 0, 1);
        bool big = d.Ratio >= 0.3f;
        f = new Floater
        {
            Victim = d.Victim, Text = "-" + Whole(value), Colour = d.Squad ? Squad : Severity(d.Ratio), Size = 0.86f + ratio * 0.7f,
            Life = (big ? 1.35f : 1.05f) * Settings.Duration, Rise = 1.25f * Settings.Rise, Lift = 0, Side = side,
            Jitter = (Jitter.NextSingle() - 0.5f) * 0.35f, Big = big, IsWord = false, Value = value,
        };
        Spawn(f);
        LastOf[d.Victim] = f;
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
        Colour = w.Word switch { Word.Knockout => Ko, Word.Severed => Limb, _ => Dead },
        Size = 1.05f, Life = 1.7f * Settings.Duration, Rise = 0.9f * Settings.Rise, Lift = 1.15f, Side = 0, Jitter = (Jitter.NextSingle() - 0.5f) * 0.35f,
        Big = w.Word != Word.Knockout, IsWord = true,
    };

    static void Spawn(Floater f)
    {
        while (Active.Count >= Settings.MaxActive) Active.RemoveAt(0);
        Active.Add(f);
    }

    /// <summary>Light to medium up to 12% of the part's health, then medium to heavy by 37%.</summary>
    static Vector3 Severity(float ratio) => ratio < 0.12f
        ? Vector3.Lerp(Light, Medium, ratio / 0.12f)
        : Vector3.Lerp(Medium, Heavy, Math.Clamp((ratio - 0.12f) / 0.25f, 0, 1));

    static string Whole(float v) => ((long)v).ToString(CultureInfo.InvariantCulture);

    #endregion

    #region Frame

#pragma warning disable IDE1006 // Harmony binds __result / __instance by name
    static void Bound(object __instance, UiBindings __result)
#pragma warning restore IDE1006
    {
        double now = Clock.Elapsed.TotalSeconds;
        float dt = (float)Math.Clamp(now - s_lastFrame, 0, 0.25);
        s_lastFrame = now;
        Take();
        if (!Settings.Enabled || s_sessionField?.GetValue(__instance) is not WorldSession session)
        {
            Active.Clear();
            return;
        }

        var characters = session.CurrentSnapshot.Characters.ToDictionary(c => c.Id);
        int slot = 0;
        for (int i = 0; i < Active.Count; i++)
        {
            var f = Active[i];
            f.Age += dt;
            if (f.Age >= f.Life || !Place(__instance, f, characters))
            {
                Active.RemoveAt(i--);
                if (LastOf.GetValueOrDefault(f.Victim) == f) LastOf.Remove(f.Victim);
                continue;
            }

            if (slot < Slots) __result.Text[Key + slot++] = Binding(f);
        }
    }

    /// <summary>The floater's anchor over the victim's head and the victim's height on screen; the last ones once it is gone.</summary>
    static bool Place(object ui, Floater f, Dictionary<CharacterId, CharacterSnapshot> characters)
    {
        if (characters.TryGetValue(f.Victim, out var c)
            && s_project!.Invoke(ui, [c.Position]) is Vector2 feet
            && s_project.Invoke(ui, [c.Position + new Vector3(0, TypicalHeight, 0)]) is Vector2 top)
        {
            f.LastBody = MathF.Abs(feet.Y - top.Y);
            f.Last = Vector2.Lerp(feet, top, HeadShare);
        }

        return f.Last is not null;
    }

    /// <summary>
    /// "x|y|bodyPx|size|pop|dx|dy|shakeX|shakeY|#RRGGBB|text" for <see cref="NumbersUi"/>: the original's pop (0.35 to 1.28 in 0.1 s, back to 1 by 0.24 s, then 12% smaller
    /// while fading from 62% of its life), its rise (easeOutQuad of the life), its sideways arc (side x 0.55 + jitter, easeOutCubic), the big hit's
    /// shake (4 px at 1080p for 0.18 s) and the white flash (0.12 s). The offsets are in multiples of the number's base height, which the
    /// interface works out from the character's height on screen.
    /// </summary>
    static string Binding(Floater f)
    {
        float t = f.Age, k = t / f.Life;
        float s = t < 0.10f ? Lerp(0.35f, 1.28f, EaseOutCubic(t / 0.10f))
            : t < 0.24f ? Lerp(1.28f, 1, EaseInOutSine((t - 0.10f) / 0.14f))
            : 1;
        float fadeStart = f.Life * 0.62f;
        if (t > fadeStart) s *= 1 - 0.12f * ((t - fadeStart) / (f.Life - fadeStart));

        float dy = -f.Rise * EaseOutQuad(k) - f.Lift;
        float dx = (f.Side * 0.55f + f.Jitter) * EaseOutCubic(MathF.Min(1, k * 1.25f));
        float shakeX = 0, shakeY = 0;
        if (f.Big && Settings.Shake && t < 0.18f)
        {
            float amp = (1 - t / 0.18f) * 4;
            (shakeX, shakeY) = ((Jitter.NextSingle() * 2 - 1) * amp, (Jitter.NextSingle() * 2 - 1) * amp);
        }

        var colour = Settings.Flash && t < 0.12f ? Vector3.Lerp(Vector3.One, f.Colour, t / 0.12f) : f.Colour;
        var at = f.Last!.Value;
        return string.Create(CultureInfo.InvariantCulture,
            $"{at.X:0.#}|{at.Y:0.#}|{f.LastBody:0.#}|{f.Size:0.###}|{s:0.###}|{dx:0.###}|{dy:0.###}|{shakeX:0.#}|{shakeY:0.#}|{Tag(colour)}|{f.Text}");
    }

    static string Tag(Vector3 c) => string.Create(CultureInfo.InvariantCulture,
        $"#{(int)(Math.Clamp(c.X, 0, 1) * 255 + 0.5f):X2}{(int)(Math.Clamp(c.Y, 0, 1) * 255 + 0.5f):X2}{(int)(Math.Clamp(c.Z, 0, 1) * 255 + 0.5f):X2}");

    static float Lerp(float a, float b, float t) => a + (b - a) * t;
    static float EaseOutCubic(float t) => 1 - (1 - t) * (1 - t) * (1 - t);
    static float EaseOutQuad(float t) => 1 - (1 - t) * (1 - t);
    static float EaseInOutSine(float t) => 0.5f - 0.5f * MathF.Cos(MathF.PI * t);

    #endregion
}
