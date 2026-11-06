using System.Globalization;
using System.Numerics;
using Meitou.Data.Ui;

namespace Meitou.Brutality.Ui;

public static partial class BrutalityUiAdapter
{
    static void StatsContent(object __instance, List<UiControl> controls)
    {
        if (controls.FirstOrDefault(c => c.Name == "Skills2Panel") is not { } panel) return;
        float scale = scaleProperty?.GetValue(__instance) is float factor ? factor : 1, row = 20 * scale;
        var heading = controls.FirstOrDefault(c => c.Name == "Skills2Panel-heading-4");
        float y = heading?.Bounds.Y + 2 * scale ?? panel.Bounds.Y + 5 * row;
        for (int i = 0; i < controls.Count; i++)
            if (controls[i].Name.StartsWith("Skills2Panel-", StringComparison.Ordinal) && controls[i].Bounds.Y >= y - 2 * scale)
                controls[i] = controls[i] with { Bounds = controls[i].Bounds with { Y = controls[i].Bounds.Y + row } };
        float level = Session(__instance) is { } session && SelectedLead(__instance) is { } lead ? Actor(session, lead.Id)?.Level ?? 0 : 0;
        var clip = panel.Bounds.Intersect(panel.Clip ?? panel.Bounds);
        var properties = new Dictionary<string, string> { ["FontName"] = "Kenshi_StandardFont_Medium", ["TextColour"] = "0.75 0.25 0.18", ["TextAlign"] = "Left VCenter" };
        controls.Add(new("brutality-stat-label", "Button", new(panel.Bounds.X, y, Math.Max(0, panel.Bounds.Width - 36 * scale), row), "Brutality", "stat:brutality", "", true, properties, Vector4.Zero, Pickable: true, Clip: clip));
        controls.Add(new("brutality-stat-value", "Button", new(panel.Bounds.X + panel.Bounds.Width - 34 * scale, y, 34 * scale, row), MathF.Floor(level).ToString("0", CultureInfo.InvariantCulture), "stat:brutality", "", true,
            new Dictionary<string, string>(properties) { ["TextAlign"] = "Right VCenter" }, Vector4.Zero, Pickable: true, Clip: clip));
    }

    static bool StatDescription(object __instance, List<UiControl> controls)
    {
        if (statField?.GetValue(__instance) as string != "brutality") return true;
        if (controls.FirstOrDefault(c => c.Name == "Description1Panel") is not { } left || controls.FirstOrDefault(c => c.Name == "Description2Panel") is not { } right) return false;
        float scale = scaleProperty?.GetValue(__instance) is float factor ? factor : 1;
        float row = Math.Min(20 * scale, Math.Max(8, (left.Bounds.Height - 4 * scale) / 9));
        var actor = Session(__instance) is { } session && SelectedLead(__instance) is { } lead ? Actor(session, lead.Id) : null;
        float level = actor?.Level ?? 0, progress = actor?.Progress ?? 0;
        string[] lines = ["Brutality", "Combat effects", "20: Scalp wounds", "40: Major head wounds, decapitation", "60: Body cleaves", "Training", "Fight capable enemies in melee", "Stronger opponents improve experience", "Helpless targets grant no experience"];
        for (int i = 0; i < lines.Length; i++)
            Text($"brutality-help-{i}", new(left.Bounds.X + 16 * scale, left.Bounds.Y + 4 * scale + row * i, left.Bounds.Width - 24 * scale, row), lines[i], left.Bounds, i == 0 ? 18 : 14);
        var bar = new UiRect(right.Bounds.X + 17 * scale, right.Bounds.Y + 8 * scale, Math.Max(0, right.Bounds.Width - 30 * scale), 12 * scale);
        controls.Add(new("brutality-progress-empty", "Widget", bar, "", "", "Kenshi_ProgressBarEmptySkin", true, new Dictionary<string, string>(), Vector4.Zero, Pickable: false));
        if (progress > 0) controls.Add(new("brutality-progress-fill", "Widget", bar with { Width = bar.Width * progress }, "", "", "Kenshi_ProgressBarFilledSkin", true, new Dictionary<string, string>(), Vector4.Zero, Pickable: false));
        Text("brutality-progress-caption", new(bar.X, bar.Y - 4 * scale, bar.Width, row), level >= 100 ? "Mastered" : $"Next level: {(progress * 100).ToString("0", CultureInfo.InvariantCulture)}%", right.Bounds);
        string description = "Experience in brutal melee combat teaches precise finishing cuts.\n\nBody cleaves require a lethal, powerful slash to the chest or stomach after armour. The weapon and attack direction must allow it.\n\nCurrent cleave chance on eligible hits: "
            + BrutalityPolicy.BisectionChance(level).ToString("P0", CultureInfo.InvariantCulture) + ".\nAt level 100: 95%.";
        Text("brutality-description", new(bar.X, bar.Y + 28 * scale, bar.Width, Math.Max(0, right.Bounds.Height - 38 * scale)), description, right.Bounds);
        return false;
        void Text(string name, UiRect bounds, string caption, UiRect clip, int pixels = 14) => controls.Add(new(name, "TextBox", bounds, caption, "", "", true,
            new Dictionary<string, string> { ["FontPixels"] = Math.Max(8, (int)MathF.Round(pixels * scale)).ToString(CultureInfo.InvariantCulture), ["TextColour"] = "0.48 0.40 0.32", ["TextAlign"] = "Left Top", ["WordWrap"] = "true" },
            Vector4.Zero, Pickable: false, Clip: clip));
    }
}
