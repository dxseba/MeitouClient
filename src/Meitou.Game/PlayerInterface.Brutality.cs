using System.Globalization;
using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Simulation.Combat;

namespace Meitou.Game;

sealed partial class PlayerInterface
{
    void AddBrutalityDescription(List<UiControl> controls)
    {
        if (!GoreEnabled) return;
        if (controls.FirstOrDefault(c => c.Name == "Description1Panel") is not { } left
            || controls.FirstOrDefault(c => c.Name == "Description2Panel") is not { } right) return;

        var lead = Selected().FirstOrDefault();
        float level = lead?.BrutalityLevel ?? 0, progress = lead?.BrutalityProgress ?? 0;
        float scale = InventoryScale, row = Math.Min(20 * scale, (left.Bounds.Height - 4 * scale) / 9);
        float y = left.Bounds.Y + 4 * scale;
        string ink = MyGuiColours.TemplateInk(uiResources, "Kenshi_GenericTextBoxSkin");
        var leftClip = left.Bounds.Intersect(left.Clip ?? left.Bounds);
        var rightClip = right.Bounds.Intersect(right.Clip ?? right.Bounds);

        Line("stat-description-title", "Brutality", TitleColour, 18);
        Line("stat-affected-heading", "Combat effects", MainColour);
        Line("brutality-unlock-scalp", "20: Scalp wounds", ink);
        Line("brutality-unlock-skull", "40: Major head wounds, decapitation", ink);
        Line("brutality-unlock-cleave", "60: Body cleaves", ink);
        Line("stat-training-heading", "Training", MainColour);
        Line("brutality-training-melee", "Fight capable enemies in melee", ink);
        Line("brutality-training-challenge", "Stronger opponents improve experience", ink);
        Line("brutality-training-helpless", "Helpless targets grant no experience", ink);

        var bar = new UiRect(right.Bounds.X + 17 * scale, right.Bounds.Y + 8 * scale,
            Math.Max(0, right.Bounds.Width - 30 * scale), 12 * scale);
        Progress("stat-next-level-track", "Kenshi_ProgressBarEmptySkin", bar);
        if (progress > 0) Progress("stat-next-level-fill", "Kenshi_ProgressBarFilledSkin", bar with { Width = bar.Width * progress });
        Progress("stat-next-level-frame", "Kenshi_ProgressBarSkin", bar);
        var barText = new UiRect(bar.X + 10 * scale, bar.Y - 4 * scale, bar.Width - 20 * scale, row);
        Text("stat-next-level-label", barText, level >= 100 ? "Mastered" : "Next level", MainColour, 14, rightClip);
        Text("stat-next-level-percent", barText, ((int)(progress * 100)).ToString(CultureInfo.InvariantCulture) + "%",
            MainColour, 14, rightClip, "Right Top");
        Text("stat-description-heading", new(bar.X + 10 * scale, bar.Y + 12 * scale, bar.Width - 20 * scale, row),
            "Description", MainColour, 14, rightClip);

        string chance = BrutalityPolicy.BisectionChance(level).ToString("P0", CultureInfo.InvariantCulture);
        string description = Loc.Tr("Experience in brutal melee combat teaches precise finishing cuts.") + "\n\n"
            + Loc.Tr("Body cleaves require a lethal, powerful slash to the chest or stomach after armour. The weapon and attack direction must allow it.")
            + "\n\n" + Loc.Tr("Current cleave chance on eligible hits:") + " " + chance
            + "\n" + Loc.Tr("At level 100: 95%.");
        Text("stat-description-body", new(bar.X + 10 * scale, bar.Y + 28 * scale, bar.Width - 20 * scale,
            Math.Max(0, right.Bounds.Height - 36 * scale)), description, ink, 14, rightClip, wrap: true);

        void Line(string name, string caption, string colour, int pixels = 14)
        {
            Text(name, new(left.Bounds.X + 19 * scale, y, left.Bounds.Width - 27 * scale, row),
                caption, colour, pixels, leftClip);
            y += row;
        }

        void Progress(string name, string skin, UiRect bounds) => controls.Add(new(name, "Widget", bounds, "", "", skin, true,
            new Dictionary<string, string>(), Vector4.Zero, Pickable: false, Clip: rightClip));

        void Text(string name, UiRect bounds, string caption, string colour, int pixels, UiRect clip,
            string align = "Left Top", bool wrap = false) => controls.Add(new(name, "TextBox", bounds, Loc.Tr(caption), "", "", true,
            new Dictionary<string, string>
            {
                ["FontPixels"] = Math.Max(8, (int)MathF.Round(pixels * scale)).ToString(CultureInfo.InvariantCulture),
                ["TextColour"] = colour, ["TextAlign"] = align, ["WordWrap"] = wrap ? "true" : "false",
            }, Vector4.Zero, Pickable: false, Clip: clip));
    }
}
