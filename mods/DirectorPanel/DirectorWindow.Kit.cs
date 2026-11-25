using System.Globalization;
using Meitou.Game.Ui;

namespace Meitou.DirectorPanel;

/// <summary>The window's building blocks: rows, labels, buttons with their press handlers, tick boxes, text fields and pick lists.</summary>
internal sealed partial class DirectorWindow
{
    /// <summary>Rows a pick list shows at most (the rest are found by searching).</summary>
    const int ListRows = 120;

    readonly record struct Styles(string Panel, string Title, string Text, string Dim, string Button, string Picked, string List);

    static Styles RegisterStyles(Meitou.Game.Ui.UiRegistration r) => new(
        r.RegisterStyle("panel", new() { Background = new(.09f, .08f, .07f, .96f), Text = new(.91f, .86f, .74f, 1) }),
        r.RegisterStyle("title", new() { Background = new(.16f, .11f, .08f, 1), Text = new(.95f, .80f, .55f, 1), TextAlign = "Center" }),
        r.RegisterStyle("text", new() { Text = new(.91f, .86f, .74f, 1), TextPadding = new(4, 1, 4, 1) }),
        r.RegisterStyle("dim", new() { Text = new(.68f, .64f, .55f, 1), TextPadding = new(4, 1, 4, 1) }),
        r.RegisterStyle("button", new()
        {
            Background = new(.20f, .15f, .11f, .95f), Hover = new(.32f, .22f, .16f, 1), Active = new(.40f, .28f, .18f, 1),
            Disabled = new(.14f, .12f, .10f, .8f), TextPadding = new(6, 2, 6, 2),
        }),
        r.RegisterStyle("picked", new() { Background = new(.42f, .26f, .14f, 1), Hover = new(.50f, .32f, .18f, 1), TextPadding = new(6, 2, 6, 2) }),
        r.RegisterStyle("list", new() { Background = new(.05f, .045f, .04f, .9f) }));

    static UiNode Row(string id) => new(id) { Placement = new(UiLength.Fill, Line + 4), Flow = UiFlow.Row, Gap = 4 };

    UiNode Label(string id, string text, string? style = null, float width = 0) =>
        new(id, UiNodeKind.Label) { Placement = new(width > 0 ? width : UiLength.Fill, Line), Text = text, Style = style ?? _styles.Text };

    /// <summary>A section's heading (an original label such as "Strike a body part:").</summary>
    UiNode Heading(string id, string english) => Label(id, texts[english], _styles.Title);

    UiNode Hint(string id, string english) => Label(id, texts[english], _styles.Dim);

    UiNode Button(string id, string text, Action press, float width, bool enabled = true, bool picked = false)
    {
        _handlers[id] = press;
        return new UiNode(id, UiNodeKind.Button)
        {
            Placement = new(width > 0 ? width : UiLength.Fill, Line + 4), Text = text, Action = _do, Argument = id, Enabled = enabled,
            Style = picked ? _styles.Picked : _styles.Button,
        };
    }

    /// <summary>A tick box: "[x] label", the press flips the value through <paramref name="set"/>.</summary>
    UiNode Tick(string id, string english, bool on, Action<bool> set, float width = 0) =>
        Button(id, (on ? "[x] " : "[  ] ") + texts[english], () => set(!on), width);

    UiNode Field(string id, float width, string fallback = "", int maxLength = 64)
    {
        string text = _fields.TryGetValue(id, out string? t) ? t : _fields[id] = fallback;
        return new UiNode(id, UiNodeKind.TextBox) { Placement = new(width, Line + 2), Text = text, Action = _text, Argument = id, MaxLength = maxLength };
    }

    string Text(string id) => _fields.TryGetValue(id, out string? t) ? t : "";

    float Number(string id, float fallback) =>
        float.TryParse(Text(id).Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) && float.IsFinite(v)
            ? v : fallback;

    /// <summary>A labelled field on one row: "label [field]".</summary>
    UiNode LabelledField(string id, string english, string fallback, float labelWidth = 180, float fieldWidth = 120)
    {
        var line = Row(id + "-row");
        line.Children.Add(Label(id + "-label", texts[english], width: labelWidth));
        line.Children.Add(Field(id, fieldWidth, fallback));
        return line;
    }

    /// <summary>A search box over a pick list of <paramref name="entries"/>; a press picks one (shown highlighted).</summary>
    IEnumerable<UiNode> PickList(string id, IEnumerable<Entry> entries, string? picked, Action<Entry> pick, int rows = 10,
        Func<Entry, string>? caption = null)
    {
        var line = Row(id + "-search-row");
        line.Children.Add(Label(id + "-search-label", texts["Search:"], width: 80));
        line.Children.Add(Field(id + "-search", 300));
        var (shown, found) = Catalog.Search(entries, Text(id + "-search"), ListRows);
        line.Children.Add(Label(id + "-found", found.ToString(CultureInfo.InvariantCulture), _styles.Dim));
        yield return line;

        var list = new UiNode(id + "-list", UiNodeKind.ScrollView)
        {
            Placement = new(UiLength.Fill, rows * (Line + 4 + 2) + 4), Style = _styles.List, Flow = UiFlow.Column, Padding = 2, Gap = 2,
        };

        for (int i = 0; i < shown.Count; i++)
        {
            var entry = shown[i];
            list.Children.Add(Button($"{id}-{i}", caption?.Invoke(entry) ?? entry.Name, () => pick(entry), 0, picked: entry.Id == picked));
        }

        yield return list;
    }

    /// <summary>A row of exclusive choices; the chosen one is highlighted.</summary>
    UiNode Choices(string id, string[] english, int chosen, Action<int> choose)
    {
        var line = Row(id);
        float width = (Width - 40 - (english.Length - 1) * 4) / english.Length;
        for (int i = 0; i < english.Length; i++)
        {
            int k = i;
            line.Children.Add(Button($"{id}-{i}", texts[english[i]], () => choose(k), width, picked: i == chosen));
        }

        return line;
    }

    static string F0(float v) => v.ToString("0", CultureInfo.InvariantCulture);
}
