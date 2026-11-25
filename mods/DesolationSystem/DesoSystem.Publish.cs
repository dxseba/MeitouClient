using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using static Meitou.Data.Gameplay.Bodies.StatsEnumerated;

namespace Meitou.DesolationSystem;

/// <summary>The published view: squad aggregates, styles, timed states and the player characters' active perks.</summary>
sealed partial class DesoSystem
{
    void Publish(World world)
    {
        var styles = new Dictionary<CharacterId, int>();
        var humanoids = new Dictionary<int, int>();
        var chefs = new HashSet<int>();
        var wild = new HashSet<int>();
        var tuned = new HashSet<int>();
        var active = new Dictionary<CharacterId, Shown>();
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (table.Cold(slot) is not { Stats: { } stats, Medical.Dead: false } cold) continue;

            var id = table.IdOf(slot);
            int style = StyleOf(cold);
            if (style >= 0) styles[id] = style;
            Aggregate(cold, stats, humanoids, chefs, wild);
            if (Tuner(cold, stats)) tuned.Add(cold.SquadId);
            if (cold.IsPlayer) active[id] = new Shown(cold.Name, style, [.. Perks.Active(cold, _cfg, style)]);
        }

        TunedSquads = tuned;
        _view = _view with { Styles = styles, Humanoids = humanoids, ChefSquads = chefs, WildSquads = wild, Active = active };
    }

    void Aggregate(CharacterCold cold, CharacterStats stats, Dictionary<int, int> humanoids, HashSet<int> chefs, HashSet<int> wild)
    {
        if (cold.SquadId < 0) return;

        if (!Gates.IsAnimal(cold)) humanoids[cold.SquadId] = humanoids.GetValueOrDefault(cold.SquadId) + 1;
        bool scoped = Gates.InScope(cold, _cfg.ScopeIndex);
        if (scoped && _cfg.P22 && Gates.L(stats, Farming) >= _cfg.Chef && Gates.L(stats, Cooking) >= _cfg.Chef) chefs.Add(cold.SquadId);
        if (Gates.Reached(cold, _cfg, Gates.Realm.Wilderness)) wild.Add(cold.SquadId);
    }

    /// <summary>Prosthetic Tuning's tuner: medic and science at the threshold, robotics above it.</summary>
    bool Tuner(CharacterCold cold, CharacterStats s) => _cfg.P201 && cold.SquadId >= 0 && Gates.InScope(cold, _cfg.ScopeIndex)
        && Gates.L(s, Medic) >= _cfg.CyberLevel && Gates.L(s, Science) >= _cfg.CyberLevel && Gates.L(s, Robotics) > _cfg.CyberLevel;

    /// <summary>Publishes the timed states again after a hook changed them.</summary>
    void PublishTimed() => _view = _view with { Slows = new Dictionary<CharacterId, Slow>(_slows), Bursts = new Dictionary<CharacterId, long>(_bursts) };
}
