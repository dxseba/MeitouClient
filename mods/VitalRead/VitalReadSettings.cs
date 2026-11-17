using Meitou.Modding;

namespace Meitou.VitalRead;

/// <summary>The portrait corner a badge sits in (the original's <c>portraitTextAnchor</c> values).</summary>
public enum Corner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>The original's Mod Hub settings that apply without icons, under Mod settings; its defaults (Vital-Read <c>mod-config.json</c>).</summary>
public sealed class VitalReadSettings
{
    [Setting("Enable Vital Read", Hint = "Marks squad portraits with the character's worst state (ZZ, DY, PD...).")]
    public bool Enabled { get; set; } = true;

    [Setting("Show text", Hint = "Draws the state badge as text.")]
    public bool ShowText { get; set; } = true;

    [Setting("Coloured text", Hint = "Tints each badge by its state, in place of the original's icons.")]
    public bool Coloured { get; set; } = true;

    [Setting("Text size", Hint = "Badge text height in pixels at 1080p.", Min = 8, Max = 40, Step = 1)]
    public int TextSize { get; set; } = 18;

    [Setting("Anchor", Hint = "The portrait corner the badge sits in.")]
    public Corner Anchor { get; set; } = Corner.TopRight;

    [Setting("Margin", Hint = "Pixels at 1080p between the badge and the portrait's edge; negative moves it outward.", Min = -20, Max = 20, Step = 1)]
    public int Margin { get; set; }
}
