using System.Numerics;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Game.Host.Input;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.Mentors;

/// <summary>
/// Port of KENSHI: MENTORS (workshop 3834394158), rebuilt from its description: a "Learn from" row on a friendly NPC's right-click menu opens a
/// training window with the skills the teacher's profession covers, bought +1 or +5 levels at a time for cats; Ctrl+Shift with the "Mentors
/// window" key opens it on the nearest teacher, or closes it.
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
        context.AddKeyAction("window", "Mentors window (Ctrl+Shift)", LessonShortcut.Pressed);
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
        if (!MentorsMod.Settings.Enabled || !CanTeach(target)) return [];

        var student = menu.Selected.Where(id => id != target.Id).DefaultIfEmpty(CharacterId.None).First();
        if (student == CharacterId.None) return [];

        return [new UiMenuVerb("Learn from", () =>
        {
            menu.Enqueue(new MoveOrder([student], target.Position) { Tick = menu.Tick });
            menu.Enqueue(new OpenLessonsOrder(student, target.Id) { Tick = menu.Tick });
        })];
    }

    /// <summary>A living, conscious, free, non-hostile non-player character: one the row is offered on.</summary>
    public static bool CanTeach(CharacterSnapshot c) =>
        !c.IsPlayer && !c.HostileToPlayer && !c.Imprisoned && c.Body is { Dead: false, Unconscious: false } && c.CarriedBy == CharacterId.None;
}

/// <summary>
/// Ctrl+Shift with the "Mentors window" key (the original's Ctrl+Shift+T; unbound by default, as the game holds T): closes the open window, or
/// opens it for the first selected character on the nearest teacher within <see cref="MentorsSettings.Distance"/>. Without both modifiers the
/// key does nothing.
/// </summary>
static class LessonShortcut
{
    public static void Pressed(ModKeyContext key)
    {
        if (!MentorsMod.Settings.Enabled || !key.Ctrl || !key.Shift) return;

        if (MentorsMod.Running?.Offer is not null)
        {
            key.Enqueue(new CloseLessonsOrder { Tick = key.Tick });
            return;
        }

        if (key.Selected.Count == 0 || Nearest(key.Snapshot, key.Selected[0]) is not { } teacher) return;

        key.Enqueue(new OpenLessonsOrder(key.Selected[0], teacher) { Tick = key.Tick });
    }

    /// <summary>The closest character the row is offered on within teaching distance of <paramref name="student"/>, or null.</summary>
    static CharacterId? Nearest(WorldSnapshot snapshot, CharacterId student)
    {
        if (snapshot.Characters.FirstOrDefault(c => c.Id == student) is not { } from) return null;

        float reach = MentorsMod.Settings.Distance;
        var near = snapshot.Characters
            .Where(c => c.Id != student && LearnMenu.CanTeach(c))
            .Select(c => (c.Id, Distance: Vector3.Distance(c.Position, from.Position)))
            .Where(c => c.Distance <= reach)
            .OrderBy(c => c.Distance)
            .ToList();
        return near.Count > 0 ? near[0].Id : null;
    }
}

/// <summary><paramref name="Student"/> asks <paramref name="Teacher"/> what it can teach; the window shows the offer.</summary>
public sealed record OpenLessonsOrder(CharacterId Student, CharacterId Teacher) : SimCommand;

/// <summary>The open offer's student buys <paramref name="Levels"/> levels of <paramref name="Stat"/>.</summary>
public sealed record BuyLessonOrder(CharacterId Student, CharacterId Teacher, StatsEnumerated Stat, int Levels) : SimCommand;

/// <summary>The training window closes.</summary>
public sealed record CloseLessonsOrder : SimCommand;
