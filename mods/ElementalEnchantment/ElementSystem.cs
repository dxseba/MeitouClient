using System.Collections.Frozen;
using System.Text;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;

namespace Meitou.ElementalEnchantment;

/// <summary>
/// Each character's active element (keyed by its lasting id, saved), the blows' element bonus (<see cref="ICombatHitFilter"/>: the active
/// element's, plus from level 50 the best other element's at its share), and the elements' training: every landed hit with an element active trains
/// its skill by the character's aptitude for it (a fixed roll of its lasting id, 0.5 to 1.5).
/// </summary>
sealed class ElementSystem(EnchantSettings settings, IReadOnlyDictionary<Element, ModSkill> skills)
    : ICombatHitFilter, ICombatHitObserver, IModSaveData
{
    const int Version = 1;

    readonly Dictionary<ulong, Element> _active = [];
    readonly List<CharacterId> _trained = [];
    readonly List<CycleElementOrder> _orders = [];

    /// <summary>The player characters' active element and its level, for the menu.</summary>
    public volatile FrozenDictionary<CharacterId, (Element Element, int Level)> View = FrozenDictionary<CharacterId, (Element, int)>.Empty;

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands)
        {
            if (command is CycleElementOrder order) _orders.Add(order);
        }
    }

    public void HitResolved(World world, in CombatHit hit)
    {
        if (settings.Enabled && hit.Source != CombatHitSource.Execution) _trained.Add(hit.Attacker);
    }

    public void HitLanding(World world, ref PendingHit hit)
    {
        if (!settings.Enabled || hit.Source == CombatHitSource.Execution || !world.Characters.TryResolveNext(hit.Attacker, out int a)) return;
        if (world.Characters.Cold(a) is not { Stats: { } stats } cold || ActiveOf(cold) is not (not Element.None and var element)) return;

        float level = skills[element].Level(stats);
        var packet = Elements.Apply(hit.Packet, element, level / 100 * settings.Strength);
        if (Secondary(stats, element) is { } second)
            packet = Elements.Apply(packet, second.Element, second.Level / 100 * settings.Strength * Elements.SecondaryShare(level));
        hit.Packet = packet;
    }

    Element ActiveOf(CharacterCold cold) => !cold.LastingId.IsNone && _active.TryGetValue(cold.LastingId.Value, out var e) ? e : Element.None;

    (Element Element, float Level)? Secondary(CharacterStats stats, Element primary)
    {
        if (Elements.SecondaryShare(skills[primary].Level(stats)) <= 0) return null;

        var best = Elements.All.Where(e => e != primary).Select(e => (Element: e, Level: skills[e].Level(stats))).MaxBy(x => x.Level);
        return best.Level > 1 ? best : null;
    }

    public void SlowWorld(World world)
    {
        foreach (var order in _orders) Cycle(world, order.Character);
        _orders.Clear();

        foreach (var id in _trained) Train(world, id);
        _trained.Clear();

        if (world.Tick % 15 == 0) View = Publish(world);
    }

    void Cycle(World world, CharacterId id)
    {
        if (!world.Characters.TryResolveNext(id, out int slot) || world.Characters.Cold(slot) is not { IsPlayer: true }) return;

        ulong lasting = world.LastingIds.GetOrGive(id).Value;
        var next = (Element)(((int)_active.GetValueOrDefault(lasting) + 1) % (Elements.All.Length + 1));
        _active[lasting] = next;
        View = Publish(world);
    }

    void Train(World world, CharacterId id)
    {
        if (!world.Characters.TryResolveNext(id, out int slot) || world.Characters.Cold(slot) is not { Stats: { } stats } cold) return;
        if (ActiveOf(cold) is not (not Element.None and var element)) return;

        skills[element].Gain(stats, settings.XpPerHit * settings.XpMultiplier * Aptitude(cold.LastingId.Value, element));
    }

    /// <summary>A fixed 0.5 to 1.5 per character and element (1 with all aptitudes at 100%).</summary>
    float Aptitude(ulong lasting, Element element) =>
        settings.FlatAptitude ? 1 : 0.5f + Rng.Float(Rng.Mix(lasting * 31 + (ulong)element));

    FrozenDictionary<CharacterId, (Element, int)> Publish(World world)
    {
        var table = world.Characters;
        var view = new Dictionary<CharacterId, (Element, int)>();
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { IsPlayer: true, Stats: { } stats } cold) continue;

            var element = ActiveOf(cold);
            view[table.IdOf(slot)] = (element, element == Element.None ? 0 : (int)skills[element].Level(stats));
        }

        return view.ToFrozenDictionary();
    }

    public void Write(Stream stream)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Version);
        writer.Write(_active.Count);
        foreach (var (id, element) in _active.OrderBy(a => a.Key))
        {
            writer.Write(id);
            writer.Write((byte)element);
        }
    }

    public void Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (reader.ReadInt32() != Version) return;

        _active.Clear();
        for (int i = reader.ReadInt32(); i > 0; i--)
        {
            ulong id = reader.ReadUInt64();
            _active[id] = (Element)reader.ReadByte();
        }
    }
}
