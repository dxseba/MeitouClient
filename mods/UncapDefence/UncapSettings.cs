using Meitou.Modding;

namespace Meitou.UncapDefence;

/// <summary>
/// The original's <c>UncapDefenceSuccessRate.ini</c> values (<c>[UncapDefenceSuccessRate]</c> Mode 1, Scale 25, Threshold 30, Ceiling 0.99, Floor
/// 0.01, Lang, Hidden), under Mod settings; the cards set them too.
/// </summary>
public sealed class UncapSettings
{
    public const string Off = "Off", Amplify = "Amplify", Threshold = "Threshold", English = "English", Chinese = "Chinese";

    internal static readonly string[] Modes = [Off, Amplify, Threshold];
    internal static readonly string[] Scales = ["15", "25", "40", "60"];
    internal static readonly string[] Gaps = ["15", "30", "50", "80"];
    internal static readonly string[] Ceilings = ["95", "99", "99.9", "100"];

    [Setting("Mode", Hint = "Off: vanilla. Amplify: the wider the level gap, the nearer the ceiling. Threshold: the ceiling at the full gap.",
        Options = [Off, Amplify, Threshold])]
    public string Mode { get; set; } = Amplify;

    [Setting("Pressure scale (levels)", Hint = "Amplify: the gap at which about 63 % of the way to the ceiling (or to 0) is made.",
        Options = ["15", "25", "40", "60"])]
    public string Scale { get; set; } = "25";

    [Setting("Full gap (levels)", Hint = "Threshold: the gap at which the ceiling (or 0) is reached.", Options = ["15", "30", "50", "80"])]
    public string FullGap { get; set; } = "30";

    [Setting("Ceiling (%)", Hint = "The highest block or dodge chance.", Options = ["95", "99", "99.9", "100"])]
    public string Ceiling { get; set; } = "99";

    [Setting("Floor", Hint = "The lowest block or dodge chance, as a fraction (the original's Floor, at most 0.5).", Min = 0, Max = 0.5,
        Step = 0.01)]
    public float Floor { get; set; } = 0.01f;

    [Setting("Card language", Options = [English, Chinese])]
    public string Language { get; set; } = English;

    [Setting("Hide the cards", Hint = "The toggle key (Ctrl+K by default, Controls page) shows and hides them too.")]
    public bool Hidden { get; set; }

    internal bool InChinese => Language == Chinese;
}
