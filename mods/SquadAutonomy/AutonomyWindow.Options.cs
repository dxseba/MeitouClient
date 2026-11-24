using System.Globalization;
using Meitou.Game.Ui;
using Meitou.Simulation.Ai;

namespace Meitou.SquadAutonomy;

/// <summary>
/// The window's option categories (the original's Category list: Basic, Labour, Combat, Guard; Labour only for a plan with the labour goal) and
/// the AUT button in each squad's box of the squad screen (<c>ShowInSquad</c>).
/// </summary>
sealed partial class AutonomyWindow
{
    /// <summary>
    /// The original's option categories.
    /// </summary>
    enum Category { Basic, Labour, Combat, Guard }

    /// <summary>
    /// The squad box button's place, as shares of the box (the original puts it between the name box and the size text, 0.05 of the box in from
    /// each; <b>Verified</b> its source; the two widgets' places from the game's <c>Kenshi_SquadBox.layout</c>: name box 0.0107 + 0.3571 wide,
    /// top 0.0585, 0.1854 high; size text from 0.7060).
    /// </summary>
    const float CellLeft = 0.3678f + 0.05f, CellWidth = 0.7060f - 0.3678f - 0.1f, CellTop = 0.0585f, CellHeight = 0.1854f;

    /// <summary>
    /// The labour categories as the original's window names them, in its order.
    /// </summary>
    static readonly (LabourKind Kind, string Text)[] LabourRows =
    [
        (LabourKind.Science, "Science"), (LabourKind.Labouring, "Labouring (Mining)"), (LabourKind.Farming, "Farming"),
        (LabourKind.Cooking, "Cooking"), (LabourKind.Medic, "Medic"), (LabourKind.Engineer, "Engineer"), (LabourKind.Robotics, "Robotics"),
        (LabourKind.WeaponSmith, "Weapon Smith"), (LabourKind.ArmourSmith, "Armour Smith"), (LabourKind.CrossbowSmith, "Crossbow Smith"),
        (LabourKind.AutomaticMachine, "Automatic Machine"), (LabourKind.Other, "Other"),
    ];

    /// <summary>
    /// The labour sliders per row.
    /// </summary>
    const int LabourColumns = 3;

    /// <summary>
    /// The shown category.
    /// </summary>
    Category _category;

    /// <summary>
    /// The squad box button's action.
    /// </summary>
    string _squadAction = "";

    /// <summary>
    /// The squad a squad box button asked the window to show; null when none is waiting.
    /// </summary>
    int? _requested;

    /// <summary>
    /// The original's Set All value.
    /// </summary>
    int _setAll = 1;

    #region Squad screen

    /// <summary>
    /// The AUT button in a squad's box of the squad screen (<c>ShowInSquad</c>): opens the window on that squad.
    /// </summary>
    UiNode? SquadButton(UiScreenContext c)
    {
        if (!SquadAutonomyMod.Settings.ShowInSquad || c.Squad is not { } squad) return null;

        var box = c.Anchor;
        return new UiNode("aut-squad-" + squad, UiNodeKind.Button)
        {
            Placement = new(box.Width * CellWidth, box.Height * CellHeight, Left: box.Width * CellLeft, Top: box.Height * CellTop),
            Text = "AUT",
            Style = _autStyle,
            Action = _squadAction,
            Argument = squad.ToString(CultureInfo.InvariantCulture),
        };
    }

    void OnSquadButton(UiActionContext c)
    {
        if (!int.TryParse(c.Argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out int squad)) return;

        (_requested, _open, _pick) = (squad, true, Pick.None);
    }

    /// <summary>
    /// Shows the squad a squad box button asked for, once.
    /// </summary>
    void ShowRequested(AutonomyView view)
    {
        if (_requested is not { } id) return;

        _requested = null;
        int index = view.Squads.ToList().FindIndex(s => s.Id == id);
        if (index >= 0) _squad = index;
    }

    #endregion

    #region Categories

    /// <summary>
    /// Category: Basic, Labour (only with the labour goal), Combat, Guard.
    /// </summary>
    UiNode CategoryLine(SquadRow squad)
    {
        if (_category == Category.Labour && !squad.Labours) _category = Category.Basic;

        var row = Row("aut-category");
        row.Children.Add(Label("aut-category-l", "Category", 100, _styles.Dim));
        foreach (var category in Enum.GetValues<Category>())
        {
            bool enabled = category != Category.Labour || squad.Labours;
            row.Children.Add(Button("category." + category, category.ToString(), 110, () => _category = category, enabled, _category == category));
        }

        return row;
    }

    /// <summary>
    /// The shown category's options.
    /// </summary>
    IEnumerable<UiNode> CategoryLines(SquadRow squad, Plan plan, AutonomyView view) => _category switch
    {
        Category.Labour => LabourLines(squad, plan),
        Category.Combat => [CombatLine(squad, plan)],
        Category.Guard => [GuardLine(squad, plan)],
        _ => BasicLines(squad, plan, view),
    };

