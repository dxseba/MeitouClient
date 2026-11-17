using Meitou.Data.Gameplay.Bodies;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.LimitedStats;

/// <summary>Caps every trainable stat at a chosen level, for player characters or everyone.</summary>
public sealed class LimitedStatsMod : IMod
{
    public void Initialize(ModContext context)
    {
        var settings = new LimitedStatsSettings();
        context.AddSettings(settings);
        context.AddSystem("cap", _ => new StatCapSystem(settings), SystemAnchor.End);
        context.Log("Limited Stats initialized");
    }
}

public sealed class LimitedStatsSettings
{
    [Setting("Stat cap", Hint = "No stat trains above this level. Stats already above it are lowered to it.", Min = 1, Max = 100)]
    public int Cap { get; set; } = 100;

    [Setting("Player characters only", Hint = "Off: the cap applies to every character in the world.")]
    public bool PlayerOnly { get; set; } = true;
}

/// <summary>
/// Clamps the stats in the serial slow phase, once a tick: combat reads other characters' stats in its parallel phases, so they are not written there
/// (docs/modding.md "Determinism contract"). The settings are copied in the serial input step.
/// </summary>
sealed class StatCapSystem(LimitedStatsSettings settings) : ITickSystem
{
    static readonly StatsEnumerated[] Capped = [.. StatsInfo.RecordStats];
    float _cap = 100;
    bool _playerOnly = true;

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        _cap = settings.Cap;
        _playerOnly = settings.PlayerOnly;
    }

    public void SlowWorld(World world)
    {
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { Stats: { } stats } cold || (_playerOnly && !cold.IsPlayer)) continue;
            foreach (var stat in Capped)
            {
                if (stats[stat] > _cap) stats[stat] = _cap;
            }
        }
    }
}
