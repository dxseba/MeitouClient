using Meitou.Data;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;

namespace Meitou.Mentors;

/// <summary>One skill the window offers: the levels (the student's, the most it can reach, the teacher's) and the next 1 and 5 lessons.</summary>
sealed record Lesson(StatsEnumerated Stat, string Label, int Student, int Limit, int Teacher, int Count1, long Cost1, int CountBatch, long CostBatch);

/// <summary>What the window shows: the student, the teacher and its trade, the player's money and the lessons.</summary>
sealed record LessonOffer(
    CharacterId Student, string StudentName, CharacterId Teacher, string TeacherName, string Profession, long Money, IReadOnlyList<Lesson> Lessons,
    string Status);

/// <summary>
/// The original's lesson rules (<b>Verified</b>, original DLL (decompiled), constants from its rdata): a teacher at <c>MinTeacherLevel</c> or more
/// teaches a skill while the student's level + 1 is at most min(teacher - <c>TeacherMargin</c>, <c>TrainingCap</c>); each lesson is one level and
/// costs <c>BasePrice x (1 + floor(level)/10)^2 x (0.75 + teacher/100)</c>, times <c>AttributePriceMult</c> for an attribute, rounded up to 10.
/// </summary>
static class LessonMath
{
    public const int Batch = 5;
    const float MaxStat = 100;

    public static float Limit(MentorsSettings s, float teacher) => MathF.Min(teacher - s.TeacherGap, s.MaxLevel);

    public static bool CanTeach(MentorsSettings s, float teacher, float student) => teacher >= s.MinTeacherLevel && student + 1 <= Limit(s, teacher);

    /// <summary>How many of <paramref name="wanted"/> lessons the teacher can give from <paramref name="student"/>, and their price.</summary>
    public static (int Count, long Price) Lessons(MentorsSettings s, float teacher, float student, bool attribute, int wanted)
    {
        float limit = Limit(s, teacher), level = student;
        int count = 0;
        long price = 0;
        while (count < wanted && limit >= level + 1)
        {
            double cost = s.BasePrice * Math.Pow(MathF.Floor(level) / 10.0 + 1, 2) * (teacher / 100.0 + 0.75);
            if (attribute) cost *= s.AttributeMultiplier;
            price += (long)(Math.Ceiling(cost / 10) * 10);
            count++;
            level++;
        }

        return (count, price);
    }

    /// <summary>The stat after <paramref name="count"/> lessons: raised by that many levels, at most 100.</summary>
    public static float Trained(float stored, int count) => MathF.Min(stored + count, MaxStat);
}

/// <summary>
/// Opens, refreshes and sells the lessons in the serial slow step: money and stats change only here. The offer is published every 10th tick
/// while open, for the window (<see cref="Offer"/>).
/// </summary>
sealed class MentorSystem(MentorsSettings settings, GameDatabase data) : ITickSystem
{
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
        if (Pair(world, studentId, teacherId) is not var (student, teacher))
        {
            _open = null;
            Offer = null;
            return;
        }

        var field = FieldOf(world, teacher);
        var lessons = field is null || !Available(student, teacher) ? [] : Lessons(field, student.Stats!, teacher.Stats!);
        string status = _status != "" ? _status : lessons.Count == 0 ? "Nothing left to teach." : "";
        string trade = field is null ? "none" : $"{field.Name} ({field.Covers})";
        Offer = new(studentId, student.Name, teacherId, teacher.Name, trade, world.Player.Money, lessons, status);
    }

    static (CharacterCold Student, CharacterCold Teacher)? Pair(World world, CharacterId studentId, CharacterId teacherId)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(studentId, out int s) || !table.TryResolveNext(teacherId, out int t)) return null;
        if (table.Cold(s) is not { Stats: not null } student || table.Cold(t) is not { Stats: not null } teacher) return null;

        return (student, teacher);
    }

    /// <summary>The original's pair check: a player character learning from someone else who is not a player character, alive and not a slave.</summary>
    static bool Available(CharacterCold student, CharacterCold teacher) =>
        student != teacher && student.IsPlayer && !teacher.IsPlayer && teacher.DiedTick < 0 && teacher.SlaveState != 1;

    /// <summary>The teacher's trade from its record's untranslated name, its shop and its NPC class (<see cref="Professions.Of"/>).</summary>
    Profession? FieldOf(World world, CharacterCold teacher)
    {
        if (!settings.RequireProfession) return Professions.Anyone;

        bool trader = world.Squads.Find(teacher.SquadId)?.Shop is not null;
        return Professions.Of(data.Find(teacher.RecordId)?.BaseName ?? "", trader, teacher.NpcClass);
    }

    List<Lesson> Lessons(Profession field, CharacterStats student, CharacterStats teacher)
    {
        var lessons = new List<Lesson>();
        foreach (var skill in Professions.All)
        {
            float own = student.Stored(skill.Stat), master = teacher[skill.Stat];
            if (!field.Skills.Contains(skill.Stat) || !LessonMath.CanTeach(settings, master, own)) continue;

            var one = LessonMath.Lessons(settings, master, own, skill.Attribute, 1);
            var batch = LessonMath.Lessons(settings, master, own, skill.Attribute, LessonMath.Batch);
            int limit = (int)LessonMath.Limit(settings, master);
            lessons.Add(new(skill.Stat, skill.Label, (int)own, limit, (int)master, one.Count, one.Price, batch.Count, batch.Price));
        }

        return lessons;
    }

    #endregion

    #region Buying

    /// <summary>Pays and raises the stat; the status line says what happened (the original's messages).</summary>
    string Buy(World world, BuyLessonOrder buy)
    {
        if (Pair(world, buy.Student, buy.Teacher) is not var (student, teacher) || !Available(student, teacher))
            return "The teacher or the student is not available now.";

        var skill = Professions.All.FirstOrDefault(t => t.Stat == buy.Stat);
        var field = FieldOf(world, teacher);
        var stats = student.Stats!;
        float own = stats.Stored(buy.Stat), master = teacher.Stats![buy.Stat];
        if (skill is null || field is null || !field.Skills.Contains(buy.Stat) || !LessonMath.CanTeach(settings, master, own))
            return "The teacher cannot teach this any further.";

        var (count, price) = LessonMath.Lessons(settings, master, own, skill.Attribute, buy.Levels);
        if (count < 1) return "The teacher cannot teach this any further.";
        if (world.Player.Money < price) return $"Not enough money: it costs {price} cats.";

        world.Player.Money -= price;
        stats[buy.Stat] = LessonMath.Trained(own, count);
        return $"{skill.Label}: {(int)own} -> {(int)stats.Stored(buy.Stat)}  (-{price} cats)";
    }

    #endregion
}
