using System.Numerics;
using Meitou.Data.Gameplay;
using Meitou.Simulation;
using Meitou.Simulation.Ai;

namespace Meitou.Mercenarie;

/// <summary>
/// The board: offers drawn per town and day (an escort, a mail, a caravan, an expedition to a ruin and, from guild level 1, a bounty), priced and
/// scaled by danger and distance tier, negotiated from guild level 2, accepted, cancelled.
/// </summary>
sealed partial class GuildSystem
{
    const int Nearest = 6, RepSuccess = 5, RepFail = 5, MaxActive = 3;
    const float KmPerDay = 20, NearKm = 20, MediumKm = 45, DangerCorridor = 5000;

    /// <summary>The escort clients the original draws from (civilian, merchant, soldier, family, mercenary squad).</summary>
    static readonly string[] EscortTemplates = ["880101", "880103", "880105", "880107", "880109"];

    internal const string Mod = "-Guild Escort Contracts.mod";
    internal const string CaravanTemplate = "880080" + Mod, ScientistTemplate = "880121" + Mod;

    void Ask(World world, AskBoard ask)
    {
        if (Giver(world, ask.Bartender) is not { } b) return;

        _board = Draw(world, b.Site);
        _boardTown = population.Sites[b.Site].Town.Name;
        _negotiation = null;
    }

    List<Contract> Draw(World world, int site)
    {
        var towns = population.Sites.Where(s => s.Index != site && IsTown(s)).OrderBy(s => Km(site, s.Index)).Take(Nearest).ToList();
        var ruins = population.Sites.Where(s => s.Town.Type == TownType.Ruins).OrderBy(s => Km(site, s.Index)).Take(Nearest).ToList();
        var board = new List<Contract>();
        var kinds = new List<ContractKind> { ContractKind.Escort, ContractKind.Mail, ContractKind.Caravan, ContractKind.Expedition };
        if (_state.Level >= 1) kinds.Add(ContractKind.Bounty);

        foreach (var kind in kinds)
        {
            var pool = kind == ContractKind.Expedition ? ruins : towns;
            if (pool.Count == 0) continue;

            ulong h = Roll(world, site, (int)kind);
            board.Add(Offer(world, kind, site, pool[(int)(h % (ulong)pool.Count)].Index, h >> 16));
        }

        return board;
    }

    ulong Roll(World world, int site, int k) => Rng.Hash(world.Seed ^ (ulong)_day, (ulong)(site * 8 + k), context.RngPurpose);

    Contract Offer(World world, ContractKind kind, int from, int to, ulong roll)
    {
        float km = Km(from, to);
        int tier = km < NearKm ? 0 : km < MediumKm ? 1 : 2;
        int group = GroupSize(kind, tier, roll);
        int danger = kind == ContractKind.Bounty ? Math.Clamp(group - 1, 1, 5) : Danger(world, from, to);
        bool prestigious = (int)(roll % 100) < Math.Max(0, _state.Reputation.GetValueOrDefault(population.Sites[from].Town.Name)) / 4;
        int reward = Pricing.Reward(kind, km, danger, tier, group, prestigious);
        int days = 1 + (int)MathF.Ceiling(km * (kind is ContractKind.Caravan or ContractKind.Expedition ? 2 : 1) / KmPerDay);
        return new Contract(kind, from, population.Sites[from].Town.Name, to, population.Sites[to].Town.Name, km, danger, tier, group, reward,
            _day + days);
    }

    /// <summary>
    /// The group: a caravan of 1-4 / 3-6 / 5-8 by tier (the original's draw); a bounty group of 2 to 6 (its templates); the escort's client and the
    /// scientists come as their templates plan them (1).
    /// </summary>
    static int GroupSize(ContractKind kind, int tier, ulong roll) => kind switch
    {
        ContractKind.Caravan => tier switch { 0 => 1 + (int)(roll % 4), 1 => 3 + (int)(roll % 4), _ => 5 + (int)(roll % 4) },
        ContractKind.Bounty => 2 + tier + (int)(roll % 3),
        _ => 1,
    };

