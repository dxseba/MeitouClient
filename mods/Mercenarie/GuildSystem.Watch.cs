using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Ai;

namespace Meitou.Mercenarie;

/// <summary>
/// The accepted contracts in the world: the spawned group (client, caravan, scientists, wanted men), its follow contract, the stages (outbound,
/// research at the ruin, the return leg), the report to a bartender, pay, XP, reputation and failure.
/// </summary>
sealed partial class GuildSystem
{
    const double ResearchHours = 2;

    /// <summary>
    /// The contract's group, tagged, near the giver (a bounty's near the destination); null (and a notice) when it cannot be placed.
    /// </summary>
    Contract? Spawn(World world, Contract c, CharacterId giver)
    {
        if (spawner is null || Giver(world, giver) is not { } g) return null;

        string tag = $"{c.Kind.ToString().ToLowerInvariant()}-{++_state.Spawns}";
        var (template, faction, around, min, max) = c.Kind switch
        {
            ContractKind.Bounty => (BountyTemplate(c.Group), BountyFaction(), Flat(population.Sites[c.To].Position), 1500f, 4000f),
            ContractKind.Caravan => (CaravanTemplate, g.Cold.Faction, Flat(g.At), 50f, 300f),
            ContractKind.Expedition => (ScientistTemplate, g.Cold.Faction, Flat(g.At), 50f, 300f),
            _ => (EscortTemplate(_state.Spawns), g.Cold.Faction, Flat(g.At), 50f, 300f),
        };
        int home = c.Kind == ContractKind.Bounty ? c.To : c.From;
        int cap = c.Kind == ContractKind.Caravan ? c.Group : 0;
        int id = faction < 0 ? -1 : spawner.SpawnPlatoon(world, faction, template, home, around, min, max, maxMembers: cap);
        if (id < 0 || world.Platoons.Find(id) is not { } platoon)
        {
            world.Post(context.Tr("The client could not be found. Is the original mod enabled?"));
            return null;
        }

        platoon.ModTags[context.Id] = tag;
        return c with { Tag = tag };
    }

    static string BountyTemplate(int group) => $"88160{Math.Clamp(group, 2, 6)}{Mod}";

    /// <summary>The wanted group's faction: its leader character's (510-gamedata.base, the bounty templates' leader).</summary>
    int BountyFaction() =>
        population.Db.Find("510-gamedata.base")?.GetReferences("faction").FirstOrDefault() is { TargetStringId: { } f }
            ? population.Relations.IndexOf(f)
            : -1;

    void Watch(World world, Contract c)
    {
        if (_day > c.Deadline && c.Stage < 3)
        {
            Fail(world, c, "the deadline is over");
            return;
        }

        if (c.Kind == ContractKind.Mail || c.Stage == 3) return;

        if (c.Squad < 0)
        {
            Load(world, c);
            return;
        }

        var members = world.Squads.Find(c.Squad)?.Members ?? [];
        if (c.Kind == ContractKind.Bounty) WatchBounty(world, c, members);
        else WatchClient(world, c, members);
    }

    /// <summary>The group has loaded: its members are counted, a client follows the player's leader.</summary>
    void Load(World world, Contract c)
    {
        if (world.Platoons.FindByTag(context.Id, c.Tag) is not { SquadId: >= 0 } platoon) return;

        c.Squad = platoon.SquadId;
        c.Members = world.Squads.Find(c.Squad)?.Members.Count ?? 0;
        if (c.Kind != ContractKind.Bounty) Follow(world, c);
    }

    void Follow(World world, Contract c)
    {
        if (Leader(world) is not { } leader) return;

        float hours = (c.Deadline - _day + 1) * 24;
        world.Defer(new StartContract(c.Squad, FollowPackage, hours, leader) { Tick = world.Tick + 1 });
    }

    void WatchBounty(World world, Contract c, List<CharacterId> members)
    {
        if (members.Count == 0 || members.All(m => Down(world, m)))
        {
            c.Stage = 3;
            world.Post(context.Tr("Bounty done. Report to the bartender of ") + c.FromName + ".");
        }
    }

    /// <summary>
    /// Escort: paid arriving at the destination. Caravan: the destination ends the outbound leg, the return leg ends at home. Expedition: research at
    /// the ruin for two hours, then home.
    /// </summary>
    void WatchClient(World world, Contract c, List<CharacterId> members)
    {
        if (!members.Any(m => Alive(world, m)))
        {
            Fail(world, c, "the client is dead");
            return;
        }

        int target = c.Stage == 2 ? c.From : c.To;
        bool there = members.Any(m => InSite(world, m, target));
        if (c.Stage == 0 && there) Arrive(world, c);
        else if (c.Stage == 1 && world.GameHours >= c.StageUntil) Leg(world, c, "We have gathered enough information. It is time to go back.");
        else if (c.Stage == 2 && there) Finish(world, c, members);
    }

