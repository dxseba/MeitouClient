using System.Numerics;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Game.Host.Input;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.Mentors;

/// <summary>
/// Port of KENSHI: MENTORS (workshop 3834394158), lesson rules from its DLL, trades read from the installed original (<see cref="Professions"/>):
/// the player reply "Could you teach me something? [training]" opens a training window with the skills the teacher's trade covers, bought 1 or 5 lessons at a time; Ctrl+Shift with the "Mentors
/// window" key opens it on the inspected or nearest teacher, or closes it.
/// </summary>
public sealed class MentorsMod : IMod
{
    internal static readonly MentorsSettings Settings = new();

    /// <summary>The running world's system, for the window's offer; null before a world starts.</summary>
    internal static volatile MentorSystem? Running;

    /// <summary>The merged game data (record names, for the trades).</summary>
    internal static Meitou.Data.GameDatabase? Data;

    static ModContext? s_context;

    /// <summary>The text in the game's language (the original's Russian texts in a Russian game, <see cref="OriginalTexts"/>); any thread.</summary>
    internal static string Tr(string english) => s_context?.Tr(english) ?? english;

    /// <summary>The original's <c>TraceDialogue</c>: one "[trace] " line in the mod log when the setting is on.</summary>
    internal static void Trace(string text)
    {
        if (Settings.TraceDialogue) s_context?.Log("[trace] " + text);
    }

    public void Initialize(ModContext context)
    {
        s_context = context;
        Data = context.Data;
        Professions.Load(context);
        OriginalTexts.Add(context);
        context.AddSettings(Settings);
        context.AddSystem("mentors", _ => Running = new MentorSystem(Settings, context.Data), SystemAnchor.End);
        context.AddUi(new TeachReply());
        context.AddUi(new MentorWindow(context));
        context.AddKeyAction("window", "Mentors window", LessonShortcut.Pressed, Binding.Of(Key.T).With(KeyModifiers.Ctrl | KeyModifiers.Shift));
        context.Log("Mentors initialized");
    }
}

/// <summary>
/// The original's player reply "Could you teach me something? [training]" (<b>Verified</b>, DLL strings) in a conversation with a character who can
/// teach the player's character: it opens the training window and, with Close dialogue, ends the conversation.
/// </summary>
sealed class TeachReply : IUiExtension
{
    const string Text = "Could you teach me something? [training]";

    public string Id => "dxseba.mentors.reply";

    public void Register(UiRegistration registration) => registration.AddDialogueReplies(Replies);

    static IEnumerable<UiDialogueReply> Replies(UiDialogueContext talk)
    {
        if (!MentorsMod.Settings.Enabled || talk.Player is not { IsPlayer: true } student || talk.Npc is not { } teacher) return [];

        if (MentorsMod.Settings.OnlyPlayerStartedTalk && !talk.Conversation.PlayerStarted)
        {
            MentorsMod.Trace($"offer? {teacher.Name} started the talk, no training reply");
            return [];
        }

        bool teaches = Teachers.CanTeach(teacher, student);
        MentorsMod.Trace($"offer? teacher {teacher.Name}, student {student.Name}, {(teaches ? "can teach" : "nothing to teach")}");
        if (!teaches) return [];

        MentorsMod.Trace("training reply added to dialogue with " + teacher.Name);
        return [new UiDialogueReply(MentorsMod.Tr(Text), () =>
        {
            MentorsMod.Trace($"training reply clicked: teacher {teacher.Name}, student {student.Name}");
            talk.Enqueue(new OpenLessonsOrder(student.Id, teacher.Id) { Tick = talk.Tick });
            if (MentorsMod.Settings.CloseDialogue) talk.Leave();
        })];
    }
}

/// <summary>Who can teach (the original's checks, the port's trades).</summary>
static class Teachers
{
    /// <summary>
    /// A living, conscious, non-hostile non-player character that is not a slave (the original's checks), not imprisoned or carried (port), with
    /// a skill to teach the student when its stats are known.
    /// </summary>
    public static bool CanTeach(CharacterSnapshot c, CharacterSnapshot? student = null) =>
        !c.IsPlayer && !c.HostileToPlayer && !c.Imprisoned && c.Slave is not { State: 1 } && c.Body is { Dead: false, Unconscious: false }
        && c.CarriedBy == CharacterId.None && Teaches(c, student);

