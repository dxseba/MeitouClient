using Meitou.Game.Host.Input;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;

namespace Meitou.ForceAwakening;

/// <summary>
/// Port of The Force Awakening (Workshop 3836973645, <c>TheForceAwakening.dll</c>, a build of the Kenshi RPG Classes engine): the Jedi, Sith, Grey
/// Jedi and Bounty Hunter paths, learned from the masters' items; The Force and Wisdom skills; Concentration, Fury and Focus; a five-skill bar
/// (G skill mode, 1-5, then click the target), a grimoire (L) with auto-cast ticks and the two specializations' passives; the skills' sounds
/// (the original's WAVs) and particles. Everything the original reads (cfg files, texts, sounds) is read from the installed original at runtime.
/// </summary>
public sealed class ForceAwakeningMod : IMod, IModDataPhase
{
    internal ForceConfig Config { get; private set; } = new();
    internal ForceSettings Settings { get; } = new();
    internal ModSkill Force { get; private set; } = null!;
    internal ModSkill Wisdom { get; private set; } = null!;
    internal ForceEffects Effects { get; private set; } = null!;
    internal ForceFx Fx { get; private set; } = null!;
    internal string ModId { get; private set; } = "";

    /// <summary>The original's <c>sounds</c> folder; null when it is not installed (the skills stay silent).</summary>
    internal string? SoundsFolder { get; private set; }

    /// <summary>The running world's system, for the interface; null before a world starts.</summary>
    internal static volatile ForceSystem? Running;

    /// <summary>The data phase: the original's records (Portuguese in its .mod) get their names and texts in the game's language.</summary>
    public void EditData(ModDataContext context) => RecordTexts.Apply(context);

    public void Initialize(ModContext context)
    {
        ModId = context.Id;
        ForceTexts.Load(context);
        Config = ForceConfig.Read(context.Folder, context.Log);
        Settings.Volume = Config.SoundVolume;
        Settings.PartyOnly = Config.Text("sound_who", "party") != "all";
        Settings.Circle = Config.Text("circle", "ground");
        if (OriginalFiles.Original(context.Folder, context.Log) is { } original && Directory.Exists(Path.Combine(original, "sounds")))
            SoundsFolder = Path.Combine(original, "sounds");

        context.AddSettings(Settings);
        Force = context.AddSkill(Skill("force.the_force", "STAT_FORCE", "The Force", "STAT_FORCE_TIP"));
        Wisdom = context.AddSkill(Skill("force.wisdom", "STAT_WIS", "Wisdom", "STAT_WIS_TIP"));
        Effects = new ForceEffects(context);
        Fx = new ForceFx(context, Settings);

        context.AddSystem("force", build => Running = new ForceSystem(this, context.Population, context.RngPurpose,
            build.Find<Meitou.Simulation.Combat.CombatSystem>(), build.Find<Meitou.Simulation.Population.PopulationSystem>(),
            build.Find<Meitou.Simulation.Aggro.AggroSystem>(), build.Find<Meitou.Simulation.Crime.CrimeSystem>()), SystemAnchor.End);
        context.AddFrame(Fx.Frame);

        var ui = new ForceUi(this, context);
        context.AddUi(ui);
        context.AddFrame(ui.Frame);
        context.AddKeyAction("skill-mode", "Force: skill mode", ui.ToggleMode, Key.G);
        context.AddKeyAction("grimoire", "Force: grimoire", ui.ToggleGrimoire, Key.L);
        ClickPatch.Install(context, ui);
        DialoguePatch.Install(context);
        BrainwashPatch.Install(context);
        context.AddCombatMove((world, id, tick) => Running?.MoveOf(world, id, tick));
        context.Log($"The Force Awakening initialized ({Config.Spells.Count} skills, sounds {(SoundsFolder is null ? "missing" : "found")})");
    }

    /// <summary>A Combat-column skill with the original's label and tooltip (its first line is the label, the rest the description).</summary>
    static ModSkill Skill(string id, string labelKey, string fallback, string tipKey)
    {
        string label = ForceTexts.Has(labelKey) ? ForceTexts.T(labelKey) : fallback;
        string tip = ForceTexts.Has(tipKey) ? ForceTexts.T(tipKey) : "";
        string description = tip.Contains("\n\n", StringComparison.Ordinal) ? tip[(tip.IndexOf("\n\n", StringComparison.Ordinal) + 2)..] : tip;
        return new ModSkill(id, label.Length is > 0 and <= 32 ? label : fallback, SkillColumn.Combat) { Description = description, Start = 1 };
    }
}

/// <summary>The options the original keeps in its grimoire and <c>user_settings.cfg</c>, under Mod settings.</summary>
public sealed class ForceSettings
{
    [Setting("Jedi and Sith in the same group", Hint = "The original's force_mixed_sides: off keeps one side of the Force per squad.")]
    public bool MixedSides { get; set; }

    [Setting("Skill sound volume", Hint = "The original's sound_volume (1 = 100 %, 0 = silent).", Min = 0, Max = 4, Step = 0.1)]
    public float Volume { get; set; } = 1;

    [Setting("Hear only my characters' skills", Hint = "The original's sound_who=party.")]
    public bool PartyOnly { get; set; } = true;

    [Setting("Casting circle", Hint = "The original's circle: on the ground, at the knees, at the waist, or hidden.",
        Options = ["ground", "knees", "waist", "off"])]
    public string Circle { get; set; } = "ground";
}

/// <summary>The data phase's renames: <c>=&lt;record&gt;|name|...</c> and its other fields (description, dialogue <c>text0</c>).</summary>
static class RecordTexts
{
    public static void Apply(ModDataContext context)
    {
        int changed = 0;
        var edits = new Dictionary<string, Meitou.Data.Fcs.FcsRecord>(StringComparer.Ordinal);
        foreach (var (id, field, text) in ForceTexts.Records(context.Folder, Localisation.Loc.Language.Code, context.Log))
        {
            if (text.Length == 0 || context.Data.Find(id) is not { } record) continue;

            string value = text.Replace("\\n", "\n", StringComparison.Ordinal);
            if (field == "name" ? record.Name == value : record.GetString(field) == value) continue;

            if (!edits.TryGetValue(id, out var edit)) edits[id] = edit = context.Edit(id);
            if (field == "name")
            {
                edit.Name = value;
                edit.Flags |= 2;   // RENAMED (docs/formats/fcs-mod.md)
            }
            else edit.Strings[field] = value;

            changed++;
        }

        if (changed > 0) context.Log($"{changed} texts of the original's records in {Localisation.Loc.Language.Code}");
    }
}
