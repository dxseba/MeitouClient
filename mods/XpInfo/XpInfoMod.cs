using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Ui;
using Meitou.Game.Host;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.XpInfo;

/// <summary>
/// Port of XP Info (workshop 3795213616, GPLv3): rising text over a player character as it gains experience ("+3% Athletics", several skills
/// merged into one label) and when a skill levels ("Athletics 24"), and XP bars of the skills it is training in the character panel.
/// </summary>
public sealed class XpInfoMod : IMod
{
    public void Initialize(ModContext context)
    {
        var settings = new XpSettings();
        context.AddSettings(settings);
        var board = new XpBoard();
        context.AddSystem("xp", _ => NewWatch(settings, board), SystemAnchor.End);
        XpFloat.Install(context, settings);
        context.AddUi(new XpFloatUi());
        context.AddUi(new XpPanelLines(settings, board));
        context.Log("XP Info initialized");
    }

    /// <summary>A watch for a new world; the last world's progress is forgotten.</summary>
    static XpWatchSystem NewWatch(XpSettings settings, XpBoard board)
    {
        board.Clear();
        return new XpWatchSystem(settings, board);
    }

    internal static string Label(StatsEnumerated stat) => stat switch
    {
        StatsEnumerated.Thieving => "Thievery",
        StatsEnumerated.FriendlyFire => "Precision Shooting",
        StatsEnumerated.SmithingWeapon => "Weapon Smith",
        StatsEnumerated.SmithingArmour => "Armour Smith",
        StatsEnumerated.SmithingBow => "Bow Smith",
        StatsEnumerated.Medic => "Field Medic",
        _ => Spaced(stat.ToString()),
    };

    static string Spaced(string name)
    {
        var text = new StringBuilder();
        foreach (char c in name)
        {
            if (char.IsUpper(c) && text.Length > 0) text.Append(' ');
            text.Append(c);
        }

        return text.ToString();
    }
}

/// <summary>
/// The original's <c>xp_info.ini</c> keys, labels (its settings window) and shipped defaults; <see cref="Seconds"/> is Meitou's.
/// </summary>
public sealed class XpSettings
{
    [Setting("Enable XP Info", Hint = "Rising text over your characters as they gain experience, and XP bars in the character panel.")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// <c>floatingLabels</c>, 1.
    /// </summary>
    [Setting("XP gain text", Hint = "Show '+N% Skill' as experience comes in.")]
    public bool Gains { get; set; } = true;

    /// <summary>
    /// <c>levelUpLabels</c>, 1.
    /// </summary>
    [Setting("Level-up text", Hint = "Show 'Skill N' when a skill levels.")]
    public bool Levels { get; set; } = true;

    /// <summary>
    /// <c>minPercent</c>, 2.00.
    /// </summary>
    [Setting("Announce gains over %", Hint = "Gains below this share of a level are summed until they reach it.", Min = 0.1, Max = 20, Step = 0.1)]
    public float MinPercent { get; set; } = 2;

    /// <summary>
    /// <c>announceEverySeconds</c>, 1.0 (game seconds here).
    /// </summary>
    [Setting("Announce at most every (s)", Hint = "A skill's gain is announced at most this often.", Min = 0, Max = 30, Step = 0.5)]
    public float AnnounceSeconds { get; set; } = 1;

    /// <summary>
    /// Meitou's: how long a label rises.
    /// </summary>
    [Setting("Seconds", Hint = "How long a label stays.", Min = 1, Max = 10, Step = 0.5)]
    public float Seconds { get; set; } = 2.5f;

    /// <summary>
    /// <c>panelBars</c>, 1.
    /// </summary>
    [Setting("XP bars in panel", Hint = "The character panel lists the skills the character is training, with a bar of each level's progress.")]
    public bool PanelBars { get; set; } = true;

    /// <summary>
    /// <c>maxRows</c>, 2.
    /// </summary>
    [Setting("Most bars at once", Hint = "At most this many skills, those that gained XP most recently.", Min = 1, Max = 4, Step = 1)]
    public int PanelLines { get; set; } = 2;

    /// <summary>
    /// <c>recentSeconds</c>, 120 (game seconds here).
    /// </summary>
    [Setting("Keep a bar for (s)", Hint = "A skill's bar stays this long after its last XP gain.", Min = 5, Max = 3600, Step = 5)]
    public int RecentSeconds { get; set; } = 120;
}

/// <summary>A label for the floating text: who, the text, the tick.</summary>
public readonly record struct XpLabel(CharacterId Character, string Text, long Tick, bool Level);

/// <summary>
/// Polls the player characters' stored stats every 10th tick in the serial slow phase: a rise is summed per stat until it reaches
/// <see cref="XpSettings.MinPercent"/> of a level, then shown merged with the character's other gains of that poll; a new whole level shows on its
/// own. A first sighting only records. Each poll also publishes the levels and last gains to the <see cref="XpBoard"/>. Presentation only: not
/// saved, not hashed.
/// </summary>
sealed class XpWatchSystem(XpSettings settings, XpBoard board) : ITickSystem
{
    static readonly StatsEnumerated[] Watched = XpBoard.Watched;
    const int Kept = 24;

