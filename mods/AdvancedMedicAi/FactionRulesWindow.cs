using Meitou.Game.Ui;
using Meitou.Modding;

namespace Meitou.AdvancedMedicAi;

/// <summary>
/// The original's faction rules window ("Advanced Medic AI: faction rules", opened by the settings' Faction rules... button): one line per faction
/// of the game data's diplomacy, by name, with a button cycling Use general rule, Always heal, Never heal; saved rules of factions the data lacks
/// are listed after "Saved rules for factions not in this game:"; Close. A change is kept in the hidden <see cref="MedicSettings.FactionRules"/>
/// setting (<c>meitou.user.json</c>, as the original's <c>AdvancedMedicAI_TargetFilters.ini</c>). Interface thread only.
/// </summary>
internal sealed class FactionRulesWindow(ModContext context, ModSetting rulesSetting) : IUiExtension
{
    const float Width = 460, Line = 22, PageLines = 16;

    static volatile bool s_open;
    int _page;

    public string Id => "dxseba.advancedmedicai.rules";

    /// <summary>
    /// The settings button: shows the window.
    /// </summary>
    public static void Open() => s_open = true;

    public void Register(UiRegistration registration)
    {
        var styles = new Styles(
            registration.RegisterStyle("panel", new() { Background = new(.10f, .085f, .07f, .95f), Text = new(.91f, .86f, .74f, 1) }),
            registration.RegisterStyle("text", new() { Text = new(.91f, .86f, .74f, 1), TextPadding = new(6, 1, 6, 1) }),
            registration.RegisterStyle("button", new()
            {
                Background = new(.20f, .15f, .11f, .95f),
                Hover = new(.32f, .22f, .16f, 1),
                Disabled = new(.14f, .12f, .10f, .8f),
                TextPadding = new(6, 2, 6, 2),
            }));
        var actions = new Actions(
            registration.RegisterAction("cycle", a => Cycle(a.Argument)),
            registration.RegisterAction("page", a => Turn(a.Argument)),
            registration.RegisterAction("close", _ => s_open = false));
        registration.Contribute(UiSlots.Overlay, _ => Build(styles, actions), order: 60);
    }

    readonly record struct Styles(string Panel, string Text, string Button);

    readonly record struct Actions(string Cycle, string Page, string Close);

    /// <summary>
    /// A line of the window: a faction's id and name (null id: a heading).
    /// </summary>
    readonly record struct Row(string? Id, string Name);

    UiNode Build(Styles styles, Actions actions)
    {
        if (!s_open) return new UiNode("medicai-rules-hidden") { Visible = false };

        var rows = Rows();
        int pages = Math.Max(1, (int)Math.Ceiling(rows.Count / PageLines));
        _page = Math.Clamp(_page, 0, pages - 1);

        var body = new UiNode("medicai-rules")
        {
            Placement = new(Width, 12 + (PageLines + 3) * (Line + 1), Left: 120, Top: 100),
            Style = styles.Panel,
            Flow = UiFlow.Column,
            Padding = 4,
            Gap = 1,
        };

        body.Children.Add(Label("medicai-rules-title", context.Tr("Advanced Medic AI: faction rules"), styles.Text));
        AddLines(body, rows, styles, actions);
        body.Children.Add(Footer(styles, actions, pages));
        var root = new UiNode("medicai-rules-root") { Placement = UiPlacement.Fill, Pickable = false };
        root.Children.Add(body);
        return root;
    }

    void AddLines(UiNode body, List<Row> rows, Styles styles, Actions actions)
    {
        var rules = FactionRuleText.Parse(rulesSetting.Get() as string ?? "");
        int first = _page * (int)PageLines;
        for (int i = first; i < Math.Min(rows.Count, first + (int)PageLines); i++)
        {
            var row = rows[i];
            body.Children.Add(row.Id is null ? Label($"medicai-rules-h{i}", row.Name, styles.Text) : FactionLine(i, row, rules, styles, actions));
        }
    }

