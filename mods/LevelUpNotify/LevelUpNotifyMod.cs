using System.Text.RegularExpressions;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.LevelUpNotify;

/// <summary>
/// When a player character's stat passes a whole level: floating text over the character ("Melee Attack 12") and a notice ("Mu: Melee Attack 12"),
/// each optional, optionally only every Nth level.
/// </summary>
public sealed partial class LevelUpNotifyMod : IMod
{
    public void Initialize(ModContext context)
    {
        var settings = new LevelUpSettings();
        context.AddSettings(settings);
        context.AddSystem("levels", _ => new LevelWatchSystem(settings), SystemAnchor.End);
        FloatingText.Install(context, settings);
        context.AddUi(new FloatingTextUi());
        context.Log("Level Up Notifications initialized");
    }

    [GeneratedRegex("(?<=[a-z])(?=[A-Z])")]
    internal static partial Regex WordBreak();
}

public sealed class LevelUpSettings
{
    [Setting("Level up notices", Hint = "A notice when a squad member's stat reaches a new whole level.")]
    public bool Enabled { get; set; } = true;

    [Setting("Notify every N levels", Hint = "1: every level; 5: levels 5, 10, 15...", Min = 1, Max = 100)]
    public int Every { get; set; } = 1;

    [Setting("Floating text", Hint = "The level rises over the character's head.")]
    public bool Floating { get; set; } = true;

    [Setting("Message", Hint = "Also post the level up in the message area.")]
    public bool Message { get; set; } = true;

    [Setting("Floating text seconds", Hint = "How long the floating text stays.", Min = 1, Max = 10, Step = 0.5)]
    public float Seconds { get; set; } = 3;
}

/// <summary>A level up for the floating text: who, what, and the tick it happened.</summary>
public readonly record struct LevelUp(CharacterId Character, string Text, long Tick);

/// <summary>
/// Polls player characters' stats twice a second in the serial slow phase. A character's first sighting (spawn, load, recruit) only records its levels.
/// Notices are presentation only, so this state is neither saved nor hashed.
/// </summary>
sealed class LevelWatchSystem(LevelUpSettings settings) : ITickSystem
{
    static readonly StatsEnumerated[] Watched = [.. StatsInfo.RecordStats];
    const int Kept = 16;
    readonly Dictionary<CharacterId, int[]> _seen = [];
    volatile LevelUp[] _recent = [];

    /// <summary>The latest level ups, newest last; safe to read from the interface thread.</summary>
    public LevelUp[] Recent => _recent;

    public void SlowWorld(World world)
    {
        if (world.Tick % 15 != 0) return;

        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { IsPlayer: true, Stats: { } stats } cold) continue;
            Check(world, new(slot, table.Next[slot].Generation), cold.Name, stats);
        }
    }

    void Check(World world, CharacterId id, string name, Meitou.Simulation.Bodies.CharacterStats stats)
    {
        bool first = !_seen.TryGetValue(id, out var levels);
        levels ??= _seen[id] = new int[Watched.Length];

        for (int i = 0; i < Watched.Length; i++)
        {
            int level = (int)MathF.Floor(stats[Watched[i]]);
            if (!first && level > levels[i] && settings.Enabled && level % Math.Max(1, settings.Every) == 0) Notify(world, id, name, Watched[i], level);
            levels[i] = level;
        }
    }

    void Notify(World world, CharacterId id, string name, StatsEnumerated stat, int level)
    {
        string text = $"{Label(stat)} {level}";
        if (settings.Message) world.Post($"{name}: {text}");
        if (settings.Floating) _recent = [.. _recent.TakeLast(Kept - 1), new LevelUp(id, text, world.Tick)];
    }

    static string Label(StatsEnumerated stat) => LevelUpNotifyMod.WordBreak().Replace(stat.ToString(), " ");
}
