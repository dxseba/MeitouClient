using System.Numerics;
using Meitou.Data;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;

namespace Meitou.Mentors;

/// <summary>One skill the window offers: the levels and the prices of the next 1 and the next <see cref="MentorSystem.Batch"/> levels.</summary>
sealed record Lesson(StatsEnumerated Stat, string Label, int Student, int Teacher, int Max, long Cost1, int Batch, long CostBatch);

/// <summary>What the window shows: the student, the teacher and its field, the player's money and the lessons.</summary>
sealed record LessonOffer(
    CharacterId Student, string StudentName, CharacterId Teacher, string TeacherName, string Profession, long Money, IReadOnlyList<Lesson> Lessons,
    string Status);

/// <summary>
/// Opens, refreshes and sells the lessons in the serial slow step: money and stats change only here. The offer is published every 10th tick
/// while open, for the window (<see cref="Offer"/>).
/// </summary>
sealed class MentorSystem(MentorsSettings settings, GameDatabase data) : ITickSystem
{
    public const int Batch = 5;

    readonly List<SimCommand> _orders = [];
    (CharacterId Student, CharacterId Teacher)? _open;
    string _status = "";

    /// <summary>The open offer; null while the window is closed.</summary>
    public volatile LessonOffer? Offer;

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands)
        {
            if (command is OpenLessonsOrder or BuyLessonOrder or CloseLessonsOrder) _orders.Add(command);
        }
    }

    public void SlowWorld(World world)
    {
        bool changed = _orders.Count > 0;
        foreach (var order in _orders) Apply(world, order);
        _orders.Clear();

        if (_open is null)
        {
            Offer = null;
            return;
        }

        if (changed || world.Tick % 10 == 0) Publish(world);
    }

    void Apply(World world, SimCommand order)
    {
        switch (order)
        {
            case OpenLessonsOrder open:
                _open = (open.Student, open.Teacher);
                _status = "";
                break;
            case CloseLessonsOrder:
                _open = null;
                break;
            case BuyLessonOrder buy when _open == (buy.Student, buy.Teacher):
                _status = Buy(world, buy);
                break;
        }
    }

    #region Offer

    void Publish(World world)
    {
        var (studentId, teacherId) = _open!.Value;
        var table = world.Characters;
        if (!table.TryResolveNext(studentId, out int s) || !table.TryResolveNext(teacherId, out int t)
            || table.Cold(s) is not { Stats: { } studentStats } student || table.Cold(t) is not { Stats: { } teacherStats } teacher)
        {
            _open = null;
            Offer = null;
            return;
        }

        var field = FieldOf(world, teacher);
        var lessons = field is null ? [] : Lessons(field, studentStats, teacherStats);
        string status = _status != "" ? _status : field is null ? $"{teacher.Name} has nothing to teach." : lessons.Count == 0 ? "Nothing left to learn here." : "";
        Offer = new(studentId, student.Name, teacherId, teacher.Name, field?.Name ?? "None", world.Player.Money, lessons, status);
    }

    Profession? FieldOf(World world, CharacterCold teacher)
    {
        if (!settings.RequireProfession) return Professions.Anyone;

        string name = data.Find(teacher.RecordId)?.Name ?? "";
        bool trader = world.Squads.Find(teacher.SquadId)?.Shop is not null;
        return Professions.Of(name, trader, teacher.NpcClass);
    }

    List<Lesson> Lessons(Profession field, CharacterStats student, CharacterStats teacher)
    {
        var lessons = new List<Lesson>();
        foreach (var stat in field.Skills)
        {
            int own = (int)student.Stored(stat), master = (int)teacher[stat];
            int max = Math.Min(settings.MaxLevel, master - settings.TeacherGap);
            if (master < settings.MinTeacherLevel || own >= max) continue;

            int batch = Math.Min(Batch, max - own);
            lessons.Add(new(stat, Professions.Label(stat), own, master, max, Price(stat, own, master, 1), batch, Price(stat, own, master, batch)));
        }

        return lessons;
    }

    /// <summary>
    /// The cats for <paramref name="levels"/> levels from <paramref name="from"/>: each level costs <c>base x (1 + level/10)^2 x (0.75 + teacher/200)</c>,
    /// attributes times the multiplier. The original's formula is not published; this one rises with both, as its description says.
    /// </summary>
    long Price(StatsEnumerated stat, int from, int teacher, int levels)
    {
        double total = 0;
        for (int level = from; level < from + levels; level++)
            total += settings.BasePrice * Math.Pow(1 + level / 10.0, 2) * (0.75 + teacher / 200.0);

        if (Professions.IsAttribute(stat)) total *= settings.AttributeMultiplier;
        return (long)Math.Round(total);
    }

    #endregion

    #region Buying

    /// <summary>Pays and raises the stat; the status line says what happened.</summary>
    string Buy(World world, BuyLessonOrder buy)
    {
        Publish(world);
        if (Offer?.Lessons.FirstOrDefault(l => l.Stat == buy.Stat) is not { } lesson) return "That lesson is not offered.";
        if (TooFar(world, buy)) return "Walk closer to the teacher.";

        int levels = Math.Min(buy.Levels, lesson.Max - lesson.Student);
        long price = levels == 1 ? lesson.Cost1 : lesson.CostBatch;
        if (world.Player.Money < price) return $"Not enough cats ({price} needed).";

        world.Player.Money -= price;
        var table = world.Characters;
        table.TryResolveNext(buy.Student, out int s);
        var stats = table.Cold(s)!.Stats!;
        stats[buy.Stat] = stats.Stored(buy.Stat) + levels;
        return $"{lesson.Label} {lesson.Student} -> {lesson.Student + levels} for {price} cats.";
    }

    bool TooFar(World world, BuyLessonOrder buy)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(buy.Student, out int s) || !table.TryResolveNext(buy.Teacher, out int t)) return true;

        return Vector3.Distance(table.Next[s].Position, table.Next[t].Position) > settings.Distance;
    }

    #endregion
}
