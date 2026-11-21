using System.Globalization;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.JobTabs;

/// <summary>
/// Port of Job Tabs (jacobericson/JobTabs, workshop 3813790518; rebuilt from its description): numbered job tabs, so each character keeps several job
/// lists (farming, crafting, defending...); a tab button switches every selected character to that list, the previous one kept.
/// </summary>
public sealed class JobTabsMod : IMod
{
    internal static readonly JobTabsSettings Settings = new();

    internal static volatile JobTabsSystem? Running;

    public void Initialize(ModContext context)
    {
        context.AddSettings(Settings);
        context.AddSystem("jobtabs", _ => Running = new JobTabsSystem(Settings), SystemAnchor.End);
        context.AddUi(new TabButtons(context));
        context.Log("Job Tabs initialized");
    }
}

public sealed class JobTabsSettings
{
    [Setting("Tabs", Hint = "How many job tabs each character has (restart to change).", Min = 2, Max = 9)]
    public int Count { get; set; } = 4;

    [Setting("Buttons X", Hint = "Left of the tab buttons, in pixels.", Min = 0, Max = 3000)]
    public int X { get; set; } = 16;

    [Setting("Buttons from bottom", Hint = "Height of the tab buttons above the screen's bottom, in pixels.", Min = 0, Max = 2000)]
    public int FromBottom { get; set; } = 210;
}

/// <summary>Switches the selected player characters to job tab <paramref name="Tab"/> (0 based).</summary>
public sealed record SwitchTabOrder(int Tab) : SimCommand;

/// <summary>"Jobs" and one button per tab while player characters are selected; the first selected one's active tab is lit.</summary>
sealed class TabButtons(ModContext context) : IUiExtension
{
    const float Size = 26, Gap = 3, LabelWidth = 46;

    string _panel = "", _button = "", _lit = "", _click = "";

    public string Id => "dxseba.jobtabs.ui";

    public void Register(UiRegistration registration)
    {
        var ink = new System.Numerics.Vector4(.91f, .86f, .74f, 1);
        _panel = registration.RegisterStyle("panel", new() { Background = new(.10f, .085f, .07f, .9f), Text = ink, TextPadding = new(6, 1, 6, 1) });
        _button = registration.RegisterStyle("button", new()
        {
            Background = new(.20f, .15f, .11f, .95f), Hover = new(.32f, .22f, .16f, 1), Text = ink,
        });
        _lit = registration.RegisterStyle("lit", new() { Background = new(.45f, .32f, .14f, 1), Hover = new(.55f, .40f, .18f, 1), Text = ink });
        _click = registration.RegisterAction("tab", c => Click(c.Argument));
        registration.Contribute(UiSlots.Overlay, Build, order: 39);
    }

    UiNode Build(UiFrameContext frame)
    {
        if (JobTabsMod.Running is not { Active: >= 0 and var active }) return new UiNode("jobtabs-hidden") { Visible = false };

        int count = JobTabsMod.Settings.Count;
        float width = LabelWidth + count * (Size + Gap) + 6;
        var row = new UiNode("jobtabs")
        {
            Placement = new(width, Size + 6, Left: JobTabsMod.Settings.X, Top: frame.Viewport.Height - JobTabsMod.Settings.FromBottom),
            Style = _panel, Flow = UiFlow.Row, Gap = Gap, Padding = 3,
        };
        row.Children.Add(new UiNode("jobtabs-label", UiNodeKind.Label) { Placement = new(LabelWidth, Size), Text = "Jobs", Style = _panel });
        for (int i = 0; i < count; i++)
        {
            string n = (i + 1).ToString(CultureInfo.InvariantCulture);
            row.Children.Add(new UiNode("jobtabs-" + n, UiNodeKind.Button)
            {
                Placement = new(Size, Size), Text = n, Style = i == active ? _lit : _button, Action = _click,
                Argument = i.ToString(CultureInfo.InvariantCulture),
            });
        }

        var root = new UiNode("jobtabs-root") { Placement = UiPlacement.Fill, Pickable = false };
        root.Children.Add(row);
        return root;
    }

    void Click(string argument)
    {
        if (int.TryParse(argument, CultureInfo.InvariantCulture, out int tab)) context.Enqueue(new SwitchTabOrder(tab) { Tick = context.Tick });
    }
}
