using System.Diagnostics;
using Meitou.Game.Host.Input;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Meitou.CharactersEmotions;

/// <summary>
/// The original's expression loop on the host's thread, once a frame (display only: nothing reaches the world): it finds the tracked player
/// characters, checks each one's situation, runs the blink, normal, hit and speech clocks in real time, and hands the renderer the weights of the
/// managed shape keys every <see cref="EmotionSettings.MorphFrequency"/> ms.
/// </summary>
sealed class FaceDriver(EmotionSettings settings, EmotionBook book, Action<string> log)
{
    /// <summary>The original waits this long after the character editor closes before it looks for characters again (its log line).</summary>
    const long EditorPause = 5000;

    static readonly IReadOnlyDictionary<string, float> Empty = new Dictionary<string, float>();

    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly Random _random = new();
    readonly Dictionary<CharacterId, Face> _faces = [];
    readonly HashSet<CharacterId> _banned = [];
    long _nextFind, _nextMorph, _editorUntil;

    long Now => _clock.ElapsedMilliseconds;

    public void Frame(ModFrame frame)
    {
        long now = Now;
        if (frame.IsOpen("editor") || now < _editorUntil)
        {
            if (frame.IsOpen("editor")) _editorUntil = now + EditorPause;
            Clear();
            return;
        }

        var snapshot = frame.Snapshot;
        if (now >= _nextFind)
        {
            _nextFind = now + settings.FindingFrequency;
            Find(snapshot, frame.Selected);
        }

        if (now < _nextMorph) return;

        _nextMorph = now + settings.MorphFrequency;
        Publish(snapshot, now);
    }

    void Clear()
    {
        _faces.Clear();
        if (FaceBoard.Weights.Count > 0) FaceBoard.Weights = new Dictionary<long, IReadOnlyDictionary<string, float>>();
    }

    /// <summary>
    /// The tracked characters: the selected player characters, or the first <see cref="EmotionSettings.CharacterCount"/> player characters in the
    /// snapshot's order; people only, alive, not banned.
    /// </summary>
    void Find(WorldSnapshot snapshot, IReadOnlyList<CharacterId> selected)
    {
        var wanted = new HashSet<CharacterId>();
        foreach (var c in snapshot.Characters)
        {
            if (!c.IsPlayer || c.Animal || c.Appearance is null || c.Body is { Dead: true } || _banned.Contains(c.Id)) continue;

            if (settings.OnlySelected ? !selected.Contains(c.Id) : wanted.Count >= settings.CharacterCount) continue;

            wanted.Add(c.Id);
        }

        foreach (var id in _faces.Keys.Where(id => !wanted.Contains(id)).ToList()) _faces.Remove(id);
        foreach (var id in wanted) _faces.TryAdd(id, new Face(id));
    }

    void Publish(WorldSnapshot snapshot, long now)
    {
        var speakers = snapshot.Speech.Select(s => s.Speaker).ToHashSet();
        var weights = new Dictionary<long, IReadOnlyDictionary<string, float>>();
        foreach (var c in snapshot.Characters)
        {
            if (!_faces.TryGetValue(c.Id, out var face)) continue;

            long key = FaceBoard.Pack(c.Id);
            var support = FaceBoard.Support.GetValueOrDefault(key);
            // Without the default keys (or before the renderer has seen the mesh) the entry only asks the renderer what the mesh supports.
            weights[key] = (support & FaceSupport.Base) == 0 ? Empty : Compute(face, c, now, speakers.Contains(c.Id), support);
        }

        FaceBoard.Weights = weights;
    }