    readonly Dictionary<CharacterId, float[]> _seen = [];
    readonly Dictionary<CharacterId, float[]> _pending = [];
    readonly Dictionary<CharacterId, long[]> _lastGain = [];
    readonly Dictionary<CharacterId, long[]> _lastShown = [];
    volatile XpLabel[] _recent = [];

    public XpLabel[] Recent => _recent;

    public void SlowWorld(World world)
    {
        if (world.Tick % 10 != 0 || !settings.Enabled) return;

        var labels = new List<XpLabel>();
        var shown = new Dictionary<CharacterId, XpProgress>();
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { IsPlayer: true, Stats: { } stats }) continue;

            var id = table.IdOf(slot);
            Check(world, id, stats, labels);
            shown[id] = new([.. _seen[id]], [.. _lastGain[id]], world.Tick, world.TickSeconds);
        }

        if (labels.Count > 0) _recent = [.. _recent.Concat(labels).TakeLast(Kept)];
        board.Publish(shown);
    }

    void Check(World world, CharacterId id, Simulation.Bodies.CharacterStats stats, List<XpLabel> labels)
    {
        var now = Watched.Select(stats.Stored).ToArray();
        if (!_seen.TryGetValue(id, out var before))
        {
            _seen[id] = now;
            _pending[id] = new float[now.Length];
            _lastGain[id] = new long[now.Length];
            _lastShown[id] = new long[now.Length];
            return;
        }

        var pending = _pending[id];
        var lastGain = _lastGain[id];
        var lastShown = _lastShown[id];
        long every = (long)(settings.AnnounceSeconds / world.TickSeconds);
        var gains = new List<string>();
        for (int i = 0; i < now.Length; i++)
        {
            float delta = now[i] - before[i];
            if (delta <= 0) continue;

            lastGain[i] = world.Tick;
            pending[i] += delta * 100;
            string label = XpInfoMod.Label(Watched[i]);

            // A level is announced at once, in place of the skill's gain (the original's per-skill entry).
            if ((int)now[i] > (int)before[i])
            {
                if (settings.Levels) labels.Add(new(id, string.Create(CultureInfo.InvariantCulture, $"{label} {(int)now[i]}"), world.Tick, true));
                (pending[i], lastShown[i]) = (0, world.Tick);
                continue;
            }

            if (pending[i] < settings.MinPercent || world.Tick - lastShown[i] < every) continue;

            gains.Add(string.Create(CultureInfo.InvariantCulture, $"+{pending[i]:0}% {label}"));
            (pending[i], lastShown[i]) = (0, world.Tick);
        }

        _seen[id] = now;
        if (settings.Gains && gains.Count > 0) labels.Add(new(id, string.Join("  ", gains), world.Tick, false));
    }
}

/// <summary>A player character's stored stats and the tick each last gained XP (0: not since first seen), by <see cref="XpBoard.Watched"/>.</summary>
sealed record XpProgress(float[] Levels, long[] LastGain, long Tick, float TickSeconds);

/// <summary>
/// The watch system's last poll, published in the serial phase and read on the host thread by the panel lines.
/// </summary>
sealed class XpBoard
{
    public static readonly StatsEnumerated[] Watched = [.. StatsInfo.RecordStats];

    volatile IReadOnlyDictionary<CharacterId, XpProgress> _progress = new Dictionary<CharacterId, XpProgress>();

    public void Publish(IReadOnlyDictionary<CharacterId, XpProgress> progress) => _progress = progress;

    public void Clear() => _progress = new Dictionary<CharacterId, XpProgress>();

    public XpProgress? Of(CharacterId id) => _progress.GetValueOrDefault(id);
}

/// <summary>
/// Projects the recent labels over their characters, rising as they age (a postfix on <c>PlayerInterface.BindUi</c>, as Level Up Notify).
/// </summary>
static class XpFloat
{
    public const string Key = "xpinfo.float.";
    public const int Slots = 10;
    const float Head = 24, Rise = 10, LevelGap = 4;

    static XpSettings s_settings = new();
    static FieldInfo? s_sessionField;
    static MethodInfo? s_project;

    public static void Install(ModContext context, XpSettings settings)
    {
        s_settings = settings;
        var type = AccessTools.TypeByName("Meitou.Game.PlayerInterface") ?? throw new TypeLoadException("Meitou.Game.PlayerInterface");
        s_sessionField = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(f => f.FieldType == typeof(WorldSession));
        s_project = AccessTools.Method(type, "Project", [typeof(Vector3)]) ?? throw new MissingMethodException(type.FullName, "Project");
        var bind = AccessTools.Method(type, "BindUi") ?? throw new MissingMethodException(type.FullName, "BindUi");
        context.Harmony.Patch(bind, postfix: new HarmonyMethod(typeof(XpFloat), nameof(Bound)));
    }

