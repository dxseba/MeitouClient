using System.Numerics;
using System.Runtime.CompilerServices;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Population;

namespace Meitou.QuestsContracts;

/// <summary>
/// The Ledger: keeps the jobs, reputation and windows, writes the variables the dialogue tests (as the original writes Extra Extensions'
/// records) at each player conversation's start and after each line, and answers the signal lines. Serial phases only: the conversation start
/// (Harmony prefix, inside the dialogue system's serial step), the signal effect and <see cref="SlowWorld"/>.
/// </summary>
sealed partial class LedgerSystem : ITickSystem, IModSaveData, IStateHashed
{
    internal const int Kinds = 14, WindowDays = 3, JobsPerWindow = 3, FailDays = 3, RepDone = 10, RepFail = 3, RepKept = 8;
    internal const int SpecialRep = 70, SpecialDays = 3, PromoDeskRep = 50, PromoThiefHeists = 3, PromoCaravanRides = 3;
    const float PickRadius = 2500, PartyRadius = 6000;

    static readonly ConditionalWeakTable<World, LedgerSystem> ByWorld = [];

    readonly LedgerTables _tables;
    readonly PopulationData _population;
    readonly RngPurpose _purpose;
    readonly Action<string> _log;
    readonly int[] _vars;
    readonly Pickpockets _pickpockets = new();
    readonly float _overlayRadius;
    LedgerState _state = new();
    int _day = -1;

    /// <summary>The values the dialogue reads, by variable index (a copy swapped whole, so the interface's previews read a settled one).</summary>
    volatile int[] _published;

    public LedgerSystem(LedgerTables tables, PopulationData population, RngPurpose purpose, Action<string> log)
    {
        _tables = tables;
        _population = population;
        _purpose = purpose;
        _log = log;
        _vars = new int[tables.Variables.Count];
        _published = new int[tables.Variables.Count];
        _overlayRadius = tables.OverlayRadius;
        Pickpockets.Open = [];
        Marks = new Dictionary<CharacterId, MarkKind>();
    }

    /// <summary>The running world's Ledger, for the interface (null before the first tick).</summary>
    internal static volatile LedgerSystem? Running;

    /// <summary>The Ledger window's copy, made in the serial step.</summary>
    internal volatile LedgerView? View;

    internal static LedgerSystem? Of(World world) => ByWorld.TryGetValue(world, out var s) ? s : null;

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        ByWorld.AddOrUpdate(world, this);
        Running = this;
        foreach (var command in commands)
        {
            if (command is PickpocketOrder pick) _pickpockets.Order(world, pick);
            else if (command is OverlayHeld held) Overlay(held);
        }
    }

    public void SlowWorld(World world)
    {
        int day = (int)Math.Floor(world.GameHours / 24);
        if (day != _day)
        {
            bool first = _day < 0;
            _day = day;
            if (!first) Expire(world);
        }

        _pickpockets.Tick(world);
        if (!world.Beat(10, 3)) return;

        Watch(world);
        RideTick(world);
        HeistTick(world);
        RescueScout(world);
        if (world.Beat(60, 11)) BountyTick(world);

        MarkTick(world);
        WriteJobVars(world);
        View = LedgerView.Of(_state, _day, Readout(), BountyLines()) with { Dots = Dots(world), Faces = Faces(world) };
    }

    /// <summary>The value the dialogue sees for variable <paramref name="index"/>.</summary>
    internal int Variable(int index) => (uint)index < (uint)_published.Length ? _published[index] : 0;

    void Set(string name, int value)
    {
        int i = _tables.VariableIndex(name);
        if (i >= 0) _vars[i] = value;
    }

    void Publish() => _published = (int[])_vars.Clone();

    #region Who and where

    Who? Describe(World world, CharacterId id)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(id, out int s) || table.Cold(s) is not { } cold) return null;

        string faction = cold.Faction >= 0 && cold.Faction < _population.Relations.Count ? _population.Relations.Factions[cold.Faction].Name : "";
        return new Who(cold.Name, TownName(table.Next[s].Position), faction);
    }

    string TownName(Vector3 at) => _population.TownAt(at) is int t and >= 0 ? _population.Sites[t].Town.Name : "";

    /// <summary>The original's town code: FNV-1a of the town's name, mod 1000000.</summary>
    internal static int TownCode(string name)
    {
        if (name.Length == 0) return 0;

        uint h = 2166136261u;
        foreach (byte b in System.Text.Encoding.UTF8.GetBytes(name))
        {
            h ^= b;
            h *= 16777619u;
        }

        return (int)(h % 1000000u);
    }

    int Roll(int n) => n <= 0 ? 0 : (int)(Rng.Hash(_state.Draws++, 0x5145u, _purpose) % (ulong)n);

    #endregion

    #region Saving and hashing

    public void Write(Stream stream) => stream.Write(_state.Serialize());

    public void Read(Stream stream)
    {
        try
        {
            _state = LedgerState.Deserialize(stream);
        }
        catch (System.Text.Json.JsonException e)
        {
            _log($"the saved Ledger could not be read, starting a new one: {e.Message}");
            _state = new LedgerState();
        }
    }

    public void Hash(ref StateHasher hasher)
    {
        hasher.Add(_state.Slots.Count);
        hasher.Add(_state.Draws);
        hasher.Add(_state.FailPending);
        foreach (var (key, value) in _state.Rep.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            hasher.Add(Rng.StableHash(key));
            hasher.Add(value);
        }
    }

    #endregion
}
