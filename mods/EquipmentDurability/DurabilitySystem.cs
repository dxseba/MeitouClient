using System.Collections.Frozen;
using Meitou.Data.Fcs;
using Meitou.Simulation;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Items;
using Meitou.Simulation.Population;

namespace Meitou.EquipmentDurability;

/// <summary>
/// The original's wear and effects. Only player characters' gear wears (its hooks all test <c>isPlayerCharacter</c>): a landed melee hit wears the
/// attacker's weapon in hand, a block the defender's, a crossbow bolt loaded the crossbow, and a melee hit on a player character each worn armour
/// piece covering the part hit. Uses are noted as they happen and applied in the serial slow step. A player's weapon deals its damage factor in melee
/// (<see cref="ICombatHitFilter"/>) and a player's armour gives its resistance factor to cut and blunt in melee (<see cref="IArmourFilter"/>), both
/// pure reads of the item's tag. New gear rolls on first contact and in a sweep over every loaded character's inventory, so its tooltip has a value.
/// </summary>
sealed class DurabilitySystem(DurabilityConfig config, PopulationData data, RngPurpose purpose, Action<string> log)
    : ICombatHitObserver, ICombatHitFilter, IArmourFilter, ICombatDefenceObserver, IItemUseObserver, IStateHashed
{
    #region Uses

    /// <summary>
    /// A use to apply: who, the part hit for an armour hit (-1 otherwise) and what kind.
    /// </summary>
    readonly record struct Use(CharacterId Who, int Part, UseKind Kind);

    /// <summary>
    /// The kinds of use.
    /// </summary>
    enum UseKind { Hit, Block, Shot, ArmourHit }

    /// <summary>
    /// The uses noted this tick, applied in the slow step.
    /// </summary>
    readonly List<Use> _uses = [];

    /// <summary>
    /// The gear rules.
    /// </summary>
    readonly Condition _condition = new(config, data);

    /// <summary>
    /// The repairs.
    /// </summary>
    readonly Repairs _repairs = new(config, data, purpose, log);

    /// <summary>
    /// Draws made in this slow step (keys the rolls, so they follow the step's fixed order).
    /// </summary>
    ulong _draws;

    /// <summary>
    /// The player characters carrying a repair item, for the menu (published every 10th tick).
    /// </summary>
    public volatile FrozenSet<CharacterId> ToolHolders = FrozenSet<CharacterId>.Empty;

    /// <summary>
    /// The gear rules, for the interface.
    /// </summary>
    public Condition Rules => _condition;

    /// <summary>
    /// A record's type (the merged data is read only after the load, so any thread may ask).
    /// </summary>
    public FcsRecordType? Type(string record) => data.Db.Find(record)?.Type;

    public void HitResolved(World world, in CombatHit hit)
    {
        if (hit.Source != CombatHitSource.Melee) return;

        _repairs.Attacked(hit.Victim);
        if (IsPlayer(world, hit.Attacker)) _uses.Add(new(hit.Attacker, -1, UseKind.Hit));
        if (config.Armour && IsPlayer(world, hit.Victim)) _uses.Add(new(hit.Victim, hit.Part, UseKind.ArmourHit));
    }

    public void Defended(World world, in CombatDefence defence)
    {
        _repairs.Attacked(defence.Defender);
        if (defence.Kind == CombatDefenceKind.Blocked && IsPlayer(world, defence.Defender)) _uses.Add(new(defence.Defender, -1, UseKind.Block));
    }

    /// <summary>
    /// A bolt loaded by a player character counts as its shot (the engine reports a bolt taken per reload, not each shot).
    /// </summary>
    public void ItemUsedUp(World world, in ItemUsed use)
    {
        if (use.Kind == ItemUseKind.Ammunition && IsPlayer(world, use.User)) _uses.Add(new(use.User, -1, UseKind.Shot));
    }

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands)
            if (command is RepairOrder order) _repairs.Start(world, order, _condition);
    }

    public void SlowWorld(World world)
    {
        _draws = 0;
        foreach (var use in _uses) Apply(world, use);
        _uses.Clear();

        _repairs.Step(world, _condition);
        if (world.Tick % 30 == 0) Sweep(world);
        if (world.Tick % 10 == 0) ToolHolders = _repairs.Holders(world);
    }

    public void Hash(ref StateHasher hasher)
    {
        hasher.Add(_uses.Count);
        _repairs.Hash(ref hasher);
    }

    static bool IsPlayer(World world, CharacterId id) =>
        world.Characters.TryResolveNext(id, out int slot) && world.Characters.Cold(slot) is { IsPlayer: true };

    #endregion

    #region Effects

    /// <summary>
    /// A player's melee blow deals its weapon's damage factor (bonus or debuff) on cut, blunt and pierce.
    /// </summary>
    public void HitLanding(World world, ref PendingHit hit)
    {
        if (hit.Source != CombatHitSource.Melee) return;
        if (!world.Characters.TryResolveNext(hit.Attacker, out int slot) || world.Characters.Cold(slot) is not { IsPlayer: true } cold) return;
        if (Gear.InHand(cold) is not { } weapon || !_condition.Tracked(weapon, out var c)) return;

        int cap = _condition.RangeOf(c).Cap;
        if (Condition.InTag(weapon.ModTag, cap) is not { } dur) return;

        float factor = _condition.DamageFactor(dur, cap);
        if (factor is > 0.999f and < 1.001f) return;

        var p = hit.Packet;
        hit.Packet = p with { Cut = p.Cut * factor, Blunt = p.Blunt * factor, Pierce = p.Pierce * factor };
    }

    /// <summary>
    /// A player's worn armour piece meets a melee hit with its resistance factor on cut and blunt (held to the game's ranges).
    /// </summary>
    public ArmourPiece Piece(World world, in ArmourAsk ask)
    {
        if (ask.Source != CombatHitSource.Melee || ask.Item is not { } item || !IsPlayer(world, ask.Wearer)) return ask.Piece;
        if (!_condition.Tracked(item, out var c)) return ask.Piece;

        int cap = _condition.RangeOf(c).Cap;
        if (Condition.InTag(item.ModTag, cap) is not { } dur) return ask.Piece;

        float factor = _condition.ResistFactor(dur, cap);
        if (factor is > 0.999f and < 1.001f) return ask.Piece;

        var p = ask.Piece;
        return p with { CutDefence = Math.Clamp(p.CutDefence * factor, 0, 0.9f), BluntDefence = Math.Clamp(p.BluntDefence * factor, 0, 0.8f) };
    }

    #endregion

    #region Wear

    void Apply(World world, Use use)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(use.Who, out int slot) || table.Cold(slot) is not { Inventory: { } inventory } cold) return;

        switch (use.Kind)
        {
            case UseKind.Hit:
                Wear(world, use.Who, cold, Gear.InHand(cold), config.PerHit);
                break;
            case UseKind.Block:
                Wear(world, use.Who, cold, Gear.InHand(cold), config.PerBlock);
                break;
            case UseKind.Shot:
                Wear(world, use.Who, cold, Gear.Crossbow(inventory), config.PerShot);
                break;
            case UseKind.ArmourHit:
                WearArmour(world, use.Who, cold, use.Part);
                break;
        }
    }

    /// <summary>
    /// Each worn armour piece covering the part hit loses a point (once per hit, as the original's per-hit list).
    /// </summary>
    void WearArmour(World world, CharacterId id, CharacterCold cold, int part)
    {
        if (cold.Medical?.Parts is not { } parts || part < 0 || part >= parts.Count) return;

        string partId = parts[part].Template.StringId;
        foreach (var piece in Gear.Worn(cold.Inventory).ToList())
        {
            if (data.Combat.Armour(piece.Record)?.Coverage.Any(e => e.PartStringId == partId) == true)
                Wear(world, id, cold, piece, config.PerArmourHit);
        }
    }

    /// <summary>
    /// The original's <c>OnPlayerWear</c>: rolls the item if new, takes <paramref name="amount"/> unless its grade spares this use, and destroys it at
    /// 0 with <c>BreakWeapons</c>.
    /// </summary>
    void Wear(World world, CharacterId id, CharacterCold owner, ItemInstance? item, int amount)
    {
        if (item is null || !_condition.Tracked(item, out var c)) return;

        int dur = _condition.GetOrRoll(item, c, Draw(world, id));
        if (dur > 0 && !Spared(world, id, item, c))
        {
            dur = Math.Max(0, dur - amount);
            Condition.Set(item, dur);
        }

        if (dur <= 0 && config.Break) _repairs.Destroy(owner, item);
    }

    /// <summary>
    /// Whether the item's grade spares this use (a roll of 0..99 at or above its wear chance).
    /// </summary>
    bool Spared(World world, CharacterId id, ItemInstance item, GearClass c)
    {
        int chance = _condition.WearChance(item, c);
        if (chance >= 100) return false;
        if (chance <= 0) return true;

        return Rng.Int(Draw(world, id), 100) >= chance;
    }

    /// <summary>
    /// The next roll of this step, keyed by the world seed, the tick, the character and the draw count.
    /// </summary>
    ulong Draw(World world, CharacterId id) => Rng.Hash(world.Seed, Rng.Key(id) ^ (ulong)world.Tick << 20, purpose, _draws++);

    /// <summary>
    /// Rolls every new tracked item carried by a loaded character (the original rolls on the first tooltip; the interface cannot change items).
    /// </summary>
    void Sweep(World world)
    {
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (table.Cold(slot) is not { Inventory: { } inventory }) continue;

            var id = table.IdOf(slot);
            foreach (var item in inventory.All())
            {
                if (item.Type is not (FcsRecordType.Weapon or FcsRecordType.Crossbow or FcsRecordType.Armour)) continue;
                if (Condition.InTag(item.ModTag, int.MaxValue) is not null) continue;
                if (_condition.Tracked(item, out var c)) _condition.GetOrRoll(item, c, Draw(world, id));
            }
        }
    }

    #endregion
}