    #pragma warning disable IDE1006 // Harmony binds __result / __instance by name
    static void Bound(object __instance, UiBindings __result)
    #pragma warning restore IDE1006
    {
        if (!s_settings.Enabled || s_sessionField?.GetValue(__instance) is not WorldSession session) return;
        if (session.World.Systems.OfType<XpWatchSystem>().FirstOrDefault() is not { } watch) return;

        float life = s_settings.Seconds / session.World.TickSeconds;
        int slot = 0;
        foreach (var label in watch.Recent.Reverse())
        {
            float age = (session.World.Tick - label.Tick) / life;
            if (age is < 0 or > 1 || slot >= Slots) continue;
            if (Screen(__instance, session, label, age) is not { } at) continue;

            __result.Text[Key + slot++] = string.Create(CultureInfo.InvariantCulture, $"{at.X:0}|{at.Y:0}|{(label.Level ? 1 : 0)}|{label.Text}");
        }
    }

    static Vector2? Screen(object player, WorldSession session, XpLabel label, float age)
    {
        var character = session.CurrentSnapshot.Characters.FirstOrDefault(c => c.Id == label.Character);
        if (character is null) return null;

        var head = character.Position + new Vector3(0, Head + (label.Level ? LevelGap : 0) + Rise * age, 0);
        return (Vector2?)s_project!.Invoke(player, [head]);
    }
}

/// <summary>Draws the bound labels: gains in pale green, levels in gold.</summary>
public sealed class XpFloatUi : IUiExtension
{
    const float Width = 320, Height = 22;

    public string Id => "dxseba.xpinfo.float";

    public void Register(UiRegistration registration)
    {
        string gain = registration.RegisterStyle("gain", new() { Text = new(.70f, .95f, .65f, 1), TextPadding = new(0, 0, 0, 0) });
        string level = registration.RegisterStyle("level", new() { Text = new(.98f, .85f, .35f, 1), TextPadding = new(0, 0, 0, 0) });
        registration.Contribute(UiSlots.Overlay, frame => Build(frame, gain, level), order: 42);
    }

    static UiNode Build(UiFrameContext frame, string gain, string level)
    {
        var root = new UiNode("xpinfo-float") { Placement = UiPlacement.Fill, Pickable = false };
        for (int i = 0; i < XpFloat.Slots; i++)
        {
            var parts = frame.Text(XpFloat.Key + i).Split('|', 4);
            if (parts.Length != 4 || !float.TryParse(parts[0], CultureInfo.InvariantCulture, out float x)
                || !float.TryParse(parts[1], CultureInfo.InvariantCulture, out float y))
                continue;

            var placement = new UiPlacement(Width, Height, Left: x - Width / 2, Top: y - Height / 2);
            string style = parts[2] == "1" ? level : gain;
            root.Children.Add(new("xpinfo-float-" + i, UiNodeKind.Label) { Placement = placement, Text = parts[3], Style = style, Pickable = false });
        }

        return root;
    }
}

/// <summary>
/// XP bars in the HUD's character panel (<see cref="UiRegistration.AddCharacterPanelLines"/>): for a player character, the
/// <see cref="XpSettings.PanelLines"/> skills that gained XP most recently, each its level and its progress to the next.
/// </summary>
sealed class XpPanelLines(XpSettings settings, XpBoard board) : IUiExtension
{
    public string Id => "dxseba.xpinfo.panel";

    public void Register(UiRegistration registration) => registration.AddCharacterPanelLines(Lines);

    IEnumerable<UiCharacterLine> Lines(UiCharacterContext context)
    {
        if (!settings.Enabled || !settings.PanelBars || board.Of(context.Id) is not { } progress) return [];

        long since = progress.Tick - (long)(settings.RecentSeconds / progress.TickSeconds);
        return Enumerable.Range(0, progress.Levels.Length)
            .Where(i => progress.LastGain[i] > 0 && progress.LastGain[i] >= since)
            .OrderByDescending(i => progress.LastGain[i])
            .Take(settings.PanelLines)
            .Select(i => Line(XpBoard.Watched[i], progress.Levels[i]));
    }

    static UiCharacterLine Line(StatsEnumerated stat, float level)
    {
        float part = level - MathF.Floor(level);
        string label = XpInfoMod.Label(stat);
        string value = string.Create(CultureInfo.InvariantCulture, $"{(int)level}  {part * 100:0}%");
        string tooltip = string.Create(CultureInfo.InvariantCulture, $"{label}: level {(int)level}, {part * 100:0.#}% of the way to the next");
        return new(label, value, part, tooltip);
    }
}
