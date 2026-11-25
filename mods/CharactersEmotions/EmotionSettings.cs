using Meitou.Modding;

namespace Meitou.CharactersEmotions;

/// <summary>
/// The original's <c>CharactersEmotions_settings.ini</c> (its F6 menu), under Mod settings: the same values, defaults and English labels (its
/// <c>localization/en_GB.txt</c>). Times are milliseconds of real time, as the original's (<c>GetTickCount</c>). The menu key itself is not ported:
/// Meitou's Settings page is the menu; the ban key is a key action.
/// </summary>
public sealed class EmotionSettings
{
    [Setting("Status Check Frequency (ms)", Min = 10, Max = 2000, Step = 10, SubGroup = "Master Settings")]
    public int CheckFrequency { get; set; } = 100;

    [Setting("Status Check Randomizer (ms)", Min = 0, Max = 1000, Step = 5, SubGroup = "Master Settings")]
    public int CheckRandomize { get; set; } = 20;

    [Setting("Character Finding Frequency (ms)", Min = 100, Max = 10000, Step = 100, SubGroup = "Master Settings")]
    public int FindingFrequency { get; set; } = 1000;

    [Setting("Morph Animation Frequency (ms)", Min = 0, Max = 256, Step = 1, SubGroup = "Master Settings")]
    public int MorphFrequency { get; set; } = 33;

    [Setting("Only Selected Character", SubGroup = "Master Settings")]
    public bool OnlySelected { get; set; }

    [Setting("Number of Characters", Hint = "EnableOnlySelectedCharacter must be False to use this value.", Min = 0, Max = 256, Step = 1,
        SubGroup = "Master Settings")]
    public int CharacterCount { get; set; } = 30;

    [Setting("Enable Animation Emotions", SubGroup = "Animation Emotions")]
    public bool AnimEmotions { get; set; } = true;

    [Setting("Enable Speech Morphing", SubGroup = "Master Settings")]
    public bool SpeechMorphing { get; set; } = true;

    [Setting("Speech Morph Change Frequency (ms)", Min = 10, Max = 2000, Step = 10, SubGroup = "Master Settings")]
    public int SpeechFrequency { get; set; } = 100;

    [Setting("Speech Morph Transition Time (ms)", Min = 0, Max = 2000, Step = 10, SubGroup = "Master Settings")]
    public int SpeechTime { get; set; } = 100;

    [Setting("Injury Boundary (1.0=100%)", Min = 0, Max = 1, Step = 0.05, SubGroup = "Status Check")]
    public float HurtBoundary { get; set; } = 0.7f;

    [Setting("Blinking Frequency (ms)", Min = 100, Max = 20000, Step = 100, SubGroup = "Blinking")]
    public int BlinkFrequency { get; set; } = 2000;

    [Setting("Blinking Randomizer (ms)", Min = 0, Max = 10000, Step = 100, SubGroup = "Blinking")]
    public int BlinkRandomizer { get; set; } = 1500;

    [Setting("Blinking Time (ms)", Min = 10, Max = 2000, Step = 10, SubGroup = "Blinking")]
    public int BlinkTime { get; set; } = 200;

    [Setting("Normal Expression Frequency (ms)", Min = 0, Max = 60000, Step = 100, SubGroup = "Normal Expression")]
    public int NormalFrequency { get; set; } = 3500;

    [Setting("Normal Expression Randomizer (ms)", Min = 0, Max = 30000, Step = 100, SubGroup = "Normal Expression")]
    public int NormalRandomizer { get; set; } = 3000;

    [Setting("Normal Expression Keep Time (ms)", Min = 0, Max = 60000, Step = 100, SubGroup = "Normal Expression")]
    public int NormalKeep { get; set; } = 5000;

    [Setting("Normal Expression Morph Time (ms)", Min = 0, Max = 10000, Step = 50, SubGroup = "Normal Expression")]
    public int NormalMorph { get; set; } = 800;

    [Setting("Attacked Keep Time (ms)", Min = 0, Max = 10000, Step = 50, SubGroup = "Attacked Expression")]
    public int AttackedKeep { get; set; } = 500;

    [Setting("Attacked Fade Time (ms)", Min = 0, Max = 10000, Step = 50, SubGroup = "Attacked Expression")]
    public int AttackedOff { get; set; } = 1000;
}
