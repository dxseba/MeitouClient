using System.Globalization;
using Meitou.Game.Ui;
using Meitou.Modding;

namespace Meitou.WorkForCats;

/// <summary>
/// The separate employment window (the original's, on by default): while a workplace is inspected, its employer, the selected character's skill
/// and hourly rate, and Get a job / Quit job. The view comes from the running system (<see cref="WorkSystem.View"/>); a press queues a command.
/// </summary>
internal sealed class EmploymentWindow(ModContext context) : IUiExtension
{
    const float Width = 300, Line = 20, Left = 16, Top = 120;

    public string Id => "dxseba.workforcats.ui";

    public void Register(UiRegistration registration)
    {
        var styles = new Styles(
            registration.RegisterStyle("panel", new() { Background = new(.10f, .085f, .07f, .94f), Text = new(.91f, .86f, .74f, 1) }),
            registration.RegisterStyle("text", new() { Text = new(.91f, .86f, .74f, 1), TextPadding = new(6, 1, 6, 1) }),
            registration.RegisterStyle("button", new()
            {
                Background = new(.20f, .15f, .11f, .95f),
                Hover = new(.32f, .22f, .16f, 1),
                Disabled = new(.14f, .12f, .10f, .8f),
                TextPadding = new(6, 2, 6, 2),
            }));
        string hire = registration.RegisterAction("hire", _ => Hire());
        string quit = registration.RegisterAction("quit", _ => Quit());
        registration.Contribute(UiSlots.Overlay, _ => Build(styles, hire, quit), order: 55);
    }

    readonly record struct Styles(string Panel, string Text, string Button);

    static EmploymentView? Shown =>
        WorkForCatsMod.Settings.ShowEmploymentWindow && WorkForCatsMod.Running?.View is { } view && (view.Refusal is null || view.EmployedHere)
            ? view : null;

    UiNode Build(Styles styles, string hire, string quit)
    {
        if (Shown is not { } view) return new UiNode("workforcats-hidden") { Visible = false };

        var lines = Lines(view);
        var root = new UiNode("workforcats") { Placement = UiPlacement.Fill, Pickable = false };
        var body = new UiNode("workforcats-window")
        {
            Placement = new(Width, 12 + (lines.Count + 1) * (Line + 1) + 6, Left: Left, Top: Top),
            Style = styles.Panel,
            Flow = UiFlow.Column,
            Padding = 4,
            Gap = 1,
        };

        for (int i = 0; i < lines.Count; i++) body.Children.Add(Label($"workforcats-line-{i}", lines[i], styles.Text));
        body.Children.Add(Button(view, styles.Button, hire, quit));
        root.Children.Add(body);
        return root;
    }

    /// <summary>
    /// The original's window: the building's name (its caption) and the rate line, "N cats/hour" (whole cats, the relation bonus in) and, when
    /// employed here, the minutes of the hour worked; nothing without a selected character.
    /// </summary>
    static List<string> Lines(EmploymentView view)
    {
        var lines = new List<string> { view.Building };
        if (view.Refusal is { } refusal) lines.Add(refusal);
        if (view.WorkerName is null) return lines;

        string rate = view.Rate.ToString("0", CultureInfo.InvariantCulture);
        lines.Add($"{rate} cats/hour{view.Status}");
        return lines;
    }

    static UiNode Label(string id, string text, string style) =>
        new(id, UiNodeKind.Label) { Placement = new(UiLength.Fill, Line), Text = text, Style = style };

    static UiNode Button(EmploymentView view, string style, string hire, string quit) =>
        new("workforcats-button", UiNodeKind.Button)
        {
            Placement = new(UiLength.Fill, Line + 4),
            Text = view.EmployedHere ? "Quit job" : "Get a job",
            Action = view.EmployedHere ? quit : hire,
            Enabled = view.WorkerName is not null && (view.EmployedHere || view.Refusal is null),
            Style = style,
        };

    void Hire()
    {
        if (WorkForCatsMod.Running?.View is { WorkerName: not null, Refusal: null } view)
            context.Enqueue(new HireOrder(view.Worker, view.Site) { Tick = context.Tick });
    }

    void Quit()
    {
        if (WorkForCatsMod.Running?.View is { WorkerName: not null, EmployedHere: true } view)
            context.Enqueue(new QuitOrder(view.Worker) { Tick = context.Tick });
    }
}
