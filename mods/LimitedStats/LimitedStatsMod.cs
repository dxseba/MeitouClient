using System.Collections.Frozen;
using System.Globalization;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;

namespace Meitou.LimitedStats;

/// <summary>
/// Caps trainable stats and scales their XP: a global cap from the settings, overridden per race and per character (by name) from the mod folder's
/// <c>limits.json</c> (<see cref="Limits"/>), as the original's ini does. The author's own race values are not shipped. The XP goes through the
/// engine's experience filter (docs/modding.md "Experience").
/// </summary>
public sealed class LimitedStatsMod : IMod
{
    public void Initialize(ModContext context)
    {
        var settings = new LimitedStatsSettings();
        context.AddSettings(settings);

        var limits = Limits.Load(Path.Combine(context.Folder, "limits.json"), context.Log);
        var board = new CapBoard();
        context.AddSystem("cap", _ => NewCapSystem(settings, limits, board), SystemAnchor.End);
        context.AddUi(new CapPanelLines(settings, board));
        context.Log("Limited Stats initialized");
    }

    /// <summary>The cap system for a new world; the last world's panel data is forgotten.</summary>
    static StatCapSystem NewCapSystem(LimitedStatsSettings settings, Limits limits, CapBoard board)
    {
        board.Publish(new Dictionary<CharacterId, CapView>());
        return new StatCapSystem(settings, limits, board);
    }
}

public sealed class LimitedStatsSettings
{
    [Setting("Stat cap", Hint = "No stat trains above this level unless limits.json sets another cap for the race or character.", Min = 1, Max = 100)]
    public int Cap { get; set; } = 100;

    [Setting("Player characters only", Hint = "Off: the caps and XP multipliers apply to every character in the world.")]
    public bool PlayerOnly { get; set; } = true;

    [Setting("Panel lines", Hint = "Lines in the character panel: 1 shows the cap, 2 also the stats at their cap; 0 none.", Min = 0, Max = 2,
        Step = 1)]
    public int PanelLines { get; set; } = 1;
}

/// <summary>
/// Clamps the stats in the serial slow phase every 15th tick and publishes each character's XP multipliers there; the experience filter only reads
/// the published table, so it is a pure read on any thread (combat trains in the parallel phases). It also publishes the player characters' caps
/// for the panel lines (<see cref="CapBoard"/>).
/// </summary>
sealed class StatCapSystem(LimitedStatsSettings settings, Limits limits, CapBoard board) : IExperienceFilter
{
    static readonly StatsEnumerated[] Capped = [.. StatsInfo.RecordStats];
    static readonly int Count = Enum.GetValues<StatsEnumerated>().Max(s => (int)s) + 1;

    volatile FrozenDictionary<CharacterId, float[]> _multipliers = FrozenDictionary<CharacterId, float[]>.Empty;

    public float Gain(World world, in ExperienceGain gain)
    {
        if (!_multipliers.TryGetValue(gain.Character, out var multipliers) || (int)gain.Skill >= multipliers.Length) return gain.Amount;

        return gain.Amount * multipliers[(int)gain.Skill];
    }

    public void SlowWorld(World world)
    {
        if (world.Tick % 15 != 0) return;

        var table = world.Characters;
        var multipliers = new Dictionary<CharacterId, float[]>();
        var views = new Dictionary<CharacterId, CapView>();
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { Stats: { } stats } cold) continue;
            if (settings.PlayerOnly && !cold.IsPlayer) continue;

            var id = table.IdOf(slot);
            if (Apply(cold, stats) is { } rates) multipliers[id] = rates;
            if (cold.IsPlayer) views[id] = View(cold, stats);
        }

        _multipliers = multipliers.ToFrozenDictionary();
        board.Publish(views);
    }

    /// <summary>A capped character's lowest and highest caps and the stats that stand at theirs.</summary>
    CapView View(CharacterCold cold, CharacterStats stats)
    {
        var caps = Capped.Select(stat => CapOf(cold, stat)).ToArray();
        var atCap = Capped.Where((stat, i) => stats.Stored(stat) >= caps[i] - 0.0001f).ToArray();
        return new(caps.Min(), caps.Max(), atCap);
    }

    float CapOf(CharacterCold cold, StatsEnumerated stat) =>
        limits.Character(cold.Name)?.CapOf(stat) ?? limits.Race(cold.Race)?.CapOf(stat) ?? settings.Cap;

    /// <summary>Clamps the character's stats to its caps; its XP multipliers, or null when no override names its race or itself.</summary>
    float[]? Apply(CharacterCold cold, CharacterStats stats)
    {
        var race = limits.Race(cold.Race);
        var own = limits.Character(cold.Name);
        foreach (var stat in Capped)
        {
            float cap = own?.CapOf(stat) ?? race?.CapOf(stat) ?? settings.Cap;
            if (stats.Stored(stat) > cap) stats[stat] = cap;
        }

        if (race is null && own is null) return null;

        var multipliers = new float[Count];
        for (int i = 0; i < Count; i++) multipliers[i] = own?.XpOf((StatsEnumerated)i) ?? race?.XpOf((StatsEnumerated)i) ?? 1;
        return multipliers;
    }
}

/// <summary>A capped character's lowest and highest stat caps and the stats at their cap.</summary>
sealed record CapView(float Lowest, float Highest, StatsEnumerated[] AtCap);

/// <summary>The cap system's last pass over the player characters, published in the serial phase and read on the host thread.</summary>
sealed class CapBoard
{
    volatile IReadOnlyDictionary<CharacterId, CapView> _views = new Dictionary<CharacterId, CapView>();

    public void Publish(IReadOnlyDictionary<CharacterId, CapView> views) => _views = views;

    public CapView? Of(CharacterId id) => _views.GetValueOrDefault(id);
}

/// <summary>
/// The stat cap in the HUD's character panel (<see cref="UiRegistration.AddCharacterPanelLines"/>) for a player character: a "Stat cap" line (its
/// tooltip names the stats at their cap) and, with <see cref="LimitedStatsSettings.PanelLines"/> at 2, an "At cap" line counting them (its tooltip names them).
/// </summary>
sealed class CapPanelLines(LimitedStatsSettings settings, CapBoard board) : IUiExtension
{
    public string Id => "dxseba.limitedstats.panel";

    public void Register(UiRegistration registration) => registration.AddCharacterPanelLines(Lines);

    IEnumerable<UiCharacterLine> Lines(UiCharacterContext context)
    {
        if (settings.PanelLines <= 0 || board.Of(context.Id) is not { } view) return [];

        string atCap = view.AtCap.Length == 0 ? "none" : string.Join(", ", view.AtCap);
        var cap = new UiCharacterLine("Stat cap", Range(view), Tooltip: "At cap: " + atCap);
        if (settings.PanelLines < 2) return [cap];

        string count = view.AtCap.Length.ToString(CultureInfo.InvariantCulture);
        return [cap, new UiCharacterLine("At cap", view.AtCap.Length == 0 ? "none" : count, Tooltip: atCap)];
    }

    static string Range(CapView view) => view.Lowest == view.Highest
        ? view.Lowest.ToString("0.#", CultureInfo.InvariantCulture)
        : string.Create(CultureInfo.InvariantCulture, $"{view.Lowest:0.#}-{view.Highest:0.#}");
}
