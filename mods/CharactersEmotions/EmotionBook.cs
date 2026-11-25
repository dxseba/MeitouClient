using System.Globalization;

namespace Meitou.CharactersEmotions;

/// <summary>
/// The default expressions, by situation, in the original's priority order from the lowest (its <c>EmotionPriority</c> scale: Normal 0 ... KnockOut
/// 7). Attacked is an overlay on the others (below KnockOut).
/// </summary>
enum Emotion
{
    Normal,
    Relax,
    Sad,
    Pain,
    Angry,
    Sleep,
    Attacked,
    KnockOut,
}

/// <summary>One shape key of an expression and its weight, 0..1 (the ini's 0..100 over 100).</summary>
readonly record struct KeyWeight(string Key, float Weight);

/// <summary>
/// One shape key track of an animation emotion: control points at whole animation frames, weights 0..1 between them linearly.
/// </summary>
sealed record AnimTrack(string Key, int[] Frames, float[] Weights)
{
    /// <summary>The track's weight at <paramref name="frame"/>; null before its first or after its last control point.</summary>
    public float? At(float frame)
    {
        if (frame < Frames[0] || frame > Frames[^1]) return null;

        for (int i = 1; i < Frames.Length; i++)
        {
            if (frame > Frames[i]) continue;

            float t = (frame - Frames[i - 1]) / (Frames[i] - Frames[i - 1]);
            return Weights[i - 1] + (Weights[i] - Weights[i - 1]) * t;
        }

        return Weights[^1];
    }
}

/// <summary>An animation emotion section of <c>CharactersEmotions_animemotions.ini</c>.</summary>
sealed record AnimEmotion(string[] Animations, int TotalFrames, int Priority, bool IgnoreBlink, bool OnlyAnim, AnimTrack[] Tracks);

/// <summary>
/// The expressions as the original's two ini files define them (<c>CharactersEmotions_emotions.ini</c>, <c>CharactersEmotions_animemotions.ini</c>,
/// read from the installed original; readme_eng.txt and readme_animemotions_eng.txt give the rules). Without the files the shipped files' content
/// (the documented shape key names at 100) is used.
/// </summary>
sealed class EmotionBook
{
    /// <summary>The original's five speech keys (settings ini comment): all five or speech morphing is off for the character.</summary>
    public static readonly string[] SpeechKeys = ["CE_SpeechA", "CE_SpeechI", "CE_SpeechU", "CE_SpeechE", "CE_SpeechO"];

    static readonly (Emotion Emotion, string Section, string Key)[] Defaults =
    [
        (Emotion.Angry, "EmotionAngry", "CE_InCombat"), (Emotion.Sad, "EmotionSad", "CE_Starving"), (Emotion.Pain, "EmotionPain", "CE_Hurt"),
        (Emotion.Relax, "EmotionRelax", "CE_Resting"), (Emotion.Sleep, "EmotionSleep", "CE_Sleeping"),
        (Emotion.Attacked, "EmotionAttacked", "CE_Damaged"), (Emotion.KnockOut, "EmotionKnockOut", "CE_KnockOut"),
    ];

    public List<string> Invalid { get; } = [];
    public List<KeyWeight> Blink { get; } = [];
    public List<List<KeyWeight>> Normals { get; } = [];
    public Dictionary<Emotion, List<KeyWeight>> Emotions { get; } = [];
    public List<string> AnimRequired { get; } = [];
    public List<AnimEmotion> Animations { get; } = [];

    /// <summary>Every shape key the default expressions use: a character needs them all (readme_eng.txt).</summary>
    public HashSet<string> BaseKeys { get; } = new(StringComparer.Ordinal);

    /// <summary>Every shape key the animation emotions use.</summary>
    public HashSet<string> AnimKeys { get; } = new(StringComparer.Ordinal);

    /// <summary>The expressions from the two files (either may be null: its defaults are used); problems go to <paramref name="log"/>.</summary>
    public static EmotionBook Load(IniFile? emotions, IniFile? animations, Action<string> log)
    {
        var book = new EmotionBook();
        book.ReadEmotions(emotions, log);
        book.ReadAnimations(animations, log);
        var used = book.Blink.Concat(book.Normals.SelectMany(n => n)).Concat(book.Emotions.Values.SelectMany(e => e));
        foreach (var k in used) book.BaseKeys.Add(k.Key);
        foreach (var t in book.Animations.SelectMany(a => a.Tracks)) book.AnimKeys.Add(t.Key);
        foreach (var k in book.AnimRequired) book.AnimKeys.Add(k);
        return book;
    }

