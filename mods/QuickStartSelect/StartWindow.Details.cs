using System.Globalization;
using Meitou.Data.Ui;
using Meitou.Game.Host.Input;
using Meitou.Game.Ui;

namespace Meitou.QuickStartSelect;

/// <summary>The window's other half: the chosen start's lines and description, the advanced options with their help, and the popups.</summary>
sealed partial class StartWindow
{
    /// <summary>Most description and help lines laid out at once (the screen's control budget).</summary>
    const int MostLines = 24;

    readonly Dictionary<string, string> _alignStyles = [];
    readonly Dictionary<(string, int), List<string>> _wrapped = [];

    #region Details

    /// <summary>
    /// Name, difficulty (in its colour), cash, play style, town (a button when the start has several), characters, relations (a button when it
    /// sets any), then the description; in the wide layout the top half of the details area, the options under it.
    /// </summary>
    void AddDetails(UiNode root, UiScreenContext context)
    {
        if (Selected is not { } start) return;

        var area = _geo.Details;
        float height = settings.Wide && GameAccess.OptionsReady ? area.Height * .5f : area.Height;
        float s = _scale, x = area.X, y = area.Y, row = 24 * s, label = Math.Min(140 * s, area.Width * .35f), value = area.Width - label;
        root.Children.Add(Text("qss-name", start.Name, x, y, area.Width, 30 * s, scale: 1.15f));
        y += 32 * s;
        AddLine(root, "diff", tr("Difficulty:"), start.DifficultyText, x, ref y, label, value, Difficulty.Colour(start.Rank, DarkText(context)));
        AddLine(root, "cash", tr("Cash:"), "c." + start.Start.Money.ToString(CultureInfo.InvariantCulture), x, ref y, label, value);
        AddLine(root, "style", tr("Play Style:"), start.Style, x, ref y, label, value);
        AddTownLine(root, start, x, ref y, label, value, row);
        AddLine(root, "chars", tr("Characters:"), start.Characters, x, ref y, label, value);
        AddRelationsLine(root, start, x, ref y, label, value, row);
        AddDescription(root, context, start.Description, new UiRect(x, y + 4 * s, area.Width, area.Y + height - y - 4 * s));
    }

    void AddLine(UiNode root, string id, string label, string value, float x, ref float y, float labelWidth, float valueWidth,
        System.Numerics.Vector4? colour = null)
    {
        float row = 24 * _scale;
        root.Children.Add(Text($"qss-l-{id}", label, x, y, labelWidth, row));
        root.Children.Add(Text($"qss-v-{id}", value, x + labelWidth, y, valueWidth, row, colour: colour));
        y += row;
    }

    /// <summary>"Town:": the one town, or with several a button naming the pick ("Random" for the game's draw) that opens the choice.</summary>
    void AddTownLine(UiNode root, StartInfo start, float x, ref float y, float label, float value, float row)
    {
        if (start.Towns.Count == 0) return;

        if (start.Towns.Count == 1)
        {
            AddLine(root, "town", tr("Town:"), start.Towns[0].Name, x, ref y, label, value);
            return;
        }

        string picked = TownPick.For(start.Start.Id) is { } id && start.Towns.FirstOrDefault(t => t.Id == id) is { } town ? town.Name : tr("Random");
        root.Children.Add(Text("qss-l-town", tr("Town:"), x, y, label, row));
        root.Children.Add(Button("qss-v-town", picked, x + label, y, Math.Min(value, 260 * _scale), row, "popup:Towns"));
        y += row + 2 * _scale;
    }

    /// <summary>"Relations:": a button that lists the factions with their values, when the start sets any.</summary>
    void AddRelationsLine(UiNode root, StartInfo start, float x, ref float y, float label, float value, float row)
    {
        if (start.Relations.Count == 0) return;

        root.Children.Add(Text("qss-l-rel", tr("Relations:"), x, y, label, row));
        string caption = $"{tr("Relations")} ({start.Relations.Count.ToString(CultureInfo.InvariantCulture)})";
        root.Children.Add(Button("qss-v-rel", caption, x + label, y, Math.Min(value, 260 * _scale), row, "popup:Relations"));
        y += row + 2 * _scale;
    }

    /// <summary>The description wrapped to the area, from its scroll offset, with up and down buttons while it does not fit.</summary>
    void AddDescription(UiNode root, UiScreenContext context, string text, UiRect area)
    {
        _descArea = area;
        float line = 22 * _scale;
        int fits = Math.Max(1, (int)(area.Height / line));
        var lines = Wrap(context, text, area.Width - 24 * _scale);
        _descOffset = Math.Clamp(_descOffset, 0, Math.Max(0, lines.Count - fits));
        AddLines(root, "qss-desc", lines, _descOffset, Math.Min(fits, MostLines), area, line);
        if (lines.Count <= fits) return;

        float w = 20 * _scale, x = area.X + area.Width - w;
        root.Children.Add(Button("qss-desc-up", "^", x, area.Y, w, w, "desc:-3", _descOffset > 0));
        root.Children.Add(Button("qss-desc-down", "v", x, area.Y + area.Height - w, w, w, "desc:3", _descOffset < lines.Count - fits));
    }