    /// <summary>
    /// Whether <paramref name="teacher"/> can teach the student any skill. The interface knows the record name but not the shop or NPC class, so
    /// without a student snapshot anyone eligible counts and the window says "Nothing left to teach."; with one a trade found by name is needed.
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
/// Ctrl+Shift with the "Mentors window" key (the original's Ctrl+Shift+T): closes the open window, or opens it for the first selected character
/// on the inspected character (the HUD's shown one) when it can teach, else on the nearest character within <see cref="MentorsSettings.Radius"/>
/// who can teach it something. Without a teacher the window opens with the original's note (<b>Verified</b>, DLL code and strings): no
/// selected character, the inspected NPC is no teacher or has nothing left to teach (with Require profession), or nobody near.
/// </summary>
static class LessonShortcut
{
    const string NoStudent = "Select your character and press Ctrl+Shift+T next to an NPC.";
    const string Nobody = "Nobody nearby can teach you anything. Select an NPC or come closer.";
    const string NoTrade = " is no teacher. Teachers need a fitting trade: guards teach combat, ninjas stealth and assassination, farmers farming, "
        + "miners labouring, traders attributes.";
    const string NoMore = " cannot teach you any more of their trade.";

    public static void Pressed(ModKeyContext key)
    {
        if (!MentorsMod.Settings.Enabled) return;

        if (MentorsMod.Running?.Offer is not null)
        {
            key.Enqueue(new CloseLessonsOrder { Tick = key.Tick });
            return;
        }

        if (key.Selected.Count == 0)
        {
            key.Enqueue(new OpenLessonsOrder(CharacterId.None, CharacterId.None) { Tick = key.Tick, Note = MentorsMod.Tr(NoStudent) });
            return;
        }

        var student = key.Selected[0];
        var teacher = Inspected(key) ?? Nearest(key.Snapshot, student);
        MentorsMod.Trace($"hotkey: student {student}, teacher {teacher?.ToString() ?? "none"}");
        if (teacher is { } found)
        {
            key.Enqueue(new OpenLessonsOrder(student, found) { Tick = key.Tick });
            return;
        }

        key.Enqueue(new OpenLessonsOrder(student, CharacterId.None) { Tick = key.Tick, Note = WhyNot(key) });
    }

    /// <summary>The note for a press without a teacher: about the inspected NPC when the trades decide, else "nobody nearby".</summary>
    static string WhyNot(ModKeyContext key)
    {
        string nobody = MentorsMod.Tr(Nobody);
        if (!MentorsMod.Settings.RequireProfession || key.Frame?.Shown is not { } shown) return nobody;

        if (key.Snapshot.Characters.FirstOrDefault(c => c.Id == shown) is not { IsPlayer: false } npc) return nobody;

        var field = Professions.Of(MentorsMod.Data?.Find(npc.RecordId)?.BaseName ?? "", trader: false, default);
        MentorsMod.Trace($"hotkey: no teacher, selected {npc.Name} ({field?.Name ?? "no trade"})");
        return field is null ? npc.Name + MentorsMod.Tr(NoTrade) : $"{npc.Name} ({MentorsMod.Tr(field.Name)}){MentorsMod.Tr(NoMore)}";
    }

    /// <summary>The character the HUD shows (an inspected NPC) when it can teach the first selected one.</summary>
    static CharacterId? Inspected(ModKeyContext key)
    {
        if (key.Frame?.Shown is not { } shown) return null;

        var student = key.Snapshot.Characters.FirstOrDefault(c => c.Id == key.Selected[0]);
        var teacher = key.Snapshot.Characters.FirstOrDefault(c => c.Id == shown);
        return student is not null && teacher is not null && Teachers.CanTeach(teacher, student) ? shown : null;
    }

    /// <summary>The closest character within the radius of <paramref name="student"/> who can teach it, or null.</summary>
    static CharacterId? Nearest(WorldSnapshot snapshot, CharacterId student)
    {
        if (snapshot.Characters.FirstOrDefault(c => c.Id == student) is not { } from) return null;

        float reach = MentorsMod.Settings.Radius;
        var near = snapshot.Characters
            .Where(c => c.Id != student && Teachers.CanTeach(c, from))
            .Select(c => (c.Id, Distance: Vector3.Distance(c.Position, from.Position)))
            .Where(c => c.Distance <= reach)
            .OrderBy(c => c.Distance)
            .ToList();
        return near.Count > 0 ? near[0].Id : null;
    }
}

/// <summary><paramref name="Student"/> asks <paramref name="Teacher"/> what it can teach; the window shows the offer.</summary>
public sealed record OpenLessonsOrder(CharacterId Student, CharacterId Teacher) : SimCommand
{
    /// <summary>The status line to open with (the hotkey's note when it found no teacher, <see cref="CharacterId.None"/>).</summary>
    public string Note { get; init; } = "";
}

/// <summary>The open offer's student buys <paramref name="Levels"/> levels of <paramref name="Stat"/>.</summary>
public sealed record BuyLessonOrder(CharacterId Student, CharacterId Teacher, StatsEnumerated Stat, int Levels) : SimCommand;

/// <summary>The training window closes.</summary>
public sealed record CloseLessonsOrder : SimCommand;
