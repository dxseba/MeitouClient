using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Crime;
using Meitou.Simulation.Population;

namespace Meitou.DirectorPanel;

/// <summary>
/// The panel's world side: its commands in <see cref="Inputs"/> (a paused-command handler, so they apply at once while the game is paused, as the
/// original's calls on the game's thread do), then the view of the watched character; every <see cref="Cadence"/> ticks the view again. The
/// packages a squad had before the panel changed them are kept for "Restore normal behaviour" (not saved: a load forgets them).
/// </summary>
internal sealed partial class DirectorSystem(PopulationData data, PopulationSystem? population, CrimeSystem? crime)
    : IPausedCommandHandler, IStateHashed
{
    const int Cadence = 10;

    /// <summary>A new world starts with the panel's simulation switches off (they are statics the patches read).</summary>
    readonly bool _reset = ResetSwitches();

    static bool ResetSwitches()
    {
        Patches.NoTheft = false;
        Patches.OpenTargets = [];
        return true;
    }

    CharacterId _watch = CharacterId.None;
    int _pairA = -1, _pairB = -1;
    volatile DirectorView? _view;

    internal DirectorView? View => _view;

    internal Action<string>? Log { get; init; }

    /// <summary>A line on the panel's status bar, the result of the last command (the original's status texts).</summary>
    internal volatile StatusLine? Status;

    void Say(string format, params object[] args) => Status = new StatusLine(format, args);

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        bool any = false;
        foreach (var command in commands)
        {
            if (command is not DirectorCommand mine) continue;

            any = true;
            Apply(world, mine);
        }

        if (any) Publish(world);
    }

    public void SlowWorld(World world)
    {
        KeepWeaponsReady(world);
        if (world.Tick % Cadence == 0) Publish(world);
    }

    void Apply(World world, DirectorCommand command)
    {
        switch (command)
        {
            case Watch watch:
                (_watch, _pairA, _pairB) = (watch.Target, watch.PairA, watch.PairB);
                break;
            case AddMoney money:
                world.Player.Money = Math.Max(0, world.Player.Money + money.Amount);
                Say("Added: %ld", money.Amount);
                break;
            case SetStats stats:
                ApplyStats(world, stats);
                break;
            default:
                if (!ApplyExtras(world, command)) ApplyWorld(world, command);
                break;
        }
    }

    void ApplyStats(World world, SetStats set)
    {
        int changed = 0;
        foreach (var id in set.Characters)
        {
            if (!world.Characters.TryResolveNext(id, out int slot) || world.Characters.Cold(slot)?.Stats is not { } stats) continue;

            foreach (var stat in set.Stats)
            {
                stats[stat] = set.Value;
                changed++;
            }
        }

        if (changed == 0) Say("The selected character has no stats.");
        else Say("Stats changed: %d", changed);
    }

    /// <summary>The cold state of a living or dead character still in the table.</summary>
    static CharacterCold? Cold(World world, CharacterId id, out int slot) =>
        world.Characters.TryResolveNext(id, out slot) ? world.Characters.Cold(slot) : null;

    /// <summary>The medical context a hit on the character is worked out in (as the game's debug cut builds it).</summary>
    MedicalContext Context(World world, int slot, CharacterCold cold, RaceData race) =>
        new(data.Bodies.Constants, data.Bodies.Options, race)
        {
            Toughness = cold.Stats?.ScaledToughness ?? 0,
            Strength = cold.Stats?.Strength ?? 50,
            HasStats = cold.Stats is not null,
            Seed = world.Seed,
            CharacterKey = Rng.Key(world.Characters.IdOf(slot)),
        };

    public void Hash(ref StateHasher hasher)
    {
        hasher.Add(_watch.Slot);
        hasher.Add(_watch.Generation);
        hasher.Add(_saved.Count);
        hasher.Add(_spawned);
        hasher.Add(_keepReady.Count);
        hasher.Add(_bloodbath);
        hasher.Add(Patches.NoTheft);
        hasher.Add(Patches.OpenTargets.Count);
    }
}
