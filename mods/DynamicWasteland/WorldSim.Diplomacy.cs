using Meitou.Data.Gameplay;
using Meitou.Simulation;
using Meitou.Simulation.Factions;

namespace Meitou.DynamicWasteland;

/// <summary>
/// Nations, declarations of war (the original's formula and intensity tables), allies called to arms, peace with a truce, and alliances.
/// </summary>
sealed partial class WorldSim
{
    #region Intensity tables (the original's, by WarIntensity calm, balanced, aggressive, relentless)

    /// <summary>Random nation pairs looked at per step.</summary>
    static readonly int[] PairsTried = [3, 7, 12, 20];

    /// <summary>The chance (per cent) that a nation already at war, with aggression above 85, looks for another war.</summary>
    static readonly int[] SecondWar = [0, 5, 10, 25];

    /// <summary>The declaration chance's base.</summary>
    static readonly int[] DeclareBase = [-2, 2, 8, 14];

    /// <summary>War exhaustion is divided by this before it lowers the chance.</summary>
    static readonly int[] ExhaustionDivisor = [15, 20, 30, 40];

    /// <summary>Penalties: stability below 30, two wars or more, bankrupt, treasury below 5000 Cats.</summary>
    static readonly int[] UnstablePenalty = [6, 4, 2, 0], BusyPenalty = [10, 6, 3, 1], BankruptPenalty = [14, 10, 5, 2], PoorPenalty = [5, 3, 1, 0];

    /// <summary>The declaration chance's cap.</summary>
    static readonly int[] DeclareCap = [8, 20, 32, 50];

    /// <summary>The chance (per cent) that a nation at war sends an offensive in a step (port choice, by intensity).</summary>
    static readonly int[] OffensiveChance = [10, 20, 35, 50];

    #endregion

    /// <summary>Relation thresholds of the declaration (the original's -20 and -50).</summary>
    const float Grudge = -20, OldHostility = -50;

    /// <summary>Allies called to arms at most (the original's two).</summary>
    const int CallToArms = 2;

    /// <summary>
    /// Adds every faction that holds a contested town and is not the player's, not real-less and not excluded; a nation that lost its last town
    /// leaves its wars.
    /// </summary>
    void EnsureNations(World world)
    {
        var data = Pop(world)!.Data;
        var owners = new SortedSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < data.Sites.Count && i < data.SiteFactions.Count; i++)
        {
            if (!TownVersions.Contested(data.Sites[i].Town.Type) || data.FactionId(data.SiteFactions[i]) is not { } f) continue;
            if (f == FactionData.PlayerId || data.SiteFactions[i] == data.PlayerFaction) continue;
            if (data.Factions[data.SiteFactions[i]].NotReal || _s.Excluded.Contains(f)) continue;

            owners.Add(f);
        }

        foreach (var f in owners)
        {
            if (_state.Find(f) is null && !TownVersions.IsPool(f))
                _state.Nations.Add(new Nation { Faction = f, Aggression = 30 + Below(world, 41, 1), Religion = SeedFaith(world, f) });
        }

