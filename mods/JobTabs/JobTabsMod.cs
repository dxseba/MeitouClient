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
        context.AddUi(new TabButtons());
        context.Log("Job Tabs initialized");
    }
}

public sealed class JobTabsSettings
{
    /// <summary>The most tabs the jobs list has room for (the engine's limit of job panel buttons across all mods).</summary>
    public const int Most = 4;

    [Setting("Tabs", Hint = "How many job tabs each character has (restart to change).", Min = 2, Max = Most)]
    public int Count { get; set; } = 4;
}

/// <summary>Switches the selected player characters to job tab <paramref name="Tab"/> (0 based).</summary>
public sealed record SwitchTabOrder(int Tab) : SimCommand;

/// <summary>
/// One button per tab beside the jobs list (<see cref="UiRegistration.AddJobPanelButtons"/>) for a selected player character: the active tab's
/// caption is bracketed, and each tooltip lists that tab's jobs.
/// </summary>
sealed class TabButtons : IUiExtension
{
    const int ShownJobs = 8;

    public string Id => "dxseba.jobtabs.ui";

    public void Register(UiRegistration registration) => registration.AddJobPanelButtons(Buttons);

    static IEnumerable<UiJobButton> Buttons(UiCharacterContext context)
    {
        if (JobTabsMod.Running?.View(context.Id) is not { } view) return [];

        int count = Math.Min(JobTabsMod.Settings.Count, JobTabsSettings.Most);
        return Enumerable.Range(0, count).Select(tab => Button(context, view, tab));
    }

    static UiJobButton Button(UiCharacterContext context, TabView view, int tab)
    {
        string n = (tab + 1).ToString(CultureInfo.InvariantCulture);
        bool active = tab == view.Active;
        string caption = active ? $"[ {n} ]" : n;
        return new(caption, Tooltip(view, tab, n), () => context.Enqueue(new SwitchTabOrder(tab) { Tick = context.Tick }));
    }

    /// <summary>The original's tooltip: "Tab 2: 3 jobs" and up to eight job names ("..." for more), or the hint for an empty tab.</summary>
    static string Tooltip(TabView view, int tab, string n)
    {
        var jobs = tab < view.Jobs.Length ? view.Jobs[tab] : [];
        if (jobs.Length == 0) return "Empty: hold Shift while giving an order to add jobs";

        var text = new System.Text.StringBuilder($"Tab {n}: {jobs.Length} {(jobs.Length == 1 ? "job" : "jobs")}");
        foreach (string job in jobs.Take(ShownJobs)) text.Append('\n').Append(job);
        if (jobs.Length > ShownJobs) text.Append("\n...");
        return text.ToString();
    }
}