    /// <summary>
    /// The route's danger, 1 to 5 (engine estimate; the original reads it from its road and region tables): one plus the sites of factions hostile to
    /// the player within 5 km of the straight route.
    /// </summary>
    int Danger(World world, int from, int to)
    {
        var (a, b) = (Flat(population.Sites[from].Position), Flat(population.Sites[to].Position));
        int hostile = 0;
        for (int s = 0; s < population.Sites.Count; s++)
        {
            int f = s < population.SiteFactions.Count ? population.SiteFactions[s] : -1;
            if (f < 0 || !population.Relations.IsHostile(f, world.Player.Faction)) continue;
            if (SegmentDistance(Flat(population.Sites[s].Position), a, b) <= DangerCorridor) hostile++;
        }

        return Math.Clamp(1 + hostile, 1, 5);
    }

    static Vector2 Flat(Vector3 p) => new(p.X, p.Z);

    static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float t = ab.LengthSquared() > 0 ? Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0, 1) : 0;
        return Vector2.Distance(p, a + t * ab);
    }

    void Accept(World world, AcceptOffer accept)
    {
        if (_board is not { } board || (uint)accept.Index >= (uint)board.Count) return;

        if (_state.Active.Count >= MaxActive)
        {
            world.Post(context.Tr("A contract is already in progress."));
            return;
        }

        var c = board[accept.Index];
        if (_negotiation is { Index: var i, Offered: > 0 } n && i == accept.Index) c = c with { Reward = n.Offered, Negotiated = n.Percent };

        if (c.Kind != ContractKind.Mail)
        {
            if (Spawn(world, c, accept.Bartender) is not { } tagged) return;

            c = tagged;
        }

        _state.Active.Add(c);
        board.RemoveAt(accept.Index);
        _negotiation = null;
        world.Post($"{context.Tr("Contract accepted")}: {Name(c.Kind)} {context.Tr("to")} {c.ToName}, {c.Reward} Cats");
    }

    /// <summary>
    /// The negotiation (from guild level 2): the demand -20..+25 %; the client's budget is hidden (engine: up to 15 % above the offer, drawn per
    /// offer and day); a demand within it is accepted, above it the client offers its budget; two attempts, then its last offer stands.
    /// </summary>
    void Negotiate(World world, NegotiateOffer n)
    {
        if (_board is not { } board || (uint)n.Index >= (uint)board.Count) return;

        if (_state.Level < 2)
        {
            world.Post(context.Tr("Negotiation locked up to level 2. You can accept the initial conditions or refuse the contract."));
            return;
        }

        var c = board[n.Index];
        int attempts = _negotiation is { Index: var i } old && i == n.Index ? old.Attempts : 0;
        if (attempts >= 2) return;

        int budget = (int)(Roll(world, c.From, 16 + n.Index) % 16);
        int percent = Math.Clamp(n.Percent, -20, 25);
        bool agreed = percent <= budget;
        int offered = Pricing.Negotiated(c.Reward, agreed ? percent : budget);
        string counter = $"{Pricing.Negotiated(c.Reward, percent)} Cats? {context.Tr("I can offer")} {offered} Cats.";
        string answer = agreed ? context.Tr("A fair price. I will remember it.") : counter;
        _negotiation = new NegotiationView(n.Index, agreed ? percent : budget, offered, answer, attempts + 1);
        if (percent < 0) _state.Rep(c.FromName, 1);
    }

    void Cancel(World world, int index)
    {
        if ((uint)index >= (uint)_state.Active.Count) return;

        var c = _state.Active[index];
        Release(world, c);
        _state.Rep(c.FromName, -RepFail);
        _state.Close(c, _day, 0, success: false);
    }

    /// <summary>The Cats to XP exchange: whole blocks of 10000 Cats, 120 Guild XP each, no reputation.</summary>
    void Exchange(World world, int blocks)
    {
        long cats = Math.Min(blocks, world.Player.Money / 10000) * 10000L;
        if (cats <= 0) return;

        world.Player.Money -= cats;
        _state.Xp += Pricing.ExchangeXp(cats);
    }

    internal string Name(ContractKind kind) => context.Tr(kind switch
    {
        ContractKind.Mail => "Message delivery",
        ContractKind.Caravan => "Caravan escort",
        ContractKind.Expedition => "Scientific expedition",
        ContractKind.Bounty => "Bounty hunt",
        _ => "Escort",
    });

    internal static string EscortTemplate(ulong spawn) => EscortTemplates[(int)(spawn % (ulong)EscortTemplates.Length)] + Mod;
}
