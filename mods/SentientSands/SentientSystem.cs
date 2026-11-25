using System.Globalization;
using System.Reflection;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Dialogue;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Population;

namespace Meitou.SentientSands;

/// <summary>A line the server sends for <see cref="Speaker"/> (a bubble; empty text ends the "...").</summary>
public sealed record ChatLine(CharacterId Speaker, string Text) : SimCommand;

/// <summary>A server notice for the top of the screen.</summary>
public sealed record ChatNotice(string Text) : SimCommand;

/// <summary>
/// An action the server's reply carries for <see cref="Npc"/> toward the player's <see cref="Player"/> (<c>[ACTION: NAME: args]</c>);
/// <see cref="Holder"/> is the squad mate after the label's <c>&gt;</c> (none: the player's speaker).
/// </summary>
public sealed record ChatAction(CharacterId Npc, CharacterId Player, string Name, string Args) : SimCommand
{
    public CharacterId Holder { get; init; } = CharacterId.None;
}

/// <summary>The chat window opened on <see cref="Npc"/> (held while <see cref="Hold"/>), or opens the trade window with it.</summary>
public sealed record ChatTalk(CharacterId Npc, CharacterId Player, bool Hold, bool Trade) : SimCommand;

/// <summary>
/// The in-game side of the original's actions, in the serial step: bubbles and notices, recruiting, freeing a prisoner, hiring, cats and items both
/// ways, relations, an attack, first aid, travel and raids, the trade window, holding the NPC while the player talks, and the game events the
/// requests report.
/// </summary>
sealed partial class SentientSystem(SentientSettings settings, PopulationData population, Action<string> log) : ITickSystem
{
    static readonly MethodInfo? JoinPlayer = typeof(DialogueSystem).GetMethod("JoinPlayer", BindingFlags.Static | BindingFlags.NonPublic,
        [typeof(World), typeof(PopulationData), typeof(int)]);

    CharacterId _held = CharacterId.None;