    void ReadEmotions(IniFile? ini, Action<string> log)
    {
        if (ini is null)
        {
            log("emotions ini not found: the shipped default expressions are used");
            UseDefaultEmotions();
            return;
        }

        if (ini.Get("Master")?.GetValueOrDefault("InvalidShapeKey") is { } invalid) Invalid.AddRange(List(invalid));

        Blink.AddRange(Keys(ini.Get("Blinking"), "Blinking", log));
        foreach (var (name, values) in ini.Sections)
        {
            if (name.StartsWith("EmotionNormal", StringComparison.OrdinalIgnoreCase) && Keys(values, name, log) is { Count: > 0 } normal)
                Normals.Add(normal);
        }

        if (Normals.Count == 0) log("No usable [EmotionNormalN] Shape Key sections were found.");

        foreach (var (emotion, section, _) in Defaults) Emotions[emotion] = Keys(ini.Get(section), section, log);
    }

    void UseDefaultEmotions()
    {
        Blink.Add(new("CE_Blinking", 1));
        for (int i = 1; i <= 3; i++) Normals.Add([new($"CE_Normal{i}", 1)]);
        foreach (var (emotion, _, key) in Defaults) Emotions[emotion] = [new(key, 1)];
    }

    /// <summary>
    /// A section's <c>ShapeKeyN</c> / <c>ShapeKeyNAmount</c> pairs, N from 1 up to the first gap; amounts clamped to 0..100.
    /// </summary>
    static List<KeyWeight> Keys(IReadOnlyDictionary<string, string>? section, string name, Action<string> log)
    {
        var keys = new List<KeyWeight>();
        if (section is null) return keys;

        for (int n = 1; section.TryGetValue($"ShapeKey{n}", out var key); n++)
        {
            if (key.Length == 0) continue;

            float amount = section.TryGetValue($"ShapeKey{n}Amount", out var text) && float.TryParse(text, NumberStyles.Float,
                CultureInfo.InvariantCulture, out float a) ? a : 100;
            keys.Add(new(key, Math.Clamp(amount, 0, 100) / 100));
        }

        if (keys.Count == 0) log($"[{name}] contains no usable ShapeKey entries.");
        return keys;
    }

    void ReadAnimations(IniFile? ini, Action<string> log)
    {
        if (ini is null)
        {
            log("animation emotions ini not found: no animation emotions");
            return;
        }

        if (ini.Get("Master")?.GetValueOrDefault("RequirementShapeKey") is { } required) AnimRequired.AddRange(List(required));
        else log("[Master] RequirementShapeKey is missing from CharactersEmotions_animemotions.ini.");

        foreach (var (name, values) in ini.Sections)
        {
            if (string.Equals(name, "Master", StringComparison.OrdinalIgnoreCase)) continue;

            if (Animation(name, values, log) is { } anim) Animations.Add(anim);
        }
    }

    static AnimEmotion? Animation(string name, IReadOnlyDictionary<string, string> values, Action<string> log)
    {
        string[] anims = [.. List(name)];
        if (anims.Length == 0) return null;

        if (!values.TryGetValue("TotalAnimFrame", out var totalText) || !int.TryParse(totalText, CultureInfo.InvariantCulture, out int total)
                || total <= 0)
        {
            log($"[{name}] TotalAnimFrame is missing or invalid; the animation section was skipped.");
            return null;
        }

        int priority = values.TryGetValue("EmotionPriority", out var p) && int.TryParse(p, CultureInfo.InvariantCulture, out int pr)
            && pr is >= 0 and <= 7 ? pr : 0;
        var tracks = Tracks(name, values, log);
        if (tracks.Count == 0)
        {
            log($"[{name}] contains no usable animation Shape Key tracks; skipped.");
            return null;
        }

        return new AnimEmotion(anims, total, priority, Bool(values, "IgnoreBlink"), Bool(values, "OnlyAnimEmotion"), [.. tracks]);
    }

    static List<AnimTrack> Tracks(string name, IReadOnlyDictionary<string, string> values, Action<string> log)
    {
        var tracks = new List<AnimTrack>();
        for (int n = 1; values.TryGetValue($"ShapeKey{n}", out var key); n++)
        {
            int[]? frames = Numbers(values.GetValueOrDefault($"ShapeKey{n}Frame"))?.Select(f => (int)f).ToArray();
            float[]? amounts = Numbers(values.GetValueOrDefault($"ShapeKey{n}Amount"));
            if (frames is null || amounts is null || frames.Length != amounts.Length || frames.Length == 0)
            {
                log($"[{name}] ShapeKey{n} Amount/Frame lists are invalid or have different lengths; skipped.");
                continue;
            }

            if (frames.Where((f, i) => f < 0 || i > 0 && f <= frames[i - 1]).Any())
            {
                log($"[{name}] ShapeKey{n} frames must be non-negative and strictly ascending; skipped.");
                continue;
            }

            tracks.Add(new AnimTrack(key, frames, [.. amounts.Select(a => Math.Clamp(a, 0, 100) / 100)]));
        }

        return tracks;
    }

    static float[]? Numbers(string? text)
    {
        if (text is null) return null;

        var parts = text.Split(',');
        var result = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result[i])) return null;
        }

        return result;
    }

    static bool Bool(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var v) && string.Equals(v, "True", StringComparison.OrdinalIgnoreCase);

    static IEnumerable<string> List(string text) => text.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0);
}
