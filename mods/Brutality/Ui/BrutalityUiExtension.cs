using Meitou.Game.Ui;
using System.Numerics;
using System.Text.Json;

namespace Meitou.Brutality.Ui;

/// <summary>Presentation-only preferences; changing them never changes combat or progression.</summary>
public sealed class BrutalityUiPreferences
{
    sealed record Saved(bool ShowPartLabels = true, bool ShowSkill = true);
    readonly string? path;
    public bool ShowPartLabels { get; private set; } = true;
    public bool ShowSkill { get; private set; } = true;
    public BrutalityUiPreferences(string? folder = null)
    {
        if (folder is null) return;
        path = Path.Combine(folder, "brutality-ui.json");
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length <= 65536
                && JsonSerializer.Deserialize<Saved>(File.ReadAllText(path)) is { } saved)
                (ShowPartLabels, ShowSkill) = (saved.ShowPartLabels, saved.ShowSkill);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { Console.Error.WriteLine($"Brutality preferences: {error.Message}"); }
    }
    public void ToggleLabels() { ShowPartLabels = !ShowPartLabels; Save(); }
    public void ToggleSkill() { ShowSkill = !ShowSkill; Save(); }
    void Save()
    {
        if (path is null) return;
        try { File.WriteAllText(path, JsonSerializer.Serialize(new Saved(ShowPartLabels, ShowSkill), new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { Console.Error.WriteLine($"Brutality preferences: {error.Message}"); }
    }
}

/// <summary>Native UI contribution using read-only bindings supplied by the host adapter.</summary>
public sealed class BrutalityUiExtension(BrutalityUiPreferences? preferences = null) : IUiExtension
{
    public const string ExtensionId = "dxseba.brutality.ui";
    public string Id => ExtensionId;
    readonly BrutalityUiPreferences options = preferences ?? BrutalityUiAdapter.Preferences;
    bool open;

    public void Register(UiRegistration registration)
    {
        string panel = registration.RegisterStyle("panel", new() { Background = new(.10f, .085f, .07f, .96f), Text = new(.91f, .86f, .74f, 1) });
        string text = registration.RegisterStyle("text", new() { Text = new(.91f, .86f, .74f, 1), TextPadding = new(6, 2, 6, 2) });
        string button = registration.RegisterStyle("button", new() { Background = new(.20f, .15f, .11f, .95f), Hover = new(.32f, .22f, .16f, 1), TextPadding = new(6, 2, 6, 2) });
        string toggle = registration.RegisterAction("toggle", _ => open = !open);
        string labels = registration.RegisterAction("labels", _ => options.ToggleLabels());
        string skill = registration.RegisterAction("skill", _ => options.ToggleSkill());
        registration.Contribute(UiSlots.Overlay, frame =>
        {
            if (frame.Text("brutality.available") != "1") return new("brutality-hidden") { Visible = false };
            float width = Math.Min(350, frame.Viewport.Width - 20);
            var root = new UiNode("brutality-ui") { Placement = UiPlacement.Fill, Pickable = false };
            string caption = options.ShowSkill ? frame.Text("brutality.caption", "Brutality") : "Gore settings";
            root.Children.Add(new("brutality-toggle", UiNodeKind.Button)
            { Placement = new(width, 28, Right: 12, Top: 44), Text = caption, Action = toggle, Style = button });
            if (!open) return root;
            var body = new UiNode("brutality-details") { Placement = new(width, 294, Right: 12, Top: 76), Style = panel, Flow = UiFlow.Column, Padding = 6, Gap = 3 };
            void Line(string name, string caption, float height = 24) => body.Children.Add(new(name, UiNodeKind.Label)
            { Placement = new(UiLength.Fill, height), Text = caption, Style = text });
            Line("brutality-heading", "Brutality");
            Line("brutality-level", frame.Text("brutality.progress", "Select a character to see their training."));
            Line("brutality-unlock20", "20: Scalp wounds");
            Line("brutality-unlock40", "40: Major head wounds and decapitation");
            Line("brutality-unlock60", "60: Body cleaves");
            Line("brutality-chance", frame.Text("brutality.chance"));
            Line("brutality-training", "Train against capable enemies in melee.");
            Line("brutality-helpless", "Helpless targets grant no experience.");
            body.Children.Add(new("brutality-labels", UiNodeKind.Button) { Placement = new(UiLength.Fill, 28), Text = "Body-part names with Alt: " + (options.ShowPartLabels ? "On" : "Off"), Action = labels, Style = button });
            body.Children.Add(new("brutality-skill", UiNodeKind.Button) { Placement = new(UiLength.Fill, 28), Text = "Show skill on HUD: " + (options.ShowSkill ? "On" : "Off"), Action = skill, Style = button });
            root.Children.Add(body);
            return root;
        }, order: 30);
    }
}
