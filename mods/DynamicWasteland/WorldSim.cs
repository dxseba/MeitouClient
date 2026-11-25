using Meitou.Data.Gameplay;
using Meitou.Localisation;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Aggro;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Population;

namespace Meitou.DynamicWasteland;

/// <summary>
/// The political simulation (the original's world tick, every <c>TickHours</c> game hours): nations, their scores and treasuries, wars,
/// truces and alliances, offensives that march as real campaigns, conquests that change a town's owner, and the news feed. It runs in the slow
/// world step; randomness is <see cref="Rng.Hash"/> with the mod's purpose and a saved counter, so runs repeat.
/// </summary>
sealed partial class WorldSim(string modId, DwSettings settings, RngPurpose purpose, Func<string, string> tr)
    : IWorldReady, IModSaveData, IStateHashed, IRelationChangeObserver, IPlatoonObserver, Meitou.Simulation.Bodies.ICharacterDeathObserver
{
    /// <summary>Ticks between the offensive and conquest checks (about 2 s).</summary>
    const int WatchTicks = 64;

    DwState _state = new();

    /// <summary>Set while the simulation changes relations itself, so the observer does not count its own changes.</summary>
    bool _self;

    /// <summary>The panel's and the map's view; null before the first publish.</summary>
    public volatile DwView? View;

    /// <summary>The settings as copied at the last step.</summary>
    DwSettingsCopy _s = DwSettingsCopy.From(new DwSettings());

    public void Ready(World world, bool fromSave)
    {
        if (Pop(world) is not { } pop) return;

        foreach (int site in _state.Owners.Keys) pop.HoldTown(site, true);
        View = Publish(world);
    }

    public void SlowWorld(World world)
    {
        if (Pop(world) is null || !settings.Enabled) return;

        bool watch = world.Tick % WatchTicks == 0;
        bool step = world.GameHours >= _state.NextStep;
        if (!watch && !step) return;

        _s = DwSettingsCopy.From(settings);
        if (watch)
        {
            WatchOffensives(world);
            ApplyConquests(world);
            WatchCaravans(world);
            ExpireMissions(world);
            View = Publish(world);
        }

        if (world.GameHours < _state.NextStep) return;

        _state.NextStep = world.GameHours + _s.TickHours;
        Step(world);
        View = Publish(world);
    }

    /// <summary>One world step, in the original's order: nations, economy, recovery, peace, alliances, declarations, offensives.</summary>
    void Step(World world)
    {
        EnsureNations(world);
        EnsurePeople(world);
        Society(world);
        Caravans(world);
        if (_s.Economy) Economy(world);
        Recover(world);
        Politics(world);
        if (_s.NegotiatedPeace) MakePeace(world);
        if (_s.Alliances) FormAlliances(world);
        if (_s.DynamicWars) DeclareWars(world);
        LaunchOffensives(world);
        EmergentFactions(world);
        Intrigue(world);
        Missions(world);
        CareerStep(world);
        _state.Truces.RemoveAll(t => t.Until <= world.GameHours);

        int day = (int)(world.GameHours / 24);
        if (day == _state.LastDay) return;

        _state.LastDay = day;
        Religion(world);
        Crises(world);
        Mercenaries(world);
    }

    #region Platoon observer

    /// <summary>A caravan squad's arrival and a bounty's quarry beaten.</summary>
    public void PlatoonChanged(World world, in PlatoonNotice notice)
    {
        if (Pop(world) is null || !notice.Platoon.ModTags.TryGetValue(modId, out var tag)) return;

        if (notice.Kind == PlatoonEvent.Arrived && tag.StartsWith("caravan:", StringComparison.Ordinal)) CaravanArrived(world, notice.Platoon);
        else if (notice.Kind == PlatoonEvent.Removed && tag.StartsWith("mission:", StringComparison.Ordinal))
            MissionPlatoonGone(world, notice.Platoon, notice.Gone);
    }

    #endregion

    #region Relation observer

    public void RelationChanged(World world, in RelationChange change)
    {
        if (_self || Pop(world) is not { } pop) return;
        if (change.Cause.Type is not (RelationCauseType.WarDeclared or RelationCauseType.WarEnded)) return;
        if (pop.Data.FactionId(change.Faction) is not { } a || pop.Data.FactionId(change.Towards) is not { } b) return;
        if (_state.Find(a) is null || _state.Find(b) is null) return;

        if (change.Cause.Type == RelationCauseType.WarDeclared && _state.WarOf(a, b) is null)
        {
            _state.Wars.Add(new War { A = a, B = b, Since = world.GameHours });
            Say(world, Fmt("{0} declares war on {1}", Name(world, a), Name(world, b)), popup: true);
        }
        else if (change.Cause.Type == RelationCauseType.WarEnded && _state.WarOf(a, b) is { } war)
        {
            EndWar(world, war, truce: true);
        }
    }

    #endregion

    #region Save and hash

    public void Write(Stream stream)
    {
        stream.Write(_state.Bytes());
    }

    public void Read(Stream stream)
    {
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        _state = DwState.FromBytes(copy.ToArray());
    }

    public void Hash(ref StateHasher hasher) => _state.Hash(ref hasher);

    #endregion

    #region Helpers

    /// <summary>The world the systems below were found in, and its population and aggro systems.</summary>
    [ThreadStatic] static World? s_world;
    [ThreadStatic] static PopulationSystem? s_pop;
    [ThreadStatic] static AggroSystem? s_aggro;

    /// <summary>The world's population system (found once per world).</summary>
    static PopulationSystem? Pop(World world)
    {
        if (!ReferenceEquals(s_world, world))
        {
            s_world = world;
            s_pop = world.Systems.OfType<PopulationSystem>().FirstOrDefault();
            s_aggro = world.Systems.OfType<AggroSystem>().FirstOrDefault();
        }

        return s_pop;
    }

    /// <summary>The world's faction relations (the aggro system's table, else the population's).</summary>
    static FactionRelations Relations(World world)
    {
        var pop = Pop(world)!;
        return s_aggro?.Relations ?? pop.Data.Relations;
    }

    /// <summary>A faction's index in the population's factions; -1 for none.</summary>
    static int Index(World world, string faction) => Pop(world)!.Data.FactionIndex(faction);

    /// <summary>A faction's shown name.</summary>
    string Name(World world, string faction)
    {
        if (_state.Find(faction) is { Title.Length: > 0 } titled) return titled.Title;

        var data = Pop(world)!.Data;
        int i = data.FactionIndex(faction);
        return i >= 0 ? ShownText.Record(faction, data.Factions[i].Name) : faction;
    }

    /// <summary>A site's shown name.</summary>
    static string TownName(World world, int site) => Pop(world)!.Data.Sites[site].Town.ShownName();

    /// <summary>The contested sites a faction holds now, in site order.</summary>
    static List<int> TownsOf(World world, string faction)
    {
        var data = Pop(world)!.Data;
        int f = data.FactionIndex(faction);
        var towns = new List<int>();
        for (int i = 0; i < data.Sites.Count && i < data.SiteFactions.Count; i++)
        {
            if (data.SiteFactions[i] == f && TownVersions.Contested(data.Sites[i].Town.Type)) towns.Add(i);
        }

        return towns;
    }

    /// <summary>The next roll of the simulation.</summary>
    ulong Roll(World world, ulong salt) => Rng.Hash(world.Seed, _state.Rolls++, purpose, salt);

    /// <summary>A roll in [0, <paramref name="n"/>).</summary>
    int Below(World world, int n, ulong salt) => n <= 1 ? 0 : (int)(Roll(world, salt) % (ulong)n);

    /// <summary>True with a chance of <paramref name="percent"/> % (the original's: 100 or more always, 0 or less never).</summary>
    bool Chance(World world, int percent, ulong salt) => percent > 0 && (percent > 99 || Below(world, 100, salt) < percent);

    string Fmt(string english, params object[] args) => string.Format(System.Globalization.CultureInfo.InvariantCulture, tr(english), args);

    /// <summary>A news line; with <paramref name="popup"/> also a message when the news popups are on.</summary>
    void Say(World world, string text, bool popup)
    {
        _state.News.Add(new News { Hours = world.GameHours, Text = text });
        int over = _state.News.Count - _s.MaxNews;
        if (over > 0) _state.News.RemoveRange(0, over);
        if (popup && _s.NewsPopups) world.Post(text);
    }

    /// <summary>Changes relations with the observer muted.</summary>
    void Quiet(Action change)
    {
        _self = true;
        try
        {
            change();
        }
        finally
        {
            _self = false;
        }
    }

    #endregion
}

/// <summary>The settings the simulation reads, copied once a step on the tick thread.</summary>
sealed record DwSettingsCopy(bool Enabled, int TickHours, int Intensity, bool DynamicWars, bool TerritoryChanges, bool WorldBattlesOnly,
    bool Garrison, int GarrisonMax, bool NewsPopups, bool BattlePopups, int MaxNews, bool Alliances, bool NegotiatedPeace, int TruceDays,
    bool Economy, int TaxPerWorker, int TradeIncome, int SoldierWage, IReadOnlySet<string> Excluded)
{
    public static DwSettingsCopy From(DwSettings s) => new(s.Enabled, Math.Max(1, s.TickHours), s.IntensityLevel, s.DynamicWars,
        s.TerritoryChanges, s.WorldBattlesOnly, s.ConquestGarrison, s.ConquestGarrisonMax, s.NewsPopups, s.NewsPopupBattles,
        Math.Max(1, s.MaxNews), s.Alliances, s.NegotiatedPeace, Math.Clamp(s.TruceDays, 10, 365), s.Economy, s.TaxPerWorker,
        s.TradeIncomePerEconomyPoint, s.SoldierWage,
        s.ExcludedFactions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal));
}
