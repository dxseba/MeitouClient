using System.Collections.Frozen;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Bodies;
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
/// pure reads of the item's tag; a player's worn crossbow reloads its reload change slower or faster (<see cref="ICombatSpeedFilter"/>). Gear rolls on
/// its first use, on its first tooltip (<see cref="RollOrder"/>, as the original) and when the world makes it (<see cref="IItemCreatedObserver"/>: loot,
/// crafted, shop stock and spawn kit, so a trader's stock, which no tooltip command can name, shows a value).
/// </summary>
sealed class DurabilitySystem(DurabilityConfig config, PopulationData data, RngPurpose purpose, Action<string> log, Action<string> sound)
    : ICombatHitObserver, ICombatHitFilter, IArmourFilter, ICombatDefenceObserver, IItemUseObserver, IItemCreatedObserver, ICombatSpeedFilter,
        IStateHashed
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
    readonly Repairs _repairs = new(config, data, purpose, log, sound);

    /// <summary>
    /// Draws made in this slow step (keys the rolls, so they follow the step's fixed order).
    /// </summary>
    ulong _draws;

    /// <summary>
    /// Rolls made outside the slow step (items made, tooltip rolls), in their serial order; never reset.
    /// </summary>
    ulong _otherDraws;

    /// <summary>
    /// The player characters carrying a repair item, for the menu (published every 10th tick).
    /// </summary>
    public volatile FrozenSet<CharacterId> ToolHolders = FrozenSet<CharacterId>.Empty;

    /// <summary>
    /// The repair animation of a character repairing; null for anyone else (a pure read for the animation's parallel step).
    /// </summary>
    public CombatMove? RepairMove(World world, CharacterId id, long tick) => _repairs.Move(world, id, tick);

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
        {
            if (command is RepairOrder order) _repairs.Start(world, order, _condition);
            else if (command is RollOrder roll) Roll(world, roll);
        }
    }

    public void SlowWorld(World world)
    {
        _draws = 0;
        foreach (var use in _uses) Apply(world, use);
        _uses.Clear();

        _repairs.Step(world, _condition);
        if (world.Tick % 10 == 0) ToolHolders = _repairs.Holders(world);
    }

    public void Hash(ref StateHasher hasher)
    {
        hasher.Add(_uses.Count);
        hasher.Add((long)_otherDraws);
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

    /// <summary>
    /// A player's crossbow reload takes its reload change (the original's reload hook: the game's time plus the change, never under a quarter of
    /// it when faster nor under 0.1 s). The game's time is rebuilt from the same formula and stats the reload uses, so the answer is the ratio.
    /// </summary>
    public float Rate(World world, in CombatSpeedAsk ask)
    {
        if (ask.Kind != CombatSpeedKind.Reload) return ask.Rate;
        if (!world.Characters.TryResolveNext(ask.Character, out int slot)) return ask.Rate;
        if (world.Characters.Cold(slot) is not { IsPlayer: true, Inventory: { } inventory } cold) return ask.Rate;
        if (Gear.Crossbow(inventory) is not { } item || !_condition.Tracked(item, out var c)) return ask.Rate;

        int cap = _condition.RangeOf(c).Cap;
        if (Condition.InTag(item.ModTag, cap) is not { } dur || GameReload(cold) is not { } time || time <= 0) return ask.Rate;

        float delta = _condition.ReloadDelta(dur, cap);
        if (delta == 0) return ask.Rate;

        float changed = time + delta;
        if (delta < 0 && changed < time * 0.25f) changed = time * 0.25f;
        changed = MathF.Max(changed, 0.1f);
        return ask.Rate * time / changed;
    }

    /// <summary>
    /// The game's reload seconds for the character's crossbow: its reload range at the blend of its effective crossbows and dexterity, as the
    /// reload (<c>RangedFormulas.ReloadTime</c>, <c>SkillBlend</c>) takes them; null without a crossbow.
    /// </summary>
    static float? GameReload(CharacterCold cold)
    {
        if (cold is not { Fighter: { Crossbow: { } x } fighter, Stats: { } stats, Medical: { } medical }) return null;

        float skill = stats[StatsEnumerated.Crossbows] * stats.Scale * medical.StatMultiplier(StatsEnumerated.Crossbows) * fighter.Factors.Ranged;
        float dexterity = stats[StatsEnumerated.Dexterity] * stats.Scale * medical.StatMultiplier(StatsEnumerated.Dexterity);
        return RangedFormulas.ReloadTime(RangedFormulas.SkillBlend(skill, dexterity, fighter.DexterityMult), x.Stats.ReloadMin, x.Stats.ReloadMax);
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
    /// A new item the world made rolls at once (<see cref="IItemCreatedObserver"/>); a crafted one starts at its cap.
    /// </summary>
    public void Created(World world, in ItemCreated made)
    {
        var item = made.Item;
        if (Condition.InTag(item.ModTag, int.MaxValue) is null && _condition.Tracked(item, out var c)) _condition.GetOrRoll(item, c, OtherDraw(world, item));
    }

    /// <summary>
    /// The first tooltip of an item rolls it (the original's tooltip roll): only while the item there is still the record shown, without a value.
    /// </summary>
    void Roll(World world, RollOrder order)
    {
        if (Holding(world, order.Owner) is not { } inventory || inventory.All().ElementAtOrDefault(order.Index) is not { } item) return;
        if (item.Record != order.Record || Condition.InTag(item.ModTag, int.MaxValue) is not null || !_condition.Tracked(item, out var c)) return;

        _condition.GetOrRoll(item, c, OtherDraw(world, item));
    }

    /// <summary>
    /// The inventory a tooltip's owner names: a character's, or a container's at a site.
    /// </summary>
    static Inventory? Holding(World world, ItemOwner owner) => owner.Holder switch
    {
        ItemHolder.Character => world.Characters.TryResolveNext(owner.Character, out int slot) ? world.Characters.Cold(slot)?.Inventory : null,
        ItemHolder.Site => world.Fixtures.Find(owner.Site)?.Contents,
        _ => null,
    };

    /// <summary>
    /// A roll outside the slow step, keyed by the world seed, the tick, the item's record and the running count.
    /// </summary>
    ulong OtherDraw(World world, ItemInstance item) =>
        Rng.Hash(world.Seed, Rng.StableHash(item.Record) ^ (ulong)world.Tick << 20, purpose, _otherDraws++);

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