        foreach (var n in _state.Nations)
        {
            if (owners.Contains(n.Faction) || n.Pooled) continue;

            foreach (var war in _state.Wars.Where(w => w.Involves(n.Faction)).ToList()) EndWar(world, war, truce: false);
        }
    }

    #region War

    /// <summary>The original's declaration pass: random pairs, each a chance from aggression, grudges, strength and the attacker's troubles.</summary>
    void DeclareWars(World world)
    {
        int level = _s.Intensity;
        var active = _state.Nations.Where(n => TownsOf(world, n.Faction).Count > 0).ToList();
        if (active.Count < 2) return;

        for (int k = 0; k < PairsTried[level]; k++)
        {
            var a = active[Below(world, active.Count, 2)];
            var b = active[Below(world, active.Count, 3)];
            if (a == b || _state.WarOf(a.Faction, b.Faction) is not null || _state.Allied(a.Faction, b.Faction)) continue;
            if (_state.InTruce(a.Faction, b.Faction, world.GameHours)) continue;

            int wars = _state.Wars.Count(w => w.Involves(a.Faction));
            if (wars > 0 && !(a.Aggression > 85 && Chance(world, SecondWar[level], 4))) continue;

            float relation = Relations(world).Get(Index(world, a.Faction), Index(world, b.Faction));
            bool holy = HolyWar(a, b);
            if (Chance(world, DeclareChance(a, b, relation, wars, level) + (holy ? 4 : 0), 5)) Declare(world, a, b, relation, holy);
        }
    }

    /// <summary>The declaration chance in per cent (claims and religion, which this port does not model, add nothing).</summary>
    static int DeclareChance(Nation a, Nation b, float relation, int wars, int level)
    {
        int chance = DeclareBase[level] + 1 + a.Aggression / 18;
        if (relation < Grudge) chance += 5;
        if (a.Military > b.Military + 20) chance += 3;
        chance -= a.Exhaustion / ExhaustionDivisor[level];
        if (a.Stability < 30) chance -= UnstablePenalty[level];
        if (wars >= 2) chance -= BusyPenalty[level];
        if (a.BankruptDays > 0) chance -= BankruptPenalty[level];
        if (a.Treasury < 5000) chance -= PoorPenalty[level];
        return Math.Min(chance, DeclareCap[level]);
    }

    /// <summary><paramref name="a"/> declares war on <paramref name="b"/>: the factions turn hostile both ways, the news, and b's allies join.</summary>
    void Declare(World world, Nation a, Nation b, float relation, bool holy)
    {
        string reason = holy ? tr("religious war") : relation < OldHostility ? tr("old hostility")
            : a.Military > b.Military + 20 ? tr("territorial opportunism") : tr("border tension");
        StartWar(world, a.Faction, b.Faction);
        Say(world, Fmt("{0} declared war on {1} ({2}).", Name(world, a.Faction), Name(world, b.Faction), reason), popup: true);
        CallAllies(world, a, b);
    }

    /// <summary>Records the war and sets the factions' war flags both ways (relations drop to the war value).</summary>
    void StartWar(World world, string a, string b)
    {
        _state.Wars.Add(new War { A = a, B = b, Since = world.GameHours });
        var relations = Relations(world);
        int ia = Index(world, a), ib = Index(world, b);
        Quiet(() =>
        {
            PlayerRelations.DeclareWar(world, relations, ia, ib, CharacterId.None);
            PlayerRelations.DeclareWar(world, relations, ib, ia, CharacterId.None);
        });
    }

    /// <summary>The defender's allies not at war with the attacker join it (at most two), each losing 2 stability.</summary>
    void CallAllies(World world, Nation attacker, Nation defender)
    {
        var allies = _state.Alliances
            .Where(p => p.A == defender.Faction || p.B == defender.Faction)
            .Select(p => p.A == defender.Faction ? p.B : p.A)
            .Where(f => f != attacker.Faction && _state.WarOf(f, attacker.Faction) is null && _state.Find(f) is not null)
            .Take(CallToArms)
            .ToList();
        foreach (var f in allies)
        {
            var ally = _state.Find(f)!;
            StartWar(world, f, attacker.Faction);
            ally.Stability -= 2;
            attacker.Stability -= 2;
            Say(world, Fmt("{0} declares war on {1}", Name(world, f), Name(world, attacker.Faction)) + " (" +
                Fmt("alliance with {0}", Name(world, defender.Faction)) + ")", popup: true);
        }
    }

    /// <summary>Ends a war: the war flags cleared both ways, exhaustion halved, and with <paramref name="truce"/> a truce of TruceDays.</summary>
    void EndWar(World world, War war, bool truce)
    {
        _state.Wars.Remove(war);
        _state.Offensives.RemoveAll(o => war.Between(o.Attacker, o.Defender));
        var relations = Relations(world);
        int ia = Index(world, war.A), ib = Index(world, war.B);
        Quiet(() =>
        {
            PlayerRelations.EndWar(world, relations, ia, ib, CharacterId.None);
            PlayerRelations.EndWar(world, relations, ib, ia, CharacterId.None);
        });

        foreach (var n in new[] { _state.Find(war.A), _state.Find(war.B) })
        {
            if (n is not null) n.Exhaustion /= 2;
        }

        if (!truce) return;

        _state.Truces.Add(new Pact { A = war.A, B = war.B, Until = world.GameHours + _s.TruceDays * 24.0 });
        Say(world, Fmt("{0} and {1} sign peace", Name(world, war.A), Name(world, war.B)), popup: true);
    }

    #endregion

    #region Peace and alliances

    /// <summary>
    /// A war of ten days or more ends by treaty with a chance from both sides' exhaustion (port formula: the sum over 4, less 5, plus 10 when
    /// either side is bankrupt, at most 60 %).
    /// </summary>
    void MakePeace(World world)
    {
        foreach (var war in _state.Wars.ToList())
        {
            if (world.GameHours - war.Since < 240 || _state.Find(war.A) is not { } a || _state.Find(war.B) is not { } b) continue;

            int chance = (a.Exhaustion + b.Exhaustion) / 4 - 5 + (a.BankruptDays > 0 || b.BankruptDays > 0 ? 10 : 0);
            if (Chance(world, Math.Min(chance, 60), 6)) EndWar(world, war, truce: true);
        }
    }

    /// <summary>
    /// Nations at peace whose relations are allied both ways may swear mutual defence (5 % a step, port choice); an alliance whose relations
    /// fell below 20, or whose members went to war with each other, ends.
    /// </summary>
    void FormAlliances(World world)
    {
        var relations = Relations(world);
        foreach (var pact in _state.Alliances.ToList())
        {
            int ia = Index(world, pact.A), ib = Index(world, pact.B);
            if (_state.WarOf(pact.A, pact.B) is null && relations.Get(ia, ib) >= 20 && relations.Get(ib, ia) >= 20) continue;

            _state.Alliances.Remove(pact);
            Say(world, Fmt("{0} breaks the alliance with {1}", Name(world, pact.A), Name(world, pact.B)), popup: false);
        }

        var nations = _state.Nations;
        for (int i = 0; i < nations.Count; i++)
        {
            for (int j = i + 1; j < nations.Count; j++)
            {
                TryAlliance(world, relations, nations[i].Faction, nations[j].Faction);
            }
        }
    }

    void TryAlliance(World world, FactionRelations relations, string a, string b)
    {
        if (_state.Allied(a, b) || _state.WarOf(a, b) is not null) return;

        int ia = Index(world, a), ib = Index(world, b);
        if (!relations.IsAlly(ia, ib) || !relations.IsAlly(ib, ia) || !Chance(world, 5, 7)) return;

        _state.Alliances.Add(new Pact { A = a, B = b, Until = double.MaxValue });
        Say(world, Fmt("{0} and {1} forge an alliance", Name(world, a), Name(world, b)), popup: true);
    }

    #endregion
}
