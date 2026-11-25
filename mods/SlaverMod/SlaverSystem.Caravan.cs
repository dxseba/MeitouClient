using System.Numerics;
using Meitou.Data.Gameplay.Ai;
using Meitou.Simulation;
using Meitou.Simulation.Population;

namespace Meitou.SlaverMod;

/// <summary>
/// The slave caravan (<c>SlaveCaravan=1</c>): every <c>SlaveCaravanDays</c> days, counted from when the last one left, the Slave Traders' "SlaverMod
/// slave caravan" squad (the original's <c>6-SlaverMod.mod</c>, with its caravan slaves) appears <c>SlaveCaravanSpawnDistance</c> from the
/// player's first town, walks in, stays <c>SlaveCaravanStayHours</c> (its members kept within <c>SlaveCaravanLeash</c>), and leaves. A day before,
/// a warning. Talk to it as to any slave trader (the trade rows).
/// </summary>
sealed partial class SlaverSystem
{
    const string CaravanTemplate = "6-SlaverMod.mod", SlaveTraders = "1084-gamedata.base";

    void StepCaravan(World world)
    {
        if (!settings.SlaveCaravan || spawner is null || !world.Beat(40, 11)) return;

        var c = _state.Caravan;
        double now = world.GameHours;
        if (c.NextAt < 0) c.NextAt = now + settings.SlaveCaravanDays * 24;

        if (world.Buildings.Towns.FirstOrDefault(t => t.Owner == world.Player.Faction) is not { } town) return;

        switch (c.Phase)
        {
            case CaravanPhase.None: Await(world, c, town, now); break;
            case CaravanPhase.Coming: Come(world, c, town, now); break;
            case CaravanPhase.Staying: Stay(world, c, town, now); break;
            case CaravanPhase.Leaving when now >= c.NextAt: Gone(world, c, now); break;
        }
    }

    void Await(World world, CaravanState c, Simulation.Construction.PlayerTown town, double now)
    {
        if (!c.Warned && now >= c.NextAt - 24)
        {
            c.Warned = true;
            world.Post($"A slave caravan of the Slave Traders will visit {town.Name} in about a day.");
        }

        if (now < c.NextAt) return;

        int faction = population.Relations.IndexOf(SlaveTraders);
        var at = new Vector2(town.Position.X, town.Position.Z);
        int site = NearestSite(at);
        string tag = $"caravan-{++c.Visits}";
        int id = faction < 0 || site < 0 ? -1 : spawner!.SpawnPlatoon(world, faction, CaravanTemplate, site, at,
            settings.SlaveCaravanSpawnDistance - 200, settings.SlaveCaravanSpawnDistance + 200);
        if (id < 0 || world.Platoons.Find(id) is not { } platoon)
        {
            c.NextAt = now + 24;
            log("the slave caravan could not be placed (is SlaverMod.mod enabled?)");
            return;
        }

        platoon.ModTags[modId] = tag;
        PopulationSystem.HoldPlatoon(world, id, modId, true);
        (c.Tag, c.Phase) = (tag, CaravanPhase.Coming);
        world.Post($"A slave caravan of the Slave Traders is coming to {town.Name}.");
    }

    int NearestSite(Vector2 at)
    {
        int best = -1;
        float bestD = float.MaxValue;
        for (int s = 0; s < population.Sites.Count; s++)
        {
            var p = population.Sites[s].Position;
            float d = Vector2.DistanceSquared(at, new(p.X, p.Z));
            if (d < bestD) (best, bestD) = (s, d);
        }

        return best;
    }

    /// <summary>Off screen it travels toward the town; loaded, its members are ordered in until its leader is within the arrive distance.</summary>
    void Come(World world, CaravanState c, Simulation.Construction.PlayerTown town, double now)
    {
        if (world.Platoons.FindByTag(modId, c.Tag) is not { } platoon)
        {
            Gone(world, c, now);
            return;
        }

        if (Members(world, platoon) is not { Count: > 0 } members)
        {
            PopulationSystem.SendPlatoon(world, platoon.Id, new Vector2(town.Position.X, town.Position.Z));
            return;
        }

        if (Vector3.Distance(world.Characters.Next[members[0]].Position, town.Position) > settings.SlaveCaravanArriveDistance)
        {
            MoveAll(world, members, town.Position);
            return;
        }

        (c.Phase, c.NextAt) = (CaravanPhase.Staying, now + settings.SlaveCaravanStayHours);
        var leader = world.Characters.Cold(members[0])!;
        world.Post($"The slave caravan arrived. Talk to {leader.Name} to buy or sell slaves.");
        Say(world, world.Characters.IdOf(members[0]), "CaravanArrive");
    }

    void Stay(World world, CaravanState c, Simulation.Construction.PlayerTown town, double now)
    {
        if (world.Platoons.FindByTag(modId, c.Tag) is not { } platoon)
        {
            Gone(world, c, now);
            return;
        }

        var members = Members(world, platoon);
        var strays = members.Where(m => Vector3.Distance(world.Characters.Next[m].Position, town.Position) > settings.SlaveCaravanLeash).ToList();
        if (strays.Count > 0) MoveAll(world, strays, town.Position);

        if (now < c.NextAt) return;

        var away = town.Position + new Vector3(settings.SlaveCaravanLeash * 2, 0, 0);
        if (members.Count > 0) MoveAll(world, members, away);
        PopulationSystem.HoldPlatoon(world, platoon.Id, modId, false);
        (c.Phase, c.NextAt) = (CaravanPhase.Leaving, now + 2);
        world.Post("The slave caravan is packing up and leaving.");
    }

    /// <summary>The visit is over: the caravan is taken out of the world and the next one is counted from now.</summary>
    void Gone(World world, CaravanState c, double now)
    {
        if (world.Platoons.FindByTag(modId, c.Tag) is { } platoon) spawner!.DespawnPlatoon(world, platoon.Id);

        (c.Phase, c.NextAt, c.Warned, c.Tag) = (CaravanPhase.None, now + settings.SlaveCaravanDays * 24, false, "");
    }

    static List<int> Members(World world, Simulation.Factions.Platoon platoon)
    {
        var slots = new List<int>();
        if (world.Squads.Find(platoon.SquadId) is not { } squad) return slots;

        foreach (var m in squad.Members)
        {
            if (world.Characters.TryResolveNext(m, out int s) && world.Characters.Cold(s) is { Medical.Dead: false }) slots.Add(s);
        }

        if (squad.Leader is { IsNone: false } lead && world.Characters.TryResolveNext(lead, out int l) && slots.Remove(l)) slots.Insert(0, l);
        return slots;
    }

    static void MoveAll(World world, List<int> members, Vector3 to)
    {
        foreach (int s in members) world.GiveOrder(world.Characters.IdOf(s), TaskType.MoveCusOrdered, point: to);
    }
}
