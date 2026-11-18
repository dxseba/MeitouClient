using Meitou.Data.Gameplay.Bodies;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.Mentors;

/// <summary>
/// Port of KENSHI: MENTORS (workshop 3834394158), rebuilt from its description: a "Learn from" row on a friendly NPC's right-click menu opens a
/// training window with the skills the teacher's profession covers, bought +1 or +5 levels at a time for cats.
/// </summary>
public sealed class MentorsMod : IMod
{
    internal static readonly MentorsSettings Settings = new();

    /// <summary>The running world's system, for the window's offer; null before a world starts.</summary>
    internal static volatile MentorSystem? Running;

    public void Initialize(ModContext context)
    {
        context.AddSettings(Settings);
        context.AddSystem("mentors", _ => Running = new MentorSystem(Settings, context.Data), SystemAnchor.End);
        context.AddUi(new LearnMenu());
        context.AddUi(new MentorWindow(context));
        context.Log("Mentors initialized");
    }
}

/// <summary>The "Learn from" row: the first selected character walks to the target and the window opens on what it can learn there.</summary>
sealed class LearnMenu : IUiExtension
{
    public string Id => "dxseba.mentors.menu";

    public void Register(UiRegistration registration) => registration.AddCharacterMenuVerbs(Verbs);

    static IEnumerable<UiMenuVerb> Verbs(UiMenuContext menu)
    {
        var target = menu.Target;
        if (!MentorsMod.Settings.Enabled || target.IsPlayer || target.HostileToPlayer || target.Imprisoned) return [];
        if (target.Body is not { Dead: false, Unconscious: false } || target.CarriedBy != CharacterId.None) return [];

        var student = menu.Selected.Where(id => id != target.Id).DefaultIfEmpty(CharacterId.None).First();
        if (student == CharacterId.None) return [];

        return [new UiMenuVerb("Learn from", () =>
        {
            menu.Enqueue(new MoveOrder([student], target.Position) { Tick = menu.Tick });
            menu.Enqueue(new OpenLessonsOrder(student, target.Id) { Tick = menu.Tick });
        })];
    }
}

/// <summary><paramref name="Student"/> asks <paramref name="Teacher"/> what it can teach; the window shows the offer.</summary>
public sealed record OpenLessonsOrder(CharacterId Student, CharacterId Teacher) : SimCommand;

/// <summary>The open offer's student buys <paramref name="Levels"/> levels of <paramref name="Stat"/>.</summary>
public sealed record BuyLessonOrder(CharacterId Student, CharacterId Teacher, StatsEnumerated Stat, int Levels) : SimCommand;

/// <summary>The training window closes.</summary>
public sealed record CloseLessonsOrder : SimCommand;
