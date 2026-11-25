using System.Numerics;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Aggro;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Population;

namespace Meitou.SlaverMod;

/// <summary>The player's <see cref="Owner"/> enslaves <see cref="Target"/> (down, caged or chained).</summary>
public sealed record EnslaveOrder(CharacterId Owner, CharacterId Target) : SimCommand;

/// <summary>The player's <see cref="Owner"/> frees, punishes or rewards the slave <see cref="Slave"/>.</summary>
public sealed record SlaveOrder(CharacterId Owner, CharacterId Slave, SlaveAct Act) : SimCommand;

/// <summary>What a <see cref="SlaveOrder"/> does.</summary>
public enum SlaveAct : byte { Free, Punish, Reward, Reprogram, Cage, LetOut }

/// <summary>
/// Player-owned slaves (the original's core): enslaving puts the game's locked shackles on through its own put-shackles order, then the target joins
/// the player's slave squad as a slave of the player's faction; submission rises while watched and falls while not; low submission may run;
/// freeing keeps the loyal and sends the rest home. Serial phases only, except <see cref="Allows"/> (a pure read).
/// </summary>
sealed partial class SlaverSystem(SlaverSettings settings, PopulationData population, RngPurpose purpose, Action<string> log,
    IReadOnlyDictionary<string, RaceTrait> races, SlaveLines lines, PopulationSystem? spawner, string modId)
    : ITickSystem, IModSaveData, IStateHashed, ICombatEngageFilter
{
    /// <summary>The running world's system, for the Harmony vetoes (they read it; null before the first tick).</summary>
    internal static volatile SlaverSystem? Running;

    internal SlaverSettings Settings => settings;

    SlaverState _state = new();

    /// <summary>The submissions the interface shows (a copy swapped whole in the serial step).</summary>
    internal static volatile IReadOnlyDictionary<CharacterId, float> Shown = new Dictionary<CharacterId, float>();

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        Running = this;
        if (!settings.Enabled) return;

        foreach (var command in commands)
        {
            if (command is EnslaveOrder e) Begin(world, e);
            else if (command is SlaveOrder o) Act(world, o);
            else if (command is SlaveTradeOrder trade) OpenTrade(world, trade);
            else if (command is TradeConfirm confirm) Confirm(world, confirm);
            else if (command is BuyKitOrder kit) BuyKit(world, kit);
            else if (command is CloseTradeWindow) Trade = null;
        }
    }

    public void SlowWorld(World world)
    {
        if (!settings.Enabled) return;

        FinishPending(world);
        int hour = (int)Math.Floor(world.GameHours);
        if (hour != _state.Hour)
        {
            bool first = _state.Hour == int.MinValue;
            _state.Hour = hour;
            if (!first) Hourly(world);
            if (!first) Daily(world, hour);
        }

        StepCaravan(world);

        if (world.Beat(10, 5)) Publish(world);
    }

    #region Lookups

    bool Resolve(World world, ulong lasting, out int slot, out CharacterCold cold)
    {
        cold = null!;
        slot = -1;
        var id = world.LastingIds.Find(new LastingId(lasting));
        if (!world.Characters.TryResolveNext(id, out slot) || world.Characters.Cold(slot) is not { } c) return false;

        cold = c;
        return true;
    }

    ulong Lasting(World world, CharacterId id) => world.LastingIds.GetOrGive(id).Value;

    /// <summary>A player-owned slave: the player's character in slave state 1 owned by its own faction.</summary>
    internal static bool PlayerSlave(CharacterCold? c) => c is { IsPlayer: true, SlaveState: 1 } && c.SlaveOwner == c.Faction;

    RaceTrait Race(CharacterCold c) => c.Race is { } r && races.TryGetValue(r.StringId, out var t) ? t : RaceTrait.Default;

    float Roll() => (Rng.Hash(_state.Draws++, 0x534cu, purpose) >> 11) * (1.0f / (1UL << 53));

    int FactionIndex(string id)
    {
        var relations = population.Relations;
        for (int f = 0; f < relations.Count; f++)
        {
            if (relations.Factions[f].Id == id) return f;
        }

        return -1;
    }

    string FactionId(int f) => f >= 0 && f < population.Relations.Count ? population.Relations.Factions[f].Id : "";

    void Say(World world, CharacterId who, string key)
    {
        if (settings.Speech && lines.Pick(key, Roll()) is { } text) world.Bark(who, text);
    }

    #endregion

    #region Protection

    /// <summary>Factions not at war with the player never take on the player's slaves (the original's <c>SparedByNeutrals</c>).</summary>
    public bool Allows(World world, in EngageAsk ask)
    {
        if (!settings.Enabled || !settings.SparedByNeutrals) return true;

        var table = world.Characters;
        if (!table.TryResolveNext(ask.Target, out int t) || !PlayerSlave(table.Cold(t))) return true;
        if (!table.TryResolveNext(ask.Attacker, out int a) || table.Cold(a) is not { IsPlayer: false } attacker) return true;

        return AggroSystem.IsEnemy(population.Relations, world.Player.Faction, attacker.Faction);
    }

    #endregion

    #region Saving

    public void Write(Stream stream) => stream.Write(_state.Serialize());

    public void Read(Stream stream)
    {
        try
        {
            _state = SlaverState.Deserialize(stream);
        }
        catch (System.Text.Json.JsonException e)
        {
            log($"the saved slaves could not be read: {e.Message}");
        }
    }

    public void Hash(ref StateHasher hasher)
    {
        hasher.Add(_state.Draws);
        foreach (var (id, s) in _state.Slaves)
        {
            hasher.Add(id);
            hasher.Add(s.Submission);
        }
    }

    void Publish(World world)
    {
        var shown = new Dictionary<CharacterId, float>();
        foreach (var (id, s) in _state.Slaves)
        {
            var c = world.LastingIds.Find(new LastingId(id));
            if (!c.IsNone) shown[c] = s.Submission;
        }

        Shown = shown;
    }

    #endregion

    static float Distance(World world, int a, int b) => Vector3.Distance(world.Characters.Next[a].Position, world.Characters.Next[b].Position);
}
