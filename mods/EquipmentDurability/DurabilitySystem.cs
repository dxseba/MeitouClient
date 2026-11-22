using System.Collections.Frozen;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Items;
using Meitou.Simulation.Population;

namespace Meitou.EquipmentDurability;

/// <summary>A player character's gear as the menu shows it: the weapon's condition (null: none) and the worn armour's lowest.</summary>
readonly record struct GearView(float? Weapon, float? Armour);

/// <summary>
/// Wears the gear: landed hits and parries are noted as they resolve (<see cref="ICombatHitObserver"/>, <see cref="ICombatDefenceObserver"/>) and
/// worn in the serial slow step, the attacker's weapon per hit, the parrying weapon per parry and the victim's armour pieces covering the part hit,
/// by the damage they stopped. Worn weapons hit softer (<see cref="ICombatHitFilter"/>) and worn armour stops less (<see cref="IArmourFilter"/>),
/// both pure reads of the item's tag. Repairs with Tools run here too. The player characters' gear is published every 30th tick.
/// </summary>
sealed class DurabilitySystem(DurabilitySettings settings, PopulationData data)
    : ICombatHitObserver, ICombatHitFilter, IArmourFilter, ICombatDefenceObserver
{
    readonly record struct Wear(CharacterId Attacker, CharacterId Victim, int Part, float Absorbed, bool Melee);

    readonly List<Wear> _hits = [];
    readonly List<CharacterId> _parries = [];
    readonly List<RepairOrder> _repairs = [];

    /// <summary>The player characters' gear, for the menu.</summary>
    public volatile FrozenDictionary<CharacterId, GearView> Gear = FrozenDictionary<CharacterId, GearView>.Empty;

    public void HitResolved(World world, in CombatHit hit)
    {
        if (!settings.Enabled || hit.Source == CombatHitSource.Execution) return;

        float absorbed = hit.Absorbed.Cut + hit.Absorbed.Blunt + hit.Absorbed.Pierce;
        _hits.Add(new(hit.Attacker, hit.Victim, hit.Part, absorbed, hit.Source == CombatHitSource.Melee));
    }

    public void HitLanding(World world, ref PendingHit hit)
    {
        if (!settings.Enabled || hit.Source != CombatHitSource.Melee) return;
        if (!world.Characters.TryResolveNext(hit.Attacker, out int a)) return;
        if (Condition.Weapon(world.Characters.Cold(a)?.Inventory) is not { } weapon) return;

        float factor = Condition.Factor(Condition.Of(weapon), settings.Threshold, settings.BrokenPenalty);
        if (factor >= 1) return;

        var p = hit.Packet;
        hit.Packet = p with { Cut = p.Cut * factor, Blunt = p.Blunt * factor, Pierce = p.Pierce * factor };
    }

    /// <summary>A worn armour piece below the threshold keeps its defences scaled by the same factor as a worn weapon's damage.</summary>
    public ArmourPiece Piece(World world, in ArmourAsk ask)
    {
        if (!settings.Enabled || ask.Item is not { } item || !Condition.Tracked(item)) return ask.Piece;

        float factor = Condition.Factor(Condition.Of(item), settings.Threshold, settings.BrokenPenalty);
        if (factor >= 1) return ask.Piece;

        var p = ask.Piece;
        return p with { CutDefence = p.CutDefence * factor, BluntDefence = p.BluntDefence * factor, PierceDefence = p.PierceDefence * factor };
    }

    /// <summary>A parry (a block with a weapon) wears the defender's weapon in the slow step.</summary>
    public void Defended(World world, in CombatDefence defence)
    {
        if (settings.Enabled && defence.Kind == CombatDefenceKind.Blocked && defence.Parry) _parries.Add(defence.Defender);
    }

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands)
        {
            if (command is RepairOrder repair) _repairs.Add(repair);
        }
    }

    public void SlowWorld(World world)
    {
        foreach (var hit in _hits) Apply(world, hit);
        _hits.Clear();

        foreach (var defender in _parries) WearWeapon(world, defender, settings.BlockWear);
        _parries.Clear();

        foreach (var repair in _repairs) Repair(world, repair.Character);
        _repairs.Clear();

        if (world.Tick % 30 == 0) Gear = Publish(world);
    }

    #region Wear

    void Apply(World world, Wear hit)
    {
        var table = world.Characters;
        if (hit.Melee) WearWeapon(world, hit.Attacker, settings.WeaponWear);

        if (!table.TryResolveNext(hit.Victim, out int v) || table.Cold(v) is not { } victim) return;
        if (victim.Medical?.Parts is not { } parts || hit.Part < 0 || hit.Part >= parts.Count) return;

        string part = parts[hit.Part].Template.StringId;
        float wear = settings.ArmourWear * (1 + hit.Absorbed / 20);
        foreach (var armour in Condition.Worn(victim.Inventory).ToList())
        {
            if (data.Combat.Armour(armour.Record)?.CoverageOf(part) > 0) Degrade(world, victim, armour, wear);
        }
    }

    void WearWeapon(World world, CharacterId id, float amount)
    {
        var table = world.Characters;
        if (table.TryResolveNext(id, out int a) && table.Cold(a) is { } owner && Condition.Weapon(owner.Inventory) is { } weapon)
            Degrade(world, owner, weapon, amount);
    }

    void Degrade(World world, CharacterCold owner, ItemInstance item, float amount)
    {
        float before = Condition.Of(item);
        if (before <= 0) return;

        float after = Math.Max(0, before - amount);
        Condition.Set(item, after);
        if (after > 0) return;

        if (settings.DeleteBroken) owner.Inventory?.Items.Remove(item);
        if (owner.IsPlayer) world.Post(settings.DeleteBroken ? $"{owner.Name}'s {item.Name} broke apart." : $"{owner.Name}'s {item.Name} is broken.");
    }

    #endregion

    #region Repair

    /// <summary>With an item named Tools in the character's inventory, its gear regains condition by its smithing skills.</summary>
    void Repair(World world, CharacterId id)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(id, out int slot) || table.Cold(slot) is not { IsPlayer: true, Inventory: { } inventory } cold) return;
        if (!inventory.Items.Any(i => i.Name == settings.ToolName))
        {
            world.Post($"{cold.Name} needs {settings.ToolName} to repair gear.");
            return;
        }

        int fixedCount = 0;
        if (Condition.Weapon(inventory) is { } weapon) fixedCount += Mend(weapon, Skill(cold, StatsEnumerated.SmithingWeapon));
        foreach (var armour in Condition.Worn(inventory)) fixedCount += Mend(armour, Skill(cold, StatsEnumerated.SmithingArmour));

        world.Post(fixedCount == 0 ? $"{cold.Name}'s gear needs no repair." : $"{cold.Name} repaired {fixedCount} piece(s) of gear.");
    }

    static float Skill(CharacterCold cold, StatsEnumerated stat) => cold.Stats?[stat] ?? 0;

    /// <summary>Adds <c>0.15 + 0.35 x skill/100</c> condition; 1 when it changed the item.</summary>
    int Mend(ItemInstance item, float skill)
    {
        float before = Condition.Of(item);
        if (before >= 1) return 0;

        Condition.Set(item, before + settings.RepairBase + settings.RepairSkill * Math.Clamp(skill, 0, 100) / 100);
        return 1;
    }

    #endregion

    FrozenDictionary<CharacterId, GearView> Publish(World world)
    {
        var table = world.Characters;
        var gear = new Dictionary<CharacterId, GearView>();
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { IsPlayer: true, Inventory: { } inventory }) continue;

            float? weapon = Condition.Weapon(inventory) is { } w ? Condition.Of(w) : null;
            var worn = Condition.Worn(inventory).Select(Condition.Of).ToList();
            gear[table.IdOf(slot)] = new(weapon, worn.Count > 0 ? worn.Min() : null);
        }

        return gear.ToFrozenDictionary();
    }
}
