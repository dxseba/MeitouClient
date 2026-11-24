using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.LevelUpNotify;

/// <summary>
/// When a player character's skill reaches a new whole level (or passes a multiple of N), a toast "Mu: Dexterity Lv.12" fades in and out in a
/// stack at a corner of the screen, as the original's.
/// </summary>
public sealed class LevelUpNotifyMod : IMod
{
    public void Initialize(ModContext context)
    {
        var settings = new LevelUpSettings();
        context.AddSettings(settings);
        context.AddSystem("levels", _ => Toasts.Watch(settings), SystemAnchor.End);
        Toasts.Install(context, settings);
        context.AddUi(new ToastUi(settings));
        context.Log("Level Up Notifications initialized");
    }
}

/// <summary>A level up: who, its toast text, and a running number the interface tells new ones by.</summary>
public readonly record struct LevelUp(CharacterId Character, string Text, long Number);

/// <summary>
/// Polls the player's characters' skills every <see cref="LevelUpSettings.PollMs"/> in the serial slow phase. A character's first sighting (spawn,
/// load, recruit) only records its levels. Level ups are presentation only, so this state is neither saved nor hashed.
/// </summary>
sealed class LevelWatchSystem(LevelUpSettings settings) : ITickSystem
{
    const int Kept = 64;
    readonly Dictionary<CharacterId, int[]> _seen = [];
    volatile LevelUp[] _recent = [];
    long _count;

    /// <summary>The latest level ups, newest last; safe to read from the interface thread.</summary>
    public LevelUp[] Recent => _recent;

    public void SlowWorld(World world)
    {
        long every = Math.Max(1, (long)MathF.Round(settings.PollMs / 1000f / world.TickSeconds));
        if (world.Tick % every != 0) return;

        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { IsPlayer: true, Stats: { } stats } cold) continue;
            Check(table.IdOf(slot), cold.Name, stats);
        }
    }

    /// <summary>
    /// The original's rule on the trained level (its <c>getStat(stat, true)</c>: the raw level without modifiers): a skill at level 1 or more that rose past a multiple of N since the last check (old / N &lt; new / N) notifies once with
    /// its level now, unless muted.
    /// </summary>
    void Check(CharacterId id, string name, Simulation.Bodies.CharacterStats stats)
    {
        bool first = !_seen.TryGetValue(id, out var levels);
        levels ??= _seen[id] = new int[Skills.Watched.Length];
        int n = Math.Max(1, settings.Every);

        for (int i = 0; i < Skills.Watched.Length; i++)
        {
            int level = (int)MathF.Floor(stats.Stored(Skills.Watched[i].Stat));
            if (level <= 0) continue;

            bool passed = !first && level > levels[i] && levels[i] / n < level / n;
            if (passed && settings.Enabled && settings.Notifies(i)) Notify(id, name, Skills.Watched[i].Name, level);
            levels[i] = level;
        }
    }

    void Notify(CharacterId id, string name, string skill, int level)
    {
        string who = name.Length > 0 ? name : "<unknown>";
        _recent = [.. _recent.TakeLast(Kept - 1), new LevelUp(id, $"{who}: {skill} Lv.{level}", ++_count)];
    }
}
