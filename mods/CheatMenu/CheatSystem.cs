using System.Numerics;
using Meitou.Data.Gameplay;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Population;
using Meitou.Simulation.Trading;

namespace Meitou.CheatMenu;

/// <summary>
/// The cheats in the tick: the window's commands in the serial <see cref="Inputs"/> (also while paused, <see cref="IPausedCommandHandler"/>), the
/// on/off cheats' upkeep in <see cref="SlowWorld"/>, and the multipliers as the game's filters (damage, experience, hunger, prices). The state is
/// per world, hashed and not saved (the original's was the process's, not saved either). The Harmony patches read it through
/// <see cref="Of"/>.
/// </summary>
internal sealed partial class CheatSystem(PopulationData data, GameRules rules)
    : IPausedCommandHandler, ICombatHitFilter, IExperienceFilter, INeedRateFilter, ITradePriceFilter, IStateHashed
{
    /// <summary>The original's Strong NPCs and Fast NPCs factors.</summary>
    internal const float StrongNpcDamage = 10, FastNpcSpeed = 3;

    readonly bool[] _on = new bool[Enum.GetValues<Toggle>().Length];
    readonly int[] _tier = new int[Tiers.Count];
    readonly SortedSet<CharacterId> _fedOnly = new(Comparer<CharacterId>.Create(ById));

    static int ById(CharacterId a, CharacterId b) => a.Slot != b.Slot ? a.Slot.CompareTo(b.Slot) : a.Generation.CompareTo(b.Generation);
    Vector3 _camera;

    /// <summary>The last world asked and its system (one reference, so threads read a consistent pair).</summary>
    static volatile Lookup? s_last;

    sealed record Lookup(World World, CheatSystem? System);

    /// <summary>The published state for the window (host thread), set after every change.</summary>
    internal volatile CheatView View = CheatView.Empty;

    internal PopulationData Data => data;

    /// <summary>The world's cheat system (cached for the last world asked); null without one.</summary>
    internal static CheatSystem? Of(World world)
    {
        if (s_last is { } last && ReferenceEquals(last.World, world)) return last.System;

        var found = world.Systems.OfType<CheatSystem>().FirstOrDefault();
        s_last = new Lookup(world, found);
        return found;
    }

    internal bool On(Toggle t) => _on[(int)t];

    /// <summary>The tier's current value (its list's first while off).</summary>
    internal float Value(Tier t) => Tiers.Values(t)[_tier[(int)t]];

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        bool changed = false;
        foreach (var command in commands)
        {
            changed |= command switch
            {
                SetToggle set => Set(world, set),
                CycleTier cycle => Cycle(world, cycle.Which),
                DoDeed deed => Do(world, deed),
                CameraAt at => Camera(at.Point),
                CycleSkillXp skill => CycleSkill(skill.Stat),
                _ => BaseInputs(world, command) | WorldInputs(world, command),
            };
        }

        if (changed) Publish();
    }

    bool Set(World world, SetToggle set)
    {
        _on[(int)set.Which] = set.On;
        if (set.Which == Toggle.NoEncumbrance && !set.On) ClearCarry(world);
        return true;
    }

    bool Cycle(World world, Tier tier)
    {
        int i = (int)tier;
        _tier[i] = (_tier[i] + 1) % Tiers.Values(tier).Length;
        rules.Apply(world, this);
        return true;
    }

    bool Camera(Vector3 point)
    {
        _camera = point;
        return false;
    }

    void Publish() => View = new CheatView([.. _on], [.. _tier], [.. _fedOnly], [.. _skillTier]);

    // ----- filters -----

    public void HitLanding(World world, ref PendingHit hit)
    {
        float factor = AttackerIsPlayer(world, hit.Attacker) switch
        {
            true => Value(Tier.Damage),
            false when On(Toggle.StrongNpcs) => StrongNpcDamage,
            _ => 1,
        };
        if (factor == 1) return;

        var p = hit.Packet;
        hit.Packet = p with { Cut = p.Cut * factor, Blunt = p.Blunt * factor, Pierce = p.Pierce * factor };
    }

    static bool? AttackerIsPlayer(World world, CharacterId attacker) =>
        world.Characters.TryResolveNext(attacker, out int slot) && world.Characters.Cold(slot) is { } cold ? cold.IsPlayer : null;

    public float Gain(World world, in ExperienceGain gain)
    {
        float factor = MathF.Max(Value(Tier.SkillXp), SkillValue(gain.Skill));
        if (factor == 1 || AttackerIsPlayer(world, gain.Character) != true) return gain.Amount;

        return gain.Amount * factor;
    }

    public float Rate(World world, in NeedRateAsk ask)
    {
        float factor = Value(Tier.Starving);
        return ask.Kind == NeedKind.Hunger && factor > 0 ? ask.Rate / factor : ask.Rate;
    }

    public long Price(World world, in TradeDeal deal, long price)
    {
        float factor = Value(Tier.ItemPrices);
        return factor == 1 ? price : (long)Math.Round(price * (double)factor);
    }

    public void Hash(ref StateHasher hasher)
    {
        foreach (bool on in _on) hasher.Add(on);
        foreach (int tier in _tier) hasher.Add(tier);
        foreach (int tier in _skillTier) hasher.Add(tier);
        foreach (int site in TakenTowns) hasher.Add(site);
        hasher.Add(_fedOnly.Count);
        foreach (var id in _fedOnly) hasher.Add(id);
    }
}

/// <summary>What the window shows: the switches, the tiers and the characters with their own No hunger.</summary>
internal sealed record CheatView(bool[] On, int[] Tier, CharacterId[] FedOnly, int[] SkillTier)
{
    public static CheatView Empty { get; } =
        new(new bool[Enum.GetValues<Toggle>().Length], new int[Tiers.Count], [], new int[CheatSystem.SkillSlots]);
}
