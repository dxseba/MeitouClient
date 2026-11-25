using Meitou.Simulation;

namespace Meitou.CharactersEmotions;

/// <summary>
/// What the faces can show, as the renderer last found the character's body mesh: every default key (else nothing is shown), the animation keys, the
/// speech keys.
/// </summary>
[Flags]
enum FaceSupport
{
    None = 0,
    Base = 1,
    Anim = 2,
    Speech = 4,
}

/// <summary>
/// One tracked character's expression clock (real milliseconds, as the original's): its situation, blink, normal expression, the last hit and the
/// speech vowels. Host thread only.
/// </summary>
sealed class Face(CharacterId id)
{
    public CharacterId Id { get; } = id;
    public Emotion State = Emotion.Normal;
    public long NextCheck;
    public long NextBlink = -1, BlinkStart = -1;
    public int Normal = -1;
    public long NormalStart, NextNormal = -1;
    public long HitAt = long.MinValue;
    public int Vowel = -1, PreviousVowel = -1;
    public long VowelAt, NextVowel, SpeechEnd = long.MinValue;
}
