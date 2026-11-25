using System.Numerics;
using Meitou.Data.Gameplay;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Population;

namespace Meitou.Mercenarie;

/// <summary>The player asks a bartender (or a Guild House client) for a contract: the board of that town is drawn and shown.</summary>
public sealed record AskBoard(CharacterId Bartender, CharacterId Player) : SimCommand;

/// <summary>The player accepts the board's offer <paramref name="Index"/>.</summary>
public sealed record AcceptOffer(int Index, CharacterId Bartender, CharacterId Player) : SimCommand;

/// <summary>The player proposes <paramref name="Percent"/> on the board's offer <paramref name="Index"/> (from guild level 2).</summary>
public sealed record NegotiateOffer(int Index, int Percent) : SimCommand;

/// <summary>The player tells a bartender a letter is delivered or a bounty is done.</summary>
public sealed record ReportToBartender(CharacterId Bartender, CharacterId Player) : SimCommand;

/// <summary>The player cancels the accepted contract <paramref name="Index"/> (a reputation loss).</summary>
public sealed record CancelContract(int Index) : SimCommand;

/// <summary>The player defines (or, with <paramref name="Abandon"/>, abandons) the building <paramref name="Building"/> as a Guild House.</summary>
public sealed record DefineGuildHouse(string Building, bool Abandon) : SimCommand;

/// <summary>The player trades whole blocks of 10000 Cats for Guild XP.</summary>
public sealed record ExchangeCats(int Blocks) : SimCommand;

/// <summary>A negotiation's state for the window: the offer, the demand, the client's answer and the attempts used (of two).</summary>
sealed record NegotiationView(int Index, int Percent, int Offered, string Answer, int Attempts);

/// <summary>What the window shows: the guild, the open board (null none), the accepted contracts, the houses and a negotiation.</summary>
sealed record GuildView(int Day, int Level, int Xp, int Prestige, IReadOnlyList<Contract>? Board, string BoardTown, IReadOnlyList<Contract> Active,
    IReadOnlyList<ClosedContract> History, IReadOnlyList<(string Town, int Rep)> Reputation, IReadOnlyList<GuildHouse> Houses,
    NegotiationView? Negotiation, IReadOnlySet<CharacterId> Clients, IReadOnlyList<Lease> Leases);

/// <summary>
/// The guild (the original's contract board and guild progress): boards drawn per town and day, accepted contracts watched in the serial step,
/// pay, XP and town reputation, negotiation, Guild Houses with walk-in clients. Serial phases only.
/// </summary>
sealed partial class GuildSystem(ModContext context, GuildSettings settings, PopulationData population, PopulationSystem? spawner)
    : ITickSystem, IModSaveData, IStateHashed
{
    const float UnitsPerKm = 1000;

    GuildState _state = new();
    List<Contract>? _board;
    string _boardTown = "";
    int _day;
    NegotiationView? _negotiation;

    internal static volatile GuildSystem? Running;
    internal volatile GuildView? View;

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        Running = this;
        _day = (int)Math.Floor(world.GameHours / 24);
        foreach (var command in commands) Handle(world, command);

        if (commands.Count > 0 || View is null) Publish(world);
    }

    void Handle(World world, SimCommand command)
    {
        switch (command)
        {
            case AskBoard ask: Ask(world, ask); break;
            case AcceptOffer accept: Accept(world, accept); break;
            case NegotiateOffer n: Negotiate(world, n); break;
            case ReportToBartender report: Report(world, report); break;
            case CancelContract cancel: Cancel(world, cancel.Index); break;
            case DefineGuildHouse house: DefineHouse(world, house); break;
            case ExchangeCats exchange: Exchange(world, exchange.Blocks); break;
            case LeaseBuilding lease when EstateAvailable: Lease(world, lease); break;
            case BuyOutLease buy: BuyOut(world, buy); break;
        }
    }

    public void SlowWorld(World world)
    {
        if (!world.Beat(20, 7)) return;

        foreach (var c in _state.Active.ToList()) Watch(world, c);
        HouseClients(world);
        if (_state.RentDay != _day && EstateAvailable)
        {
            _state.RentDay = _day;
            Rents(world);
        }

        Publish(world);
    }

    void Publish(World world) => View = new GuildView(_day, _state.Level, _state.Xp, _state.Prestige, _board is null ? null : [.. _board], _boardTown,
        [.. _state.Active], [.. _state.History], [.. _state.Reputation.Select(kv => (kv.Key, kv.Value))], [.. _state.Houses], _negotiation,
        ClientIds(world), [.. _state.Leases]);

    #region Towns and bartenders

    /// <summary>
    /// A bartender (the original's test, <c>FUN_180074380</c>): the name holds "bartender", "bar tender", "barman", "barkeeper" or "bar keeper", or
    /// the squad's template is a "big bar" or "small bar".
    /// </summary>
    internal static bool IsBartenderName(string name)
    {
        string n = name.ToLowerInvariant();
        return n.Contains("bartender") || n.Contains("bar tender") || n.Contains("barman") || n.Contains("barkeeper") || n.Contains("bar keeper");
    }

    bool IsBartender(World world, CharacterCold c)
    {
        if (IsBartenderName(c.Name)) return true;

        string template = world.Squads.Find(c.SquadId) is { } s && population.Db.Find(s.TemplateId) is { } r ? r.Name.ToLowerInvariant() : "";
        return template.Contains("big bar") || template.Contains("small bar");
    }

    /// <summary>A bartender, or the client waiting in a Guild House, with its town site.</summary>
    (int Site, CharacterCold Cold, Vector3 At)? Giver(World world, CharacterId id)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(id, out int s) || table.Cold(s) is not { IsPlayer: false } c) return null;
        if (!IsBartender(world, c) && !IsHouseClient(world, c)) return null;

        var at = table.Next[s].Position;
        int site = population.TownAt(at);
        return site < 0 ? null : (site, c, at);
    }

    static bool IsTown(TownSite s) => s.Town.Type == TownType.Town;

    float Km(int a, int b)
    {
        var (p, q) = (population.Sites[a].Position, population.Sites[b].Position);
        return Vector2.Distance(new(p.X, p.Z), new(q.X, q.Z)) / UnitsPerKm;
    }

    #endregion

    #region Saving

    public void Write(Stream stream) => stream.Write(_state.Serialize());

    public void Read(Stream stream)
    {
        try
        {
            _state = GuildState.Deserialize(stream);
        }
        catch (System.Text.Json.JsonException e)
        {
            context.Log($"the saved guild could not be read, starting a new one: {e.Message}");
            _state = new GuildState();
        }
    }

    public void Hash(ref StateHasher hasher)
    {
        hasher.Add(_state.Xp);
        hasher.Add(_state.Active.Count);
        hasher.Add(_state.Spawns);
        hasher.Add(_state.Houses.Count);
        hasher.Add(_state.Leases.Count);
    }

    #endregion
}