    void AddLines(UiNode root, string id, List<string> lines, int from, int count, UiRect area, float line)
    {
        for (int i = 0; i < count && from + i < lines.Count; i++)
            root.Children.Add(Text($"{id}{i}", lines[from + i], area.X, area.Y + i * line, area.Width - 24 * _scale, line));
    }

    /// <summary>The text's lines at <paramref name="width"/> pixels, words kept whole (cached by text and width).</summary>
    List<string> Wrap(UiScreenContext context, string text, float width)
    {
        var key = (text, (int)width);
        if (_wrapped.TryGetValue(key, out var cached)) return cached;

        string plain = text.Replace("\r", "", StringComparison.Ordinal);
        List<string> lines;
        if (context.Frame.Measure("M").X <= 0) lines = WrapPlain(plain, width);
        else
        {
            lines = [];
            foreach (string paragraph in plain.Split('\n')) WrapParagraph(context, paragraph, width, lines);
        }

        if (_wrapped.Count > 64) _wrapped.Clear();
        _wrapped[key] = lines;
        return lines;
    }

    static void WrapParagraph(UiScreenContext context, string paragraph, float width, List<string> lines)
    {
        string current = "";
        foreach (string word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string next = current.Length == 0 ? word : current + " " + word;
            float measured = context.Frame.Measure(next, "Kenshi_StandardFont_Medium", 1).X;
            if (measured > width && current.Length > 0)
            {
                lines.Add(current);
                current = word;
            }
            else current = next;
        }

        lines.Add(current);
    }

    #endregion

    #region Options

    /// <summary>
    /// The game's advanced options: one row each (its name, which shows its help; less, the value, more; a tick's on or off), a right click on a
    /// row resetting it; the help and the default below or beside them. The bottom half of the details in the wide layout, all of it in the
    /// compact one.
    /// </summary>
    void AddOptions(UiNode root)
    {
        if (!GameAccess.OptionsReady || GameAccess.CurrentSettings() is not { } current) return;

        var area = _geo.Details;
        var box = settings.Wide ? new UiRect(area.X, area.Y + area.Height * .5f + 6 * _scale, area.Width, area.Height * .5f - 6 * _scale) : area;
        var options = OptionTable.All;
        float row = Math.Min(26 * _scale, box.Height * .6f / Math.Max(1, options.Count));
        float listWidth = settings.Wide ? box.Width * .6f : box.Width;
        for (int i = 0; i < options.Count; i++) AddOption(root, current, options[i], i, new UiRect(box.X, box.Y + i * row, listWidth, row));

        var help = settings.Wide
            ? new UiRect(box.X + listWidth + 10 * _scale, box.Y, box.Width - listWidth - 10 * _scale, box.Height)
            : new UiRect(box.X, box.Y + options.Count * row + 6 * _scale, box.Width, box.Height - options.Count * row - 6 * _scale);
        AddHelp(root, help);
    }

    void AddOption(UiNode root, object current, GameOption option, int i, UiRect r)
    {
        float s = _scale, arrow = r.Height, valueWidth = 54 * s, label = r.Width - 2 * arrow - valueWidth - 6 * s;
        var buttons = settings.RightClickReset ? UiPointerEvents.Buttons : 0;
        if (Hovered(r with { Width = label })) _help = i;
        root.Children.Add(new UiNode($"qss-opt{i}", UiNodeKind.Button)
        {
            Placement = new(label, r.Height, Left: r.X, Top: r.Y), Text = option.Label, Action = _act, Argument = $"opt:{i}", Pointer = buttons,
            Style = i == _help ? _chosen : _row, TextColour = _ink,
        });
        float value = OptionTable.Value(current, option);
        if (option.Tick)
        {
            root.Children.Add(Button($"qss-tick{i}", value >= .5f ? tr("on") : tr("off"), r.X + label + arrow, r.Y, valueWidth, r.Height, $"more:{i}"));
            return;
        }

        root.Children.Add(Button($"qss-less{i}", "<", r.X + label, r.Y, arrow, r.Height, $"less:{i}", value > option.Min));
        root.Children.Add(Text($"qss-val{i}", value.ToString("0.##", CultureInfo.InvariantCulture), r.X + label + arrow, r.Y, valueWidth, r.Height,
            "Center"));
        root.Children.Add(Button($"qss-more{i}", ">", r.X + label + arrow + valueWidth, r.Y, arrow, r.Height, $"more:{i}", value < option.Max));
    }

