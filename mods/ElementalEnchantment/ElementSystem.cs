using System.Collections.Frozen;
using System.Text;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Items;

namespace Meitou.ElementalEnchantment;

/// <summary>
/// What the interface shows of a character's enchantment: its elements (the secondary as chosen, and whether it counts yet), level and
/// experience, aptitudes, ward and whether it fights unarmed (the hand effects).
/// </summary>
sealed record EnchantView(bool Player, bool Able, Element Main, Element Second, int Level, int Xp, Element Favoured, bool Rare, Element Ward,
    bool Unarmed)
{
    /// <summary>
    /// The enchantment the bonuses read (the secondary only from level 50).
    /// </summary>
    public Enchant Enchant => new(Main, Level >= Elements.SecondaryLevel ? Second : Element.None, Level, Ward);
}

/// <summary>
/// The enchantments of the world: each player character's profile (keyed by lasting id, saved), the bosses' fixed ones (by CHARACTER record, from the optional bosses.json),
/// the blows' bonuses and the ward's protection (<see cref="ICombatHitFilter"/>), the speed bonuses (<see cref="ICombatSpeedFilter"/>, a pure
/// read on any thread) and the experience of landed hits (<see cref="ICombatHitObserver"/>, applied in the serial slow step).
/// </summary>
sealed class ElementSystem(EnchantSettings settings, ModSkill skill)
    : ICombatHitFilter, ICombatHitObserver, ICombatSpeedFilter, IModSaveData
{
    #region State

    /// <summary>
    /// The save layout: 2 since the single level per character (1 kept one active element per character).
    /// </summary>
    const int Version = 2;

    /// <summary>
    /// The player characters' profiles by lasting id; changed only in serial steps.
    /// </summary>
    readonly Dictionary<ulong, Profile> _profiles = [];

    /// <summary>
    /// This tick's attackers of landed hits, trained in the slow step.
    /// </summary>
    readonly List<CharacterId> _trained = [];

    /// <summary>
    /// The window's choices and the recalibrations waiting for the slow step.
    /// </summary>
    readonly List<ChooseElementOrder> _orders = [];

    /// <summary>
    /// The enchanted characters for the interface (players and bosses in the world), rebuilt every 15 ticks and after a change.
    /// </summary>
    public volatile FrozenDictionary<CharacterId, EnchantView> View = FrozenDictionary<CharacterId, EnchantView>.Empty;

    #endregion

    #region Commands and the slow step

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands)
        {
            if (command is ChooseElementOrder order) _orders.Add(order);
        }
    }

    public void SlowWorld(World world)
    {
        bool changed = _orders.Count > 0 || _trained.Count > 0;
        foreach (var order in _orders) Choose(world, order);
        _orders.Clear();

        foreach (var id in _trained) Train(world, id);
        _trained.Clear();

        if (world.Tick % 15 != 0 && !changed) return;

        GiveProfiles(world);
        View = Publish(world);
    }

    /// <summary>
    /// A choice in the ENCHANTMENT window: the main element (any time) or the secondary (from level 50, unlike the main).
    /// </summary>
    void Choose(World world, ChooseElementOrder order)
    {
        if (ProfileOf(world, order.Character) is not { Able: true } profile) return;

        var element = order.Chosen;
        if (!order.Secondary)
        {
            profile.Main = element;
            if (profile.Second == element) profile.Second = Element.None;
            return;
        }

        if (profile.Level < Elements.SecondaryLevel || element == profile.Main && element != Element.None) return;
        profile.Second = element;
    }

    /// <summary>
    /// One landed hit's experience for the attacker's main element; the stats window's skill follows the level.
    /// </summary>
    void Train(World world, CharacterId id)
    {
        if (ProfileOf(world, id) is not { } profile || !world.Characters.TryResolveNext(id, out int slot)) return;
        if (!profile.Gain(settings.XpMultiplier, settings.FlatAptitude)) return;

        if (world.Characters.Cold(slot)?.Stats is { } stats) skill.Set(stats, Shown(profile));
    }

    /// <summary>
    /// The level the stats window shows: the whole level and the way to the next.
    /// </summary>
    static float Shown(Profile profile)
    {
        int level = profile.Level;
        if (level >= Elements.MaxLevel) return Elements.MaxLevel;

        int from = Elements.XpFor(level), to = Elements.XpFor(level + 1);
        return level + (profile.Xp - from) / (float)(to - from);
    }

    /// <summary>
    /// Every player character without a profile gets one (its lasting id given), rolled as the original rolls a recruit's.
    /// </summary>
    void GiveProfiles(World world)
    {
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { IsPlayer: true } cold) continue;
            if (!cold.LastingId.IsNone && _profiles.ContainsKey(cold.LastingId.Value)) continue;

            ulong lasting = world.LastingIds.GetOrGive(table.IdOf(slot)).Value;
            var profile = Profile.Roll(lasting, settings.RecruitChance);
            _profiles[lasting] = profile;
            if (cold.Stats is { } stats) skill.Set(stats, Shown(profile));
        }
    }

    /// <summary>
    /// The player character's profile; null for others or before it has one.
    /// </summary>
    Profile? ProfileOf(World world, CharacterId id) =>
        world.Characters.TryResolveNext(id, out int slot) && world.Characters.Cold(slot) is { IsPlayer: true, LastingId.IsNone: false } cold
            ? _profiles.GetValueOrDefault(cold.LastingId.Value) : null;

    #endregion

    #region Recalibrator

    /// <summary>
    /// The aptitude recalibrator's right click (not in a bag): one is used up and the aptitude rerolled to a different result; a character
    /// without elemental ability gains it.
    /// </summary>
    public bool Recalibrate(ItemUse use)
    {
        if (use.Item.Section == "backpack_content") return false;
        if (ProfileOf(use.World, use.User) is not { } profile) return false;

        var lasting = use.World.LastingIds.GetOrGive(use.User);
        profile.Recalibrate(lasting.Value ^ (ulong)use.World.Tick);
        use.Inventory.RemoveOne(use.Item);
        use.World.Post($"Aptitude: {profile.Favoured}{(profile.Rare ? " (all elements 100%)" : "")}");
        View = Publish(use.World);
        return true;
    }

    #endregion

    #region Combat

    public void HitResolved(World world, in CombatHit hit)
    {
        if (settings.Enabled && hit.Source != CombatHitSource.Execution) _trained.Add(hit.Attacker);
    }

    /// <summary>
    /// The attacker's bonuses and resonance on the blow or bolt, then the victim's ward.
    /// </summary>
    public void HitLanding(World world, ref PendingHit hit)
    {
        if (!settings.Enabled || hit.Source == CombatHitSource.Execution) return;

        if (EnchantOf(world, hit.Attacker) is { } enchant)
        {
            var packet = Elements.Apply(hit.Packet, enchant, settings.Strength);
            hit.Packet = Elements.Resonate(packet, enchant, settings.Resonance * settings.Strength);
        }

        Ward(world, ref hit);
    }

    /// <summary>
    /// A worn ward takes <see cref="EnchantSettings.DevicePercent"/> off the cut, blunt and pierce damage the wearer takes (none at effect
    /// strength 0).
    /// </summary>
    void Ward(World world, ref PendingHit hit)
    {
        if (settings.DevicePercent <= 0 || settings.Strength <= 0) return;
        if (!world.Characters.TryResolveNext(hit.Victim, out int slot) || WardOf(world.Characters.Cold(slot)) == Element.None) return;

        float keep = 1 - Math.Clamp(settings.DevicePercent, 0, 30) / 100;
        hit.Packet = hit.Packet with { Cut = hit.Packet.Cut * keep, Blunt = hit.Packet.Blunt * keep, Pierce = hit.Packet.Pierce * keep };
    }

    /// <summary>
    /// The attack, block or reload rate or bolt speed with the character's enchantment. Any thread; reads only the asked character.
    /// </summary>
    public float Rate(World world, in CombatSpeedAsk ask)
    {
        if (!settings.Enabled || EnchantOf(world, ask.Character) is not { } enchant) return ask.Rate;

        float resonance = settings.Resonance * settings.Strength;
        return ask.Kind switch
        {
            CombatSpeedKind.Reload => Elements.Reload(ask.Rate, enchant, settings.Strength, resonance),
            CombatSpeedKind.Projectile => Elements.Bolt(ask.Rate, enchant, settings.Strength),
            _ => Elements.Speed(ask.Rate, ask.Kind, enchant, settings.Strength, resonance),
        };
    }

    /// <summary>
    /// The character's enchantment with a main element: a player's profile, else a boss's by its record.
    /// </summary>
    Enchant? EnchantOf(World world, CharacterId id)
    {
        if (!world.Characters.TryResolveNext(id, out int slot) || world.Characters.Cold(slot) is not { } cold) return null;

        var view = ViewOf(cold);
        return view is { Able: true, Main: not Element.None } ? view.Enchant : null;
    }

    /// <summary>
    /// The character's enchantment as the interface shows it; null without one.
    /// </summary>
    EnchantView? ViewOf(CharacterCold cold)
    {
        bool unarmed = cold.Fighter?.Unarmed ?? true;
        if (cold.IsPlayer)
        {
            if (cold.LastingId.IsNone || !_profiles.TryGetValue(cold.LastingId.Value, out var p)) return null;
            return new(true, p.Able, p.Main, p.Second, p.Level, p.Xp, p.Favoured, p.Rare, WardOf(cold), unarmed);
        }

        if (!EnchantData.Bosses.TryGetValue(cold.RecordId, out var boss)) return null;
        return new(false, true, boss.Main, boss.Second, boss.Level, Elements.XpFor(boss.Level), boss.Main, false, WardOf(cold), unarmed);
    }

    /// <summary>
    /// The element of the ward the character wears (in a worn slot, not its main grid or bag); none without one.
    /// </summary>
    static Element WardOf(CharacterCold? cold)
    {
        if (cold?.Inventory is not { } inventory) return Element.None;

        foreach (var item in inventory.Items)
        {
            if (InventoryCommands.IsEquipmentSection(item.Section) && EnchantData.Wards.TryGetValue(item.Record, out var element)) return element;
        }

        return Element.None;
    }

    #endregion

    #region Interface

    /// <summary>
    /// The enchanted characters in the world.
    /// </summary>
    FrozenDictionary<CharacterId, EnchantView> Publish(World world)
    {
        var table = world.Characters;
        var view = new Dictionary<CharacterId, EnchantView>();
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { } cold || ViewOf(cold) is not { } shown) continue;
            view[table.IdOf(slot)] = shown;
        }

        return view.ToFrozenDictionary();
    }

    #endregion

    #region Persistence

    public void Write(Stream stream)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Version);
        writer.Write(_profiles.Count);
        foreach (var (id, p) in _profiles.OrderBy(a => a.Key))
        {
            writer.Write(id);
            writer.Write(p.Able);
            writer.Write((byte)p.Main);
            writer.Write((byte)p.Second);
            writer.Write(p.Xp);
            writer.Write((byte)p.Carry);
            writer.Write((byte)p.Favoured);
            writer.Write(p.Rare);
        }
    }

    public void Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        int version = reader.ReadInt32();
        if (version is not (1 or 2)) return;

        _profiles.Clear();
        for (int i = reader.ReadInt32(); i > 0; i--)
        {
            ulong id = reader.ReadUInt64();
            _profiles[id] = version == 1 ? ReadOld(id, reader) : ReadProfile(reader);
        }
    }

    /// <summary>
    /// A version 2 profile.
    /// </summary>
    static Profile ReadProfile(BinaryReader reader) => new()
    {
        Able = reader.ReadBoolean(),
        Main = (Element)reader.ReadByte(),
        Second = (Element)reader.ReadByte(),
        Xp = Math.Clamp(reader.ReadInt32(), 0, Elements.MaxXp),
        Carry = reader.ReadByte() % 100,
        Favoured = (Element)Math.Clamp((int)reader.ReadByte(), 1, 5),
        Rare = reader.ReadBoolean(),
    };

    /// <summary>
    /// A version 1 entry (the active element alone): a fresh roll with that main element.
    /// </summary>
    Profile ReadOld(ulong id, BinaryReader reader)
    {
        var profile = Profile.Roll(id, settings.RecruitChance);
        profile.Main = (Element)reader.ReadByte();
        profile.Able = true;
        return profile;
    }

    #endregion
}

/// <summary>
/// <paramref name="Character"/> chooses the element <paramref name="Value"/> as its main element, or its secondary with <paramref name="Secondary"/>.
/// </summary>
public sealed record ChooseElementOrder(CharacterId Character, bool Secondary, byte Value) : SimCommand
{
    /// <summary>
    /// The element chosen.
    /// </summary>
    internal Element Chosen => (Element)Math.Clamp((int)Value, 0, 5);
}