    UiNode FactionLine(int i, Row row, Dictionary<string, FactionRule> rules, Styles styles, Actions actions)
    {
        var line = new UiNode($"medicai-rules-l{i}") { Placement = new(UiLength.Fill, Line), Flow = UiFlow.Row, Gap = 4 };
        line.Children.Add(new UiNode($"medicai-rules-n{i}", UiNodeKind.Label)
        {
            Placement = new(Width - 190, Line),
            Text = row.Name,
            Style = styles.Text,
        });
        line.Children.Add(new UiNode($"medicai-rules-b{i}", UiNodeKind.Button)
        {
            Placement = new(170, Line),
            Text = context.Tr(Caption(rules.GetValueOrDefault(row.Id!))),
            Action = actions.Cycle,
            Argument = row.Id!,
            Style = styles.Button,
        });
        return line;
    }

    UiNode Footer(Styles styles, Actions actions, int pages)
    {
        var line = new UiNode("medicai-rules-footer") { Placement = new(UiLength.Fill, Line + 4), Flow = UiFlow.Row, Gap = 4 };
        line.Children.Add(Button("medicai-rules-prev", "<", actions.Page, "-1", styles.Button, 40, _page > 0));
        line.Children.Add(Label("medicai-rules-page", $"{_page + 1} / {pages}", styles.Text, 80));
        line.Children.Add(Button("medicai-rules-next", ">", actions.Page, "1", styles.Button, 40, _page < pages - 1));
        line.Children.Add(Button("medicai-rules-close", context.Tr("Close"), actions.Close, "", styles.Button, 120, true));
        return line;
    }

    /// <summary>
    /// The factions of the data by shown name (the player's own left out), then a heading and the saved rules of ids the data lacks.
    /// </summary>
    List<Row> Rows()
    {
        var relations = context.Population.Relations;
        var known = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<Row>();
        for (int f = 0; f < relations.Count; f++)
        {
            var faction = relations.Factions[f];
            if (f == relations.PlayerFaction || !known.Add(faction.Id)) continue;

            rows.Add(new Row(faction.Id, faction.Name));
        }

        rows.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        AddMissing(rows, known);
        return rows;
    }

    void AddMissing(List<Row> rows, HashSet<string> known)
    {
        var missing = FactionRuleText.Parse(rulesSetting.Get() as string ?? "").Keys.Where(id => !known.Contains(id)).Order(StringComparer.Ordinal)
            .ToList();
        if (missing.Count == 0) return;

        rows.Add(new Row(null, context.Tr("Saved rules for factions not in this game:")));
        rows.AddRange(missing.Select(id => new Row(id, id)));
    }

    static string Caption(FactionRule rule) => rule switch
    {
        FactionRule.Always => "Always heal",
        FactionRule.Never => "Never heal",
        _ => "Use general rule",
    };

    void Cycle(string id)
    {
        if (id.Length == 0) return;

        var rules = FactionRuleText.Parse(rulesSetting.Get() as string ?? "");
        rules[id] = rules.GetValueOrDefault(id) switch
        {
            FactionRule.General => FactionRule.Always,
            FactionRule.Always => FactionRule.Never,
            _ => FactionRule.General,
        };
        rulesSetting.Set(FactionRuleText.Format(rules));
    }

    void Turn(string by)
    {
        if (int.TryParse(by, out int step)) _page += step;
    }

    static UiNode Label(string id, string text, string style, float width = 0) =>
        new(id, UiNodeKind.Label) { Placement = new(width > 0 ? width : UiLength.Fill, Line), Text = text, Style = style };

    static UiNode Button(string id, string text, string action, string argument, string style, float width, bool enabled) =>
        new(id, UiNodeKind.Button)
        {
            Placement = new(width, Line + 4),
            Text = text,
            Action = action,
            Argument = argument,
            Enabled = enabled,
            Style = style,
        };
}
