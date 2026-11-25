using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Population;

namespace Meitou.DesolationSystem;

/// <summary>
/// Sets <paramref name="Character"/>'s Ryu style (0 Gale, 1 Balance, 2 Crush; -1 none): the Style card. Applied while paused too.
/// </summary>
public sealed record SetStyle(CharacterId Character, int Style) : SimCommand, IPausedCommand;

/// <summary>
/// The perks in the tick. The settings are copied in <see cref="Inputs"/>; every <see cref="Cadence"/> ticks <see cref="SlowWorld"/> refreshes the
/// squad aggregates, stat and carry modifiers, the regeneration perks and the Harmony patches' per-body table, applies the damage the hit and
/// defence hooks queued, and publishes the view the cards read. Ryu styles are saved by lasting id; timed states (polearm slow, Lightning Counter)
/// are hashed and reset on load.
/// </summary>
sealed partial class DesoSystem(DesoSettings settings, PopulationData population, CombatSystem? combat, RngPurpose purpose)
    : IPausedCommandHandler, IStateHashed, IModSaveData
{
    internal const int Cadence = 10;

    internal const string Key = "dxseba.desolationsystem";

    DesoSettings _cfg = settings.Copy();

    /// <summary>Ryu styles by lasting id.</summary>
    readonly SortedDictionary<ulong, int> _styles = [];

    /// <summary>The view the filters running on other threads and the cards read; replaced whole.</summary>
    volatile DesoView _view = DesoView.Empty;

    internal DesoView View => _view;

    internal DesoSettings Cfg => _cfg;

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        _cfg = settings.Copy();
        Mastery.Scale = _cfg.EffectScale;
        foreach (var command in commands)
        {
            if (command is SetStyle set) Style(world, set);
            else if (command is Pickpocket pick) Pick(world, pick);
        }
    }

    void Style(World world, SetStyle set)
    {
        if (!world.Characters.TryResolveNext(set.Character, out int slot) || world.Characters.Cold(slot) is not { Stats: { } stats }) return;
        if (Gates.L(stats, Data.Gameplay.Bodies.StatsEnumerated.MeleeAttack) < 60) return;

        ulong id = world.LastingIds.GetOrGive(set.Character).Value;
        if (set.Style is >= 0 and <= 2) _styles[id] = set.Style;
        else _styles.Remove(id);

        Publish(world);
    }

    /// <summary>The character's Ryu style, -1 for none.</summary>
    internal int StyleOf(CharacterCold cold) => cold.LastingId.Value != 0 && _styles.TryGetValue(cold.LastingId.Value, out int s) ? s : -1;

    public void SlowWorld(World world)
    {
        ApplyQueued(world);
        if (world.Tick % Cadence != 0) return;

        Publish(world);
        Body(world);
    }

    public void Hash(ref StateHasher hasher)
    {
        hasher.Add(_styles.Count);
        foreach (var (id, style) in _styles)
        {
            hasher.Add((long)id);
            hasher.Add(style);
        }

        HashTimed(ref hasher);
    }

    public void Write(Stream stream)
    {
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        w.Write(1);
        w.Write(_styles.Count);
        foreach (var (id, style) in _styles)
        {
            w.Write(id);
            w.Write(style);
        }
    }

    public void Read(Stream stream)
    {
        using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        if (r.ReadInt32() != 1) return;

        int count = r.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            ulong id = r.ReadUInt64();
            _styles[id] = r.ReadInt32();
        }
    }
}