    /// <summary>The managed keys' weights for one character now: the invalid keys and every unused managed key at 0.</summary>
    Dictionary<string, float> Compute(Face face, CharacterSnapshot c, long now, bool speaking, FaceSupport support)
    {
        if (now >= face.NextCheck)
        {
            face.State = Situation(c);
            face.NextCheck = now + Interval(settings.CheckFrequency, settings.CheckRandomize);
        }

        var w = new Dictionary<string, float>(StringComparer.Ordinal);
        Zero(w, book.Invalid);
        Zero(w, book.BaseKeys);
        bool anim = (support & FaceSupport.Anim) != 0 && settings.AnimEmotions;
        bool speech = (support & FaceSupport.Speech) != 0 && settings.SpeechMorphing;
        if (anim) Zero(w, book.AnimKeys);
        if (speech) Zero(w, EmotionBook.SpeechKeys);

        float hit = face.State == Emotion.KnockOut ? 0 : Attacked(face, now);
        var shown = hit > 0 ? Emotion.Attacked : face.State;
        var playing = anim ? AnimationEmotions.Match(book, c, shown) : null;

        float normal = NormalWeight(face, now);
        if (playing is not { Emotion.OnlyAnim: true }) Situational(w, face, normal, hit);

        bool closedEyes = face.State is Emotion.Sleep or Emotion.KnockOut || playing is { Emotion.IgnoreBlink: true };
        float blink = BlinkWeight(face, now);
        if (!closedEyes) Add(w, book.Blink, blink);

        if (playing is { } p) AnimationEmotions.Apply(w, p);
        if (speech) Speech(w, face, now, speaking);
        return w;
    }

    void Situational(Dictionary<string, float> w, Face face, float normal, float hit)
    {
        if (face.State == Emotion.Normal)
        {
            if (face.Normal >= 0 && face.Normal < book.Normals.Count) Add(w, book.Normals[face.Normal], normal * (1 - hit));
        }
        else if (book.Emotions.TryGetValue(face.State, out var keys)) Add(w, keys, 1 - hit);

        if (hit > 0 && book.Emotions.TryGetValue(Emotion.Attacked, out var attacked)) Add(w, attacked, hit);
    }

    /// <summary>
    /// The situation as the original's check orders it (decompile of its state test): out cold (not dead), on a bed (asleep, in a bed, or its
    /// sleep clips), in combat, a body part at or under the injury boundary of its maximum, hungry (hunger 2 or less of 3), seated (its sitting
    /// clips), else normal.
    /// </summary>
    Emotion Situation(CharacterSnapshot c)
    {
        var body = c.Body;
        if (body is { Unconscious: true, Dead: false }) return Emotion.KnockOut;

        if (c.Asleep || c.InBed || AnimationEmotions.Playing(c, "sleepinbed", "sleeponfloor")) return Emotion.Sleep;

        if (c.InCombat) return Emotion.Angry;

        if (body is { } b && b.WorstPart <= settings.HurtBoundary) return Emotion.Pain;

        if (body is { } h && h.Hunger <= 2) return Emotion.Sad;

        return AnimationEmotions.Playing(c, "sitting chair", "sitting idle") ? Emotion.Relax : Emotion.Normal;
    }

    /// <summary>
    /// The normal expression's weight: one of the <c>EmotionNormalN</c> picked at random when the character turns normal and again after
    /// <c>EmotionNormal_Frequency</c> +- <c>_Randomizer</c>; it morphs in over <c>_MorphTime</c>, holds <c>_KeepTime</c> and morphs out.
    /// </summary>
    float NormalWeight(Face face, long now)
    {
        if (face.State != Emotion.Normal || book.Normals.Count == 0)
        {
            face.Normal = -1;
            face.NextNormal = now;
            return 0;
        }

        if (face.Normal < 0 && now >= face.NextNormal)
        {
            face.Normal = _random.Next(book.Normals.Count);
            face.NormalStart = now;
        }

        if (face.Normal < 0) return 0;

        float t = now - face.NormalStart, morph = Math.Max(1, settings.NormalMorph), keep = settings.NormalKeep;
        if (t < morph) return t / morph;

        if (t < morph + keep) return 1;

        if (t < 2 * morph + keep) return 1 - (t - morph - keep) / morph;

        face.Normal = -1;
        face.NextNormal = now + Interval(settings.NormalFrequency, settings.NormalRandomizer);
        return 0;
    }