    /// <summary>The serial step's copy for the interface.</summary>
    internal static volatile GameFacts Facts = GameFacts.Empty;

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands)
        {
            switch (command)
            {
                case ChatLine line when line.Text.Length > 0: world.Bark(line.Speaker, line.Text); break;
                case ChatNotice notice: world.Post(notice.Text); break;
                case ChatTalk talk: Talk(world, talk); break;
                case ChatAction action: Run(world, action); break;
            }
        }
    }

    public void SlowWorld(World world)
    {
        if (!world.Beat(20, 7)) return;

        Facts = FactsReader.Read(world, population);
        WatchStates(world);
        WatchNames(world);
        if (_held.IsNone || !settings.HoldInDialogue) return;

        if (world.Characters.TryResolveNext(_held, out int s) && world.Characters.Cold(s) is { InCombat: false })
            TaskStart.Stand(world.Characters, s);
    }

    void Talk(World world, ChatTalk talk)
    {
        _held = talk.Hold ? talk.Npc : CharacterId.None;
        if (talk.Trade && !GameWindows.Trade(world, talk.Player, talk.Npc)) log("the trade window could not be opened (the port needs an update)");
    }

    void Run(World world, ChatAction a)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(a.Npc, out int n) || table.Cold(n) is not { } npc) return;

        if (RunItem(world, a, npc)) return;

        switch (a.Name)
        {
            case "JOIN_PARTY": Recruit(world, n, npc, Cats(a.Args)); break;
            case "RELEASE_PRISONER" or "RELEASE": Release(world, a.Player); break;
            case "HIRE": Hire(world, npc, a, Cats(a.Args)); break;
            case "END_HIRE" or "EXPIRE_HIRE": EndHire(world, npc); break;
            case "GIVE_CATS" or "ADD_CATS": world.Player.Money += Cats(a.Args); break;
            case "TAKE_CATS": world.Player.Money = Math.Max(0, world.Player.Money - Cats(a.Args)); break;
            case "RAISE_RELATION": Relation(world, npc, Cats(a.Args)); break;
            case "ATTACK": world.Defer(new AttackOrder([a.Npc], a.Player) { Tick = world.Tick + 1, Cause = "a chat" }); break;
            case "TRADE" or "BARTER": GameWindows.Trade(world, a.Player, a.Npc); break;
            case "FIRST_AID": world.GiveOrder(a.Npc, Data.Gameplay.Ai.TaskType.FirstAidOrder, a.Player); break;
            case "TRAVEL_TO_TARGET_TOWN" or "TRAVEL_TO_TARGET_TOWN_FAST": Travel(world, a.Npc, npc, a.Args, raid: false); break;
            case "RAID_TOWN": Travel(world, a.Npc, npc, a.Args, raid: true); break;
            default: log($"action {a.Name} is not ported"); break;
        }
    }

    internal static long Cats(string args)
    {
        string first = args.Split(':', 2)[0].Trim().TrimEnd('c', 'C').Replace(",", "");
        return long.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? Math.Abs(v) : 0;
    }

    void Recruit(World world, int n, CharacterCold npc, long fee)
    {
        if (npc.IsPlayer || JoinPlayer is null) return;

        if (world.Player.Money < fee)
        {
            world.Post("Not enough cats.");
            return;
        }

        if (DialogueSystem.RecruitRefusal(world, population, world.Characters.IdOf(n)) is { } why)
        {
            world.Post(why);
            return;
        }

        if (JoinPlayer.Invoke(null, [world, population, n]) is true) world.Player.Money -= fee;
    }

    /// <summary>The player's character in a cage gets out: the cage unlocks and it is ordered out; its shackles' lock breaks (the game's DA
    /// 53).</summary>
    static void Release(World world, CharacterId prisoner)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(prisoner, out int p) || table.Cold(p) is not { } c) return;

        var spot = world.Fixtures.CageSpots(table).FirstOrDefault(s => s.Occupant == prisoner);
        if (spot is not null && world.Fixtures.Find(spot.Id) is { } cage)
        {
            world.Fixtures.SetLocked(cage, false);
            world.Defer(new GetOutOrder([prisoner]) { Tick = world.Tick + 1 });
        }

        if (LockSystem.WornShackles(c) is not null || c.SlaveState != 0) FixtureRegistry.FreeSlave(world, p, c);
    }

    /// <summary>The NPC's squad follows the player on a contract (the original's <c>Blackboard::setContractJob</c>): hours after a colon, else
    /// 24.</summary>
    void Hire(World world, CharacterCold npc, ChatAction a, long fee)
    {
        if (npc.SquadId < 0 || npc.IsPlayer) return;

        var parts = a.Args.Split(':');
        float hours = parts.Length > 1 && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float h) ? h : 24;
        world.Player.Money = Math.Max(0, world.Player.Money - fee);
        world.Defer(new StartContract(npc.SquadId, settings.HirePackage, hours, a.Player) { Tick = world.Tick + 1 });
    }

    static void EndHire(World world, CharacterCold npc)
    {
        if (npc.SquadId >= 0) world.Defer(new EndContract(npc.SquadId) { Tick = world.Tick + 1 });
    }

    void Relation(World world, CharacterCold npc, long by)
    {
        if (npc.Faction >= 0 && world.Player.Faction >= 0) population.Relations.Add(npc.Faction, world.Player.Faction, Math.Clamp(by, 0, 100));
    }

    /// <summary>
    /// The original gives the NPC task 53 (travel to a town) or 18 (raid a town) toward the town the args name (decompile). The port orders that
    /// row with the town when the game takes it as an order; else it holds the NPC's platoon, sends it there off screen and walks the NPC there.
    /// </summary>
    void Travel(World world, CharacterId id, CharacterCold npc, string town, bool raid)
    {
        var site = population.Sites.FirstOrDefault(s => string.Equals(s.Town.Name, town.Trim(), StringComparison.OrdinalIgnoreCase));
        if (site is null)
        {
            log($"no town named '{town}'");
            return;
        }

        var row = raid ? Data.Gameplay.Ai.TaskType.RaidTown : Data.Gameplay.Ai.TaskType.TravelToTargetTown;
        if (world.CanOrder(row, id, site: site.Town.Id).Can)
        {
            world.GiveOrder(id, row, site: site.Town.Id);
            return;
        }

        if (world.Squads.Find(npc.SquadId) is { PlatoonId: >= 0 } squad && PopulationSystem.HoldPlatoon(world, squad.PlatoonId, true))
            PopulationSystem.SendPlatoon(world, squad.PlatoonId, new System.Numerics.Vector2(site.Position.X, site.Position.Z));

        world.GiveOrder(id, Data.Gameplay.Ai.TaskType.MoveCusOrdered, point: site.Position);
    }
}
