using System.Globalization;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Game.Ui;
using Meitou.Modding;

namespace Meitou.Mentors;

/// <summary>
/// The training window: teacher, field, the student's money, and one row per skill (the student's level, the teacher's, +1 and +5 with their
/// prices). The offer comes from the running system (<see cref="MentorSystem.Offer"/>); a press queues a command.
/// </summary>
sealed class MentorWindow(ModContext context) : IUiExtension
{
    const float Width = 560, Line = 22, Top = 110;

    readonly record struct Styles(string Panel, string Text, string Dim, string Button);

    Styles _styles;
    string _buy = "", _close = "";

    public string Id => "dxseba.mentors.ui";

    public void Register(UiRegistration registration)
    {
        var ink = new System.Numerics.Vector4(.91f, .86f, .74f, 1);
        var pad = new Meitou.Data.Ui.UiInsets(6, 1, 6, 1);
        _styles = new(
            registration.RegisterStyle("panel", new() { Background = new(.10f, .085f, .07f, .95f), Text = ink }),
            registration.RegisterStyle("text", new() { Text = ink, TextPadding = pad }),
            registration.RegisterStyle("dim", new() { Text = new(.66f, .62f, .54f, 1), TextPadding = pad }),
            registration.RegisterStyle("button", new()
            {
                Background = new(.20f, .15f, .11f, .95f), Hover = new(.32f, .22f, .16f, 1), Disabled = new(.14f, .12f, .10f, .8f), TextPadding = pad,
            }));
        _buy = registration.RegisterAction("buy", c => Buy(c.Argument));
        _close = registration.RegisterAction("close", _ => context.Enqueue(new CloseLessonsOrder { Tick = context.Tick }));
        registration.Contribute(UiSlots.Overlay, Build, order: 58);
    }

    UiNode Build(UiFrameContext frame)
    {
        if (MentorsMod.Running?.Offer is not { } offer) return new UiNode("mentors-hidden") { Visible = false };

        var lines = new List<UiNode> { Header(offer), Text("mentors-sub", $"Student: {offer.StudentName}    Cats: {Cats(offer.Money)}", _styles.Dim) };
        if (offer.Lessons.Count > 0) lines.Add(ColumnTitles());
        lines.AddRange(offer.Lessons.Select(l => LessonRow(l, offer.Money)));
        if (offer.Status != "") lines.Add(Text("mentors-status", offer.Status, _styles.Dim));

        float height = 12 + lines.Count * (Line + 2);
        var window = new UiNode("mentors-window")
        {
            Placement = new(Width, height, Left: (frame.Viewport.Width - Width) / 2, Top: Top), Style = _styles.Panel, Flow = UiFlow.Column,
            Padding = 6, Gap = 2,
        };
        window.Children.AddRange(lines);

        var root = new UiNode("mentors") { Placement = UiPlacement.Fill, Pickable = false };
        root.Children.Add(window);
        return root;
    }

    UiNode Header(LessonOffer offer)
    {
        var row = Row("mentors-header");
        row.Children.Add(Label("mentors-title", $"{offer.TeacherName} ({offer.Profession})", Width - 12 - 70 - 4, _styles.Text));
        row.Children.Add(Button("mentors-close", "Close", 70, _close, "", true));
        return row;
    }

    UiNode ColumnTitles()
    {
        var row = Row("mentors-titles");
        row.Children.Add(Label("mentors-t-skill", "Skill", 170, _styles.Dim));
        row.Children.Add(Label("mentors-t-own", "You", 50, _styles.Dim));
        row.Children.Add(Label("mentors-t-teacher", "Teacher", 70, _styles.Dim));
        return row;
    }

    UiNode LessonRow(Lesson lesson, long money)
    {
        string key = lesson.Stat.ToString();
        var row = Row("mentors-row-" + key);
        row.Children.Add(Label($"mentors-{key}-name", lesson.Label, 170, _styles.Text));
        row.Children.Add(Label($"mentors-{key}-own", Level(lesson.Student), 50, _styles.Text));
        row.Children.Add(Label($"mentors-{key}-teacher", Level(lesson.Teacher), 70, _styles.Dim));
        row.Children.Add(Button($"mentors-{key}-1", $"+1  {Cats(lesson.Cost1)}", 115, _buy, key + ":1", money >= lesson.Cost1));
        if (lesson.Batch > 1)
            row.Children.Add(Button($"mentors-{key}-n", $"+{lesson.Batch}  {Cats(lesson.CostBatch)}", 125, _buy, $"{key}:{lesson.Batch}", money >= lesson.CostBatch));
        return row;
    }

    void Buy(string argument)
    {
        if (MentorsMod.Running?.Offer is not { } offer) return;

        var parts = argument.Split(':');
        if (parts.Length != 2 || !Enum.TryParse<StatsEnumerated>(parts[0], out var stat) || !int.TryParse(parts[1], CultureInfo.InvariantCulture, out int levels))
            return;

        context.Enqueue(new BuyLessonOrder(offer.Student, offer.Teacher, stat, levels) { Tick = context.Tick });
    }

    static string Level(int level) => level.ToString(CultureInfo.InvariantCulture);

    static string Cats(long cats) => cats.ToString("N0", CultureInfo.InvariantCulture) + "c";

    static UiNode Row(string id) =>
        new(id) { Placement = new(UiLength.Fill, Line), Flow = UiFlow.Row, Gap = 4, Pickable = false };

    static UiNode Label(string id, string text, float width, string style) =>
        new(id, UiNodeKind.Label) { Placement = new(width, Line), Text = text, Style = style };

    UiNode Text(string id, string text, string style) =>
        new(id, UiNodeKind.Label) { Placement = new(UiLength.Fill, Line), Text = text, Style = style };

    UiNode Button(string id, string text, float width, string action, string argument, bool enabled) =>
        new(id, UiNodeKind.Button)
        {
            Placement = new(width, Line), Text = text, Style = _styles.Button, Action = enabled ? action : "", Argument = argument, Enabled = enabled,
        };
}
