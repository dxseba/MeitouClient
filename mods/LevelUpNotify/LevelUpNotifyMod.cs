using System.Text.RegularExpressions;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.LevelUpNotify;

/// <summary>Posts a notice when a player character's stat passes a whole level ("Mu: Melee Attack 12").</summary>
public sealed partial class LevelUpNotifyMod : IMod
{
    public void Initialize(ModContext context)
    {
        var settings = new LevelUpSettings();
        context.AddSettings(settings);
        context.AddSystem("levels", _ => new LevelWatchSystem(settings), SystemAnchor.End);
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
}

/// <summary>
/// Polls player characters' stats twice a second in the serial slow phase. A character's first sighting (spawn, load, recruit) only records its levels.
/// Notices are presentation only, so this state is neither saved nor hashed.
/// </summary>
sealed class LevelWatchSystem(LevelUpSettings settings) : ITickSystem
{
    static readonly StatsEnumerated[] Watched = [.. StatsInfo.RecordStats];
    readonly Dictionary<CharacterId, int[]> _seen = [];

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
            if (!first && level > levels[i] && settings.Enabled && level % Math.Max(1, settings.Every) == 0)
                world.Post($"{name}: {Label(Watched[i])} {level}");
            levels[i] = level;
        }
    }

    static string Label(StatsEnumerated stat) => LevelUpNotifyMod.WordBreak().Replace(stat.ToString(), " ");
}
