using System.Numerics;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Game.Host.Input;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.Mentors;

/// <summary>
/// Port of KENSHI: MENTORS (workshop 3834394158), lesson rules from its DLL, trades the port's own: a "Learn from" row on a friendly NPC's right-click menu (in place of the
/// original's dialogue reply) opens a training window with the skills the teacher's trade covers, bought 1 or 5 lessons at a time; Ctrl+Shift with the "Mentors
/// window" key opens it on the nearest teacher, or closes it.
/// </summary>
public sealed class MentorsMod : IMod
{
    internal static readonly MentorsSettings Settings = new();

    /// <summary>The running world's system, for the window's offer; null before a world starts.</summary>
    internal static volatile MentorSystem? Running;

    /// <summary>The merged game data (record names, for the trades).</summary>
    internal static Meitou.Data.GameDatabase? Data;

    public void Initialize(ModContext context)
    {
        Data = context.Data;
        Professions.Load(context.Folder, context.Log);
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
        var student = menu.Selected.Where(id => id != target.Id).DefaultIfEmpty(CharacterId.None).First();
        if (!MentorsMod.Settings.Enabled || student == CharacterId.None) return [];

        if (!CanTeach(target)) return [];

        return [new UiMenuVerb("Learn from", () =>
        {
            menu.Enqueue(new MoveOrder([student], target.Position) { Tick = menu.Tick });
            menu.Enqueue(new OpenLessonsOrder(student, target.Id) { Tick = menu.Tick });
        })];
    }

    /// <summary>
    /// A living, conscious, non-hostile non-player character that is not a slave (the original's checks), not imprisoned or carried (port), with
    /// a skill to teach the student when its stats are known.
    /// </summary>
    public static bool CanTeach(CharacterSnapshot c, CharacterSnapshot? student = null) =>
        !c.IsPlayer && !c.HostileToPlayer && !c.Imprisoned && c.Slave is not { State: 1 } && c.Body is { Dead: false, Unconscious: false }
        && c.CarriedBy == CharacterId.None && Teaches(c, student);

    /// <summary>
    /// Whether <paramref name="teacher"/> can teach the student any skill. The interface knows the record name but not the shop or NPC class, so
    /// the menu (no student snapshot) offers the row on anyone eligible and the window says "Nothing left to teach."; the hotkey needs a trade
    /// found by name.
    /// </summary>
    static bool Teaches(CharacterSnapshot teacher, CharacterSnapshot? student)
    {
        var settings = MentorsMod.Settings;
        var field = settings.RequireProfession
            ? Professions.Of(MentorsMod.Data?.Find(teacher.RecordId)?.BaseName ?? "", trader: false, default)
            : Professions.Anyone;
        if (field is null) return student is null;
        if (student is null || teacher.AllSkills.Count == 0 || student.AllSkills.Count == 0) return true;

        return field.Skills.Any(stat =>
            LessonMath.CanTeach(settings, teacher.AllSkills.ElementAtOrDefault((int)stat), student.AllSkills.ElementAtOrDefault((int)stat)));
    }
}

/// <summary>
/// Ctrl+Shift with the "Mentors window" key (the original's Ctrl+Shift+T; unbound by default, as the game holds T): closes the open window, or
/// opens it for the first selected character on the nearest character within <see cref="MentorsSettings.Radius"/> who can teach it something (the
/// original's "nearest one who can teach"). Without both modifiers the key does nothing.
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

    /// <summary>The closest character within the radius of <paramref name="student"/> who can teach it, or null.</summary>
    static CharacterId? Nearest(WorldSnapshot snapshot, CharacterId student)
    {
        if (snapshot.Characters.FirstOrDefault(c => c.Id == student) is not { } from) return null;

        float reach = MentorsMod.Settings.Radius;
        var near = snapshot.Characters
            .Where(c => c.Id != student && LearnMenu.CanTeach(c, from))
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
