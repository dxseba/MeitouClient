using Meitou.Game.Ui;

namespace Meitou.ShopCounterRestock;

/// <summary>
/// The window's nodes, built each frame from the published view.
/// </summary>
internal sealed partial class RestockWindow
{
    const float Width = 640, Line = 22, Gap = 2;

    UiNode Build(Styles s, Actions a)
    {
        if (_counter is not { } site || ShopCounterRestockMod.Running?.View is not { } view || !view.Counters.TryGetValue(site, out var counter))
            return new UiNode("restock-hidden") { Visible = false };

        var body = new UiNode("restock-window")
        {
            Placement = new(Width, 560, Left: 140, Top: 90),
            Style = s.Panel,
            Flow = UiFlow.Column,
            Padding = 6,
            Gap = Gap,
        };

        body.Children.Add(Label("restock-title", context.Tr("Shop counter restock - ") + counter.Name, s.Text));
        body.Children.Add(WorkerLine(view, counter, s, a));
        AddSearch(body, s, a);
        AddWhitelist(body, counter, s, a);
        body.Children.Add(Bottom(counter, s, a));
        body.Children.Add(Label("restock-status", context.Tr(counter.Status), s.Job));

        var root = new UiNode("restock-root") { Placement = UiPlacement.Fill, Pickable = false };
        root.Children.Add(body);
        return root;
    }

    UiNode WorkerLine(RestockView view, CounterView counter, Styles s, Actions a)
    {
        string assigned = counter.WorkerName ?? context.Tr("Unassigned");
        string chosen = _workerCursor >= 0 && _workerCursor < view.Workers.Count ? view.Workers[_workerCursor].Name : assigned;

        var line = Row("restock-worker");
        line.Children.Add(Label("restock-worker-label", context.Tr("Assigned worker") + ": " + assigned, s.Text, 230));
        line.Children.Add(Button("restock-worker-prev", "<", a.Worker, "-1", s.Button, 28));
        line.Children.Add(Label("restock-worker-name", chosen, s.Text, 170));
        line.Children.Add(Button("restock-worker-next", ">", a.Worker, "1", s.Button, 28));
        line.Children.Add(Button("restock-assign", context.Tr("Assign"), a.Assign, "", s.Button, 80, _workerCursor >= 0));
        line.Children.Add(Button("restock-unassign", context.Tr("Unassigned"), a.Assign, "none", s.Button, 90, !counter.Worker.IsNone));
        return line;
    }

    void AddSearch(UiNode body, Styles s, Actions a)
    {
        var (shown, found) = catalog.Search(_search, ResultLines);
        var line = Row("restock-search-line");
        string hint = _search.Trim().Length == 0 ? context.Tr("Search all items by name or ID") : $"{found} " + context.Tr("items found");
        line.Children.Add(TextBox("restock-search", _search, a.Search, 380, 64));
        line.Children.Add(Label("restock-found", hint, s.Text, 230));
        body.Children.Add(line);

        var list = List("restock-results", ResultLines, s);
        for (int i = 0; i < shown.Count; i++) list.Children.Add(Pick($"restock-result-{i}", $"{shown[i].Name}  ({shown[i].Id})", shown[i].Id, s, a));
        body.Children.Add(list);
    }

    void AddWhitelist(UiNode body, CounterView counter, Styles s, Actions a)
    {
        body.Children.Add(Label("restock-whitelist-title", context.Tr("Counter whitelist (target units)"), s.Text));
        var list = List("restock-whitelist", 8, s);
        for (int i = 0; i < counter.Items.Count; i++)
        {
            var item = counter.Items[i];
            string name = catalog.Knows(item.Id) ? item.Name : context.Tr("Missing item") + " " + item.Id;
            list.Children.Add(Pick($"restock-item-{i}", $"{name}: {item.Stock} / {item.Target}", item.Id, s, a));
        }

        body.Children.Add(list);

        var line = Row("restock-target-line");
        line.Children.Add(Label("restock-target-label", context.Tr("Target:"), s.Text, 70));
        line.Children.Add(TextBox("restock-target", _targetText, a.Target, 90, 5));
        line.Children.Add(Button("restock-add", context.Tr("Add / update"), a.Add, "", s.Button, 130, _picked is not null));
        line.Children.Add(Button("restock-set", context.Tr("Set target"), a.Add, "", s.Button, 110, Listed(counter)));
        line.Children.Add(Button("restock-remove", context.Tr("Remove"), a.Remove, "", s.Button, 100, Listed(counter)));
        line.Children.Add(Label("restock-picked", _picked is { } p ? catalog.NameOf(p) : "", s.Text, 120));
        body.Children.Add(line);
    }

    bool Listed(CounterView counter) => _picked is { } p && counter.Items.Any(i => i.Id == p);

    UiNode Bottom(CounterView counter, Styles s, Actions a)
    {
        var line = Row("restock-bottom");
        line.Children.Add(counter.Enabled
            ? Button("restock-toggle", context.Tr("Disable restock"), a.Enable, "off", s.Button, 160)
            : Button("restock-toggle", context.Tr("Enable restock"), a.Enable, "on", s.Button, 160));
        line.Children.Add(Button("restock-close", context.Tr("Close"), a.Close, "", s.Button, 100));
        return line;
    }

    static UiNode TextBox(string id, string text, string action, float width, int maxLength) =>
        new(id, UiNodeKind.TextBox) { Placement = new(width, Line), Text = text, Action = action, MaxLength = maxLength };

    static UiNode Row(string id) => new(id) { Placement = new(UiLength.Fill, Line + 4), Flow = UiFlow.Row, Gap = 4 };

    static UiNode List(string id, int lines, Styles s) => new(id, UiNodeKind.ScrollView)
    {
        Placement = new(UiLength.Fill, lines * (Line + Gap) + 4),
        Style = s.List,
        Flow = UiFlow.Column,
        Padding = 2,
        Gap = Gap,
    };

    UiNode Pick(string id, string text, string item, Styles s, Actions a) =>
        new(id, UiNodeKind.Button)
        {
            Placement = new(UiLength.Fill, Line),
            Text = (_picked == item ? "> " : "") + text,
            Action = a.Pick,
            Argument = item,
            Style = s.Button,
        };

    static UiNode Label(string id, string text, string style, float width = 0) =>
        new(id, UiNodeKind.Label) { Placement = new(width > 0 ? width : UiLength.Fill, Line), Text = text, Style = style };

    static UiNode Button(string id, string text, string action, string argument, string style, float width, bool enabled = true) =>
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
