using Meitou.Simulation;

namespace Meitou.CharactersEmotions;

/// <summary>An animation emotion playing on a character, at a frame of its <c>TotalAnimFrame</c>.</summary>
readonly record struct PlayingEmotion(AnimEmotion Emotion, float Frame);

/// <summary>
/// The animation-driven expressions (readme_animemotions_eng.txt): a section whose names hold a clip playing on the character follows that clip's
/// place, the clip's fraction (its <c>currentFrameTime01</c>, here time over length) times <c>TotalAnimFrame</c>.
/// </summary>
static class AnimationEmotions
{
    /// <summary>Whether one of <paramref name="names"/> is playing on <paramref name="c"/> (with a weight).</summary>
    public static bool Playing(CharacterSnapshot c, params string[] names)
    {
        foreach (var layer in c.Animations)
        {
            if (layer.Weight > 0 && names.Contains(layer.Name, StringComparer.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <summary>
    /// The first section with a clip playing on <paramref name="c"/> (its heaviest layer first) whose <c>EmotionPriority</c> lets it show over
    /// <paramref name="shown"/>: with priority p it shows while the default expression is one of the first p of Normal, Relax, Sad, Pain, Angry,
    /// Sleep, Attacked, KnockOut. Null for none.
    /// </summary>
    public static PlayingEmotion? Match(EmotionBook book, CharacterSnapshot c, Emotion shown)
    {
        if (book.Animations.Count == 0) return null;

        foreach (var layer in c.Animations.Where(l => l.Weight > 0 && l.Length > 0).OrderByDescending(l => l.Weight))
        {
            foreach (var anim in book.Animations)
            {
                if ((int)shown >= anim.Priority || !anim.Animations.Contains(layer.Name, StringComparer.OrdinalIgnoreCase)) continue;

                float fraction = layer.Time / layer.Length;
                fraction -= MathF.Floor(fraction);
                return new PlayingEmotion(anim, fraction * anim.TotalFrames);
            }
        }

        return null;
    }

    /// <summary>
    /// Each track's weight at the frame, set over the default expression's; a track before its first or after its last control point leaves the
    /// key as it was. Frame 0 is only a reference point (the readme): the frame counts from 1.
    /// </summary>
    public static void Apply(Dictionary<string, float> w, PlayingEmotion playing)
    {
        float frame = Math.Max(playing.Frame, 1);
        foreach (var track in playing.Emotion.Tracks)
        {
            if (track.At(frame) is { } weight) w[track.Key] = weight;
        }
    }
}
