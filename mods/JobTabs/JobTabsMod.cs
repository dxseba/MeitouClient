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
        return new(caption, Tooltip(view, tab, n, active), () => context.Enqueue(new SwitchTabOrder(tab) { Tick = context.Tick }));
    }

    static string Tooltip(TabView view, int tab, string n, bool active)
    {
        var jobs = tab < view.Jobs.Length ? view.Jobs[tab] : [];
        string head = active ? $"Job tab {n} (active)" : $"Job tab {n}: switch the selected characters to it";
        return jobs.Length == 0 ? head + "\nNo jobs" : head + "\n" + string.Join("\n", jobs);
    }
}