    /// <summary>
    /// Basic: the work hours, this port's off-work package, the rest rules and Allow Using Paid Beds.
    /// </summary>
    List<UiNode> BasicLines(SquadRow squad, Plan plan, AutonomyView view) =>
    [
        HourLine(squad, plan, "start", "Start Work Time", plan.StartHour, h => plan with { StartHour = h }, $"{view.Hour:00}:00"),
        HourLine(squad, plan, "end", "End Work Time", plan.EndHour, h => plan with { EndHour = h }, ""),
        RestLine(plan),
        RecoveryLine(squad, plan),
        ThresholdLine(squad, plan),
        PaidBedsLine(squad, plan),
    ];

    /// <summary>
    /// The original's Allow Using Paid Beds.
    /// </summary>
    UiNode PaidBedsLine(SquadRow squad, Plan plan)
    {
        var rules = plan.Rules;
        var row = Row("aut-paid");
        row.Children.Add(Button("rest.paid", "Allow Using Paid Beds", 230,
            () => Save(squad, plan with { Resting = rules with { PaidBeds = !rules.PaidBeds } }), selected: rules.PaidBeds));
        return row;
    }

    /// <summary>
    /// Guard: Gate Guard: Stay Inside Gate and Close Gate.
    /// </summary>
    UiNode GuardLine(SquadRow squad, Plan plan)
    {
        var row = Row("aut-guard");
        row.Children.Add(Label("aut-guard-l", "Gate Guard:", 110, _styles.Dim));
        row.Children.Add(GateButton(squad, plan, SquadConduct.GateInside, "Stay Inside Gate"));
        row.Children.Add(GateButton(squad, plan, SquadConduct.CloseDoors, "Close Gate"));
        return row;
    }

    UiNode GateButton(SquadRow squad, Plan plan, SquadConduct flag, string text) =>
        Button("gate." + flag, text, 160, () => Save(squad, plan with { Gate = plan.Gate ^ flag }), selected: (plan.Gate & flag) != 0);

    #endregion

    #region Labour

    /// <summary>
    /// Labour: the note, a slider (0 to 10) per category and Set All.
    /// </summary>
    List<UiNode> LabourLines(SquadRow squad, Plan plan)
    {
        string note = context.Tr("Stats Used:") + " " + context.Tr("(bigger number = higher priority, 0 = forbid)");
        var lines = new List<UiNode> { Text("aut-labour-note", note, _styles.Dim) };
        for (int i = 0; i < LabourRows.Length; i += LabourColumns)
        {
            var row = Row("aut-labour-" + i);
            foreach (var (kind, text) in LabourRows.Skip(i).Take(LabourColumns)) AddLabour(row, squad, plan, kind, text);
            lines.Add(row);
        }

        lines.Add(SetAllLine(squad, plan));
        return lines;
    }

    /// <summary>
    /// One category's slider as - and + buttons.
    /// </summary>
    void AddLabour(UiNode row, SquadRow squad, Plan plan, LabourKind kind, string text)
    {
        var labour = plan.Labour;
        int value = labour.Of(kind);
        row.Children.Add(Label($"aut-lab-{kind}-l", text, 110, _styles.Dim));
        row.Children.Add(Button($"lab.{kind}.down", "-", 22, () => Save(squad, plan with { Labouring = labour.With(kind, value - 1) }), value > 0));
        row.Children.Add(Label($"aut-lab-{kind}-v", value.ToString(CultureInfo.InvariantCulture), 26, _styles.Text));
        row.Children.Add(Button($"lab.{kind}.up", "+", 22, () => Save(squad, plan with { Labouring = labour.With(kind, value + 1) }),
            value < LabourPriorities.Max));
    }

    /// <summary>
    /// The original's Set All slider and its Set button.
    /// </summary>
    UiNode SetAllLine(SquadRow squad, Plan plan)
    {
        var row = Row("aut-labour-all");
        row.Children.Add(Label("aut-lab-all-l", "Set All", 110, _styles.Dim));
        row.Children.Add(Button("lab.all.down", "-", 22, () => _setAll = Math.Max(0, _setAll - 1), _setAll > 0));
        row.Children.Add(Label("aut-lab-all-v", _setAll.ToString(CultureInfo.InvariantCulture), 26, _styles.Text));
        row.Children.Add(Button("lab.all.up", "+", 22, () => _setAll = Math.Min(LabourPriorities.Max, _setAll + 1), _setAll < LabourPriorities.Max));
        row.Children.Add(Button("lab.all.set", "Set", 60, () => Save(squad, plan with { Labouring = LabourPriorities.All(_setAll) })));
        return row;
    }

    #endregion
}