    /// <summary>A blink every <c>Blinking_Frequency</c> +- <c>_Randomizer</c>: closing and opening again over <c>Blinking_Time</c>.</summary>
    float BlinkWeight(Face face, long now)
    {
        if (face.NextBlink < 0) face.NextBlink = now + Interval(settings.BlinkFrequency, settings.BlinkRandomizer);

        if (face.BlinkStart < 0 && now >= face.NextBlink) face.BlinkStart = now;

        if (face.BlinkStart < 0) return 0;

        float t = now - face.BlinkStart, time = Math.Max(1, settings.BlinkTime);
        if (t < time) return 1 - Math.Abs(2 * t / time - 1);

        face.BlinkStart = -1;
        face.NextBlink = now + Interval(settings.BlinkFrequency, settings.BlinkRandomizer);
        return 0;
    }

    /// <summary>The hit expression after a wound: full for <c>EmotionAttacked_KeepTime</c>, then fading over <c>_OffTime</c> (decompile).</summary>
    float Attacked(Face face, long now)
    {
        if (face.HitAt == long.MinValue) return 0;

        long t = now - face.HitAt;
        if (t <= settings.AttackedKeep) return 1;

        long off = t - settings.AttackedKeep;
        return off < settings.AttackedOff ? 1 - off / (float)settings.AttackedOff : 0;
    }

    /// <summary>
    /// The mouth while the character's line shows: a random vowel key every <c>UseSpeechMorphin_Frequency</c>, each crossfading from the last over
    /// <c>UseSpeechMorphin_Time</c>, the last fading out after the line.
    /// </summary>
    void Speech(Dictionary<string, float> w, Face face, long now, bool speaking)
    {
        float time = Math.Max(1, settings.SpeechTime);
        if (speaking)
        {
            face.SpeechEnd = now;
            if (now >= face.NextVowel)
            {
                face.PreviousVowel = face.Vowel;
                face.Vowel = (Math.Max(face.Vowel, 0) + 1 + _random.Next(EmotionBook.SpeechKeys.Length - 1)) % EmotionBook.SpeechKeys.Length;
                face.VowelAt = now;
                face.NextVowel = now + settings.SpeechFrequency;
            }
        }

        if (face.Vowel < 0) return;

        float into = Math.Min(1, (now - face.VowelAt) / time);
        float fade = speaking ? 1 : 1 - (now - face.SpeechEnd) / time;
        if (fade <= 0)
        {
            face.Vowel = face.PreviousVowel = -1;
            return;
        }

        w[EmotionBook.SpeechKeys[face.Vowel]] = into * fade;
        if (face.PreviousVowel >= 0) w[EmotionBook.SpeechKeys[face.PreviousVowel]] = (1 - into) * fade;
    }

    /// <summary><paramref name="frequency"/> +- <paramref name="spread"/> ms, from 0 to an hour (the original's interval roll).</summary>
    long Interval(int frequency, int spread) => Math.Clamp(frequency + _random.Next(-spread, spread + 1), 0, 3_600_000);

    static void Zero(Dictionary<string, float> w, IEnumerable<string> keys)
    {
        foreach (string key in keys) w[key] = 0;
    }

    static void Add(Dictionary<string, float> w, List<KeyWeight> keys, float scale)
    {
        if (scale <= 0) return;

        foreach (var k in keys) w[k.Key] = Math.Max(w.GetValueOrDefault(k.Key), k.Weight * scale);
    }

    /// <summary>The frame's hits: a tracked victim shows the hit expression (the original's wound hook).</summary>
    public void Hits(IReadOnlyList<CombatHit> hits)
    {
        long now = Now;
        foreach (var hit in hits)
        {
            if (hit.VictimWasAlive && _faces.TryGetValue(hit.Victim, out var face)) face.HitAt = now;
        }
    }

    /// <summary>The ban key: the selected player characters stop showing expressions, or start again.</summary>
    public void ToggleBan(ModKeyContext context)
    {
        foreach (var id in context.Selected)
        {
            bool banned = _banned.Add(id);
            if (!banned) _banned.Remove(id);

            if (banned) _faces.Remove(id);
            log(banned ? $"character {id} added to the unsupported list" : $"character {id} removed from the unsupported list");
        }

        _nextFind = 0;
    }
}