    /// <summary>The chosen option's help (the game's own text), then its default and how to reset it.</summary>
    void AddHelp(UiNode root, UiRect area)
    {
        var options = OptionTable.All;
        if (_help < 0 || _help >= options.Count || area.Height <= 0) return;

        var option = options[_help];
        string fallback = option.Tick ? (option.Default >= .5f ? tr("on") : tr("off")) : option.Default.ToString("0.##", CultureInfo.InvariantCulture);
        string text = GameAccess.Help(option.HelpLine);
        if (settings.RightClickReset) text += "\n" + tr("Default: %s. Right click resets it.").Replace("%s", fallback, StringComparison.Ordinal);
        float line = 22 * _scale;
        int fits = Math.Max(1, (int)(area.Height / line));
        AddLines(root, "qss-help", WrapPlain(text, area.Width - 24 * _scale), 0, Math.Min(fits, MostLines), area, line);
    }

    /// <summary>The help wrapped without a frame to measure with: about one character per 9 pixels at the scale.</summary>
    List<string> WrapPlain(string text, float width)
    {
        int chars = Math.Max(10, (int)(width / (9 * _scale)));
        var lines = new List<string>();
        foreach (string paragraph in text.Split('\n'))
        {
            string current = "";
            foreach (string word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (current.Length > 0 && current.Length + 1 + word.Length > chars)
                {
                    lines.Add(current);
                    current = word;
                }
                else current = current.Length == 0 ? word : current + " " + word;
            }

            lines.Add(current);
        }

        return lines;
    }

    /// <summary>A press on an option's name shows its help; a right click resets it to the game's default.</summary>
    void OptionPressed(int i, UiActionContext c)
    {
        _help = i;
        if (c.Button != MouseButton.Right || !settings.RightClickReset) return;

        var option = OptionTable.All[i];
        if (GameAccess.CurrentSettings() is { } current) GameAccess.SetSettings(OptionTable.With(current, option, option.Default));
    }

    /// <summary>Less or more: a slider by 0.05 within its range, a tick toggled.</summary>
    void Step(int i, int direction)
    {
        var option = OptionTable.All[i];
        _help = i;
        if (GameAccess.CurrentSettings() is not { } current) return;

        float value = OptionTable.Value(current, option);
        float next = option.Tick ? 1 - value : value + direction * .05f;
        GameAccess.SetSettings(OptionTable.With(current, option, next));
    }

    #endregion

    #region Popups

    /// <summary>The open popup over the list: its rows from its offset, up and down while they do not fit.</summary>
    void AddPopup(UiNode root)
    {
        if (_popup == Popup.None) return;

        var rows = PopupRows();
        var area = _geo.List;
        float row = _geo.Row;
        int fits = Math.Max(1, (int)(area.Height / row) - 1);
        _popupOffset = Math.Clamp(_popupOffset, 0, Math.Max(0, rows.Count - fits));
        root.Children.Add(new UiNode("qss-pop") { Placement = Place(area), Style = _panel });
        for (int i = 0; i < fits && _popupOffset + i < rows.Count; i++)
        {
            var (caption, argument) = rows[_popupOffset + i];
            root.Children.Add(new UiNode($"qss-pop{i}", UiNodeKind.Button)
            {
                Placement = new(area.Width - 24 * _scale, row, Left: area.X, Top: area.Y + i * row), Text = caption, Action = _act,
                Argument = argument, Style = _row, TextColour = _ink, Enabled = argument.Length > 0,
            });
        }

        float w = 20 * _scale, x = area.X + area.Width - w, bottom = area.Y + area.Height - row;
        if (rows.Count > fits) root.Children.Add(Button("qss-pop-up", "^", x, area.Y, w, w, "pop:-3", _popupOffset > 0));
        if (rows.Count > fits) root.Children.Add(Button("qss-pop-down", "v", x, bottom - w, w, w, "pop:3", _popupOffset < rows.Count - fits));
        root.Children.Add(Button("qss-pop-close", tr("Close"), area.X, bottom, area.Width, row, "popup:" + _popup));
    }

    /// <summary>The popup's rows: caption and action argument (empty: a line that does nothing).</summary>
    List<(string Caption, string Argument)> PopupRows()
    {
        var selected = Selected;
        return _popup switch
        {
            Popup.Races => [($"{tr("All races")} ({starts.Count})", "race:"),
                .. starts.SelectMany(s => s.Races).Distinct().OrderBy(r => r, StringComparer.CurrentCultureIgnoreCase)
                    .Select(r => ($"{r} ({RaceCount(r)})", "race:" + r))],
            Popup.Towns when selected is not null => [(tr("Random"), "town:"), .. selected.Towns.Select(t => (t.Name, "town:" + t.Id))],
            Popup.Relations when selected is not null =>
                [.. selected.Relations.Select(r => ($"{r.Faction}: {r.Value.ToString(CultureInfo.InvariantCulture)}", ""))],
            _ => [],
        };
    }

    #endregion

    /// <summary>A text style aligned as asked (registered on first use).</summary>
    string AlignStyle(string align) => _alignStyles.GetValueOrDefault(align, _row);
}