    void Arrive(World world, Contract c)
    {
        if (c.Kind == ContractKind.Escort)
        {
            Finish(world, c, world.Squads.Find(c.Squad)?.Members ?? []);
            return;
        }

        if (c.Kind == ContractKind.Expedition)
        {
            c.Stage = 1;
            c.StageUntil = world.GameHours + ResearchHours;
            world.Post(context.Tr("Successful entry: the expedition continues its research inside."));
            return;
        }

        Leg(world, c, "Outbound leg completed: protect the caravan on the way back.");
    }

    void Leg(World world, Contract c, string notice)
    {
        c.Stage = 2;
        world.Post(context.Tr(notice));
    }

    void Finish(World world, Contract c, List<CharacterId> members)
    {
        Release(world, c);
        Pay(world, c, members.Count(m => Alive(world, m)) >= c.Members);
    }

    /// <summary>A bartender hears a delivered letter (its destination town) or a finished bounty (its issuing town).</summary>
    void Report(World world, ReportToBartender report)
    {
        if (Giver(world, report.Bartender) is not { } b) return;

        var mail = _state.Active.FirstOrDefault(c => c.Kind == ContractKind.Mail && c.To == b.Site);
        var bounty = _state.Active.FirstOrDefault(c => c.Kind == ContractKind.Bounty && c.Stage == 3 && c.From == b.Site);
        if ((mail ?? bounty) is not { } done)
        {
            world.Post(context.Tr("No contract to report in this town."));
            return;
        }

        Pay(world, done, noLosses: true);
    }

    void Pay(World world, Contract c, bool noLosses)
    {
        int paid = Pricing.Paid(c.Reward, settings.RewardPercent);
        int xp = Pricing.Xp(c.Kind, c.Km * (c.RoundTrip ? 2 : 1), c.Danger, noLosses);
        int before = _state.Level;
        world.Player.Money += paid;
        _state.Xp += xp;
        _state.Rep(c.FromName, RepSuccess);
        _state.Close(c, _day, xp, success: true);
        world.Post($"{context.Tr("CONTRACT COMPLETED")}: +{paid} Cats | {context.Tr("Guild XP")}: +{xp} | {_state.Xp} XP");
        if (_state.Level > before) world.Post($"{context.Tr("YOUR GUILD HAS REACHED LEVEL")} {_state.Level}");
    }

    void Fail(World world, Contract c, string why)
    {
        Release(world, c);
        _state.Rep(c.FromName, -RepFail);
        _state.Close(c, _day, 0, success: false);
        world.Post($"{context.Tr("Contract failed")}: {Name(c.Kind)} {context.Tr("to")} {c.ToName} ({context.Tr(why)})");
    }

    void Release(World world, Contract c)
    {
        if (c.Squad >= 0 && c.Kind != ContractKind.Bounty) world.Defer(new EndContract(c.Squad) { Tick = world.Tick + 1 });
    }

    static bool Alive(World world, CharacterId id) =>
        world.Characters.TryResolveNext(id, out int s) && world.Characters.Cold(s)?.Medical is not { Dead: true };

    static bool Down(World world, CharacterId id) =>
        !world.Characters.TryResolveNext(id, out int s) || world.Characters.Cold(s)?.Medical is { Incapacitated: true };

    bool InSite(World world, CharacterId id, int site)
    {
        if (!world.Characters.TryResolveNext(id, out int s)) return false;

        var t = population.Sites[site];
        return Vector2.Distance(Flat(world.Characters.Next[s].Position), Flat(t.Position)) <= Math.Max(t.Town.Radius, 875);
    }

    /// <summary>The player's first living selected character, else the first living player character.</summary>
    static CharacterId? Leader(World world)
    {
        var table = world.Characters;
        foreach (var id in world.Player.Selection)
        {
            if (table.TryResolveNext(id, out int s) && table.Cold(s) is { IsPlayer: true, Medical.Dead: false }) return id;
        }

        for (int i = 0; i < table.Next.Length; i++)
        {
            if (table.Cold(i) is { IsPlayer: true, Medical.Dead: false }) return table.IdOf(i);
        }

        return null;
    }

    /// <summary>The client's follow contract (vanilla "Bodyguard"; engine choice, the original drives its own escort AI).</summary>
    internal const string FollowPackage = "5090-gamedata.base";
}
