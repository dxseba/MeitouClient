using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;

namespace Meitou.DirectorPanel;

internal sealed partial class DirectorSystem
{
    IReadOnlySet<string> _researched = new HashSet<string>();
    long _researchVersion = -1;

    void Publish(World world)
    {
        var relations = data.Relations;
        int player = world.Player.Faction >= 0 ? world.Player.Faction : data.PlayerFaction;
        var to = new float[relations.Count];
        var from = new float[relations.Count];
        for (int f = 0; f < relations.Count && player >= 0; f++)
        {
            to[f] = relations.Get(f, player);
            from[f] = relations.Get(player, f);
        }

        _view = new DirectorView(Target(world), player, to, from, Pair(_pairA, _pairB), Pair(_pairB, _pairA), Researched(world),
            PlayerCharacters(world), world.Player.Money);
    }

    float Pair(int a, int b) => data.Relations.Contains(a) && data.Relations.Contains(b) ? data.Relations.Get(a, b) : 0;

    IReadOnlySet<string> Researched(World world)
    {
        if (world.Research.Version == _researchVersion) return _researched;

        _researchVersion = world.Research.Version;
        return _researched = new HashSet<string>(world.Research.Completed, StringComparer.Ordinal);
    }

    static List<CharacterId> PlayerCharacters(World world)
    {
        var table = world.Characters;
        var ids = new List<CharacterId>();
        for (int s = 0; s < table.HighWater; s++)
        {
            if (table.Next[s].Alive && table.Cold(s) is { IsPlayer: true }) ids.Add(table.IdOf(s));
        }

        return ids;
    }

    TargetView? Target(World world)
    {
        if (_watch.IsNone || Cold(world, _watch, out int slot) is not { } cold) return null;

        var hot = world.Characters.Next[slot];
        var stats = new float[(int)StatsEnumerated.End];
        for (var s = StatsEnumerated.None + 1; s < StatsEnumerated.End; s++) stats[(int)s] = cold.Stats?.Stored(s) ?? 0;

        var medical = cold.Medical;
        var squad = world.Squads.Find(cold.SquadId);
        string[] packages = squad?.Ai is { } board ? [.. board.Packages.Select(p => p.Package.Name)] : [];
        return new TargetView(_watch, cold.Name, hot.Position, hot.Yaw, cold.Faction, cold.SquadId, cold.IsPlayer, cold.Animal is not null,
            cold.Age, stats, Parts(medical), (medical?.Hunger ?? 0) * 100, medical?.Unconscious == true, medical?.Dead == true,
            cold.Bounties?.Total ?? 0, packages, _saved.ContainsKey(cold.SquadId));
    }

    static List<PartView> Parts(MedicalState? medical)
    {
        if (medical is null) return [];

        return [.. medical.Parts.Select(p => new PartView(p.Template.StringId, p.Template.Name, p.Flesh, p.MaxHp, p.Limb,
            p.Replacement is not null, p.Template.Type is BodyPartType.Arm or BodyPartType.Leg))];
    }
}