/// <summary>
/// Where a character's gear is.
/// </summary>
static class Gear
{
    /// <summary>
    /// The weapon item in the character's hand: the back one when its fighter holds the back weapon, else the hip one; null for fists or an animal.
    /// </summary>
    public static ItemInstance? InHand(CharacterCold cold)
    {
        if (cold.Fighter is not { IsAnimal: false, Weapon: not null } fighter) return null;

        string section = fighter.WeaponIsBack ? "back" : "hip";
        return cold.Inventory?.Items.FirstOrDefault(i => i.Section == section && i.Type == FcsRecordType.Weapon);
    }

    /// <summary>
    /// The crossbow in a weapon slot.
    /// </summary>
    public static ItemInstance? Crossbow(Inventory inventory) =>
        inventory.Items.FirstOrDefault(i => i.Type == FcsRecordType.Crossbow && Condition.WeaponSections.Contains(i.Section));

    /// <summary>
    /// The item in a section, if any.
    /// </summary>
    public static ItemInstance? In(Inventory inventory, string section) => inventory.Items.FirstOrDefault(i => i.Section == section);

    /// <summary>
    /// The worn armour pieces.
    /// </summary>
    public static IEnumerable<ItemInstance> Worn(Inventory? inventory) =>
        inventory?.Items.Where(i => i.Type == FcsRecordType.Armour && Condition.ArmourSections.Contains(i.Section)) ?? [];
}
