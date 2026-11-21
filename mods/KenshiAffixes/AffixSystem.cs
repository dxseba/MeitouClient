using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Items;

namespace Meitou.KenshiAffixes;

/// <summary>
/// Rolls affixes on the weapons and armour the loaded characters carry (every 60th tick, once per item, kept in its tag), applies the stat affixes
/// of each character's equipped gear as stat modifiers (every 30th tick; not saved, so set again after a load), and the weapon damage affixes as a
/// hit filter (a pure read of the attacker's weapon).
/// </summary>
sealed class AffixSystem(AffixSettings settings, RngPurpose purpose) : ICombatHitFilter
{
    const string ModifierKey = "dxseba.kenshiaffixes";

    /// <summary>Armour's melee attack and defence points stop at this total (the original's cap).</summary>
    const float ArmourPointCap = 25;

    readonly List<ShowAffixesOrder> _shows = [];

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands)
        {
            if (command is ShowAffixesOrder show) _shows.Add(show);
        }
    }

    public void SlowWorld(World world)
    {
        foreach (var show in _shows) Show(world, show.Character);
        _shows.Clear();

        if (world.Tick % 60 == 0 && settings.Enabled) RollAll(world);
        if (world.Tick % 30 == 0) ApplyAll(world);
    }

    public void HitLanding(World world, ref PendingHit hit)
    {
        if (!settings.Enabled || hit.Source != CombatHitSource.Melee || !world.Characters.TryResolveNext(hit.Attacker, out int a)) return;
        if (Affixes.FightingWeapon(world.Characters.Cold(a)?.Inventory) is not { } weapon) return;

        var p = hit.Packet;
        foreach (var affix in Affixes.Of(weapon))
        {
            float share = 1 + affix.Value / 100;
            p = affix.Kind.Effect switch
            {
                AffixEffect.Cut => p with { Cut = p.Cut * share },
                AffixEffect.Blunt => p with { Blunt = p.Blunt * share },
                AffixEffect.Bleed => p with { BleedMultiplier = p.BleedMultiplier * share },
                _ => p,
            };
        }

        hit.Packet = p;
    }

    #region Rolling

    void RollAll(World world)
    {
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot)?.Inventory is not { } inventory) continue;

            ulong owner = Rng.Key(table.IdOf(slot));
            for (int i = 0; i < inventory.Items.Count; i++)
            {
                var item = inventory.Items[i];
                if (Affixes.Eligible(item) && !Affixes.Rolled(item)) Roll(world, item, owner ^ (ulong)i << 40);
            }
        }
    }

    /// <summary>
    /// Item level = the gear level (1 to 100); up to three distinct affixes, each more likely the higher the level (none at all is common on poor
    /// gear), each valued between its range's ends by the level and a roll.
    /// </summary>
    void Roll(World world, ItemInstance item, ulong entity)
    {
        int level = Math.Clamp(item.GearLevel, 1, 100);
        var table = item.Type == FcsRecordType.Weapon ? Affixes.Weapon : Affixes.Armour;
        var pool = table.ToList();
        var rolled = new List<Affix>();
        ulong counter = (ulong)world.Tick;

        for (int k = 0; k < 3 && pool.Count > 0; k++)
        {
            float chance = settings.AffixChance * (level / 100f) - k * 0.2f;
            if (Rng.Float(Rng.Hash(world.Seed, entity, purpose, counter++)) >= chance) break;

            var kind = pool[Rng.Int(Rng.Hash(world.Seed, entity, purpose, counter++), pool.Count)];
            pool.Remove(kind);
            float t = level / 100f * (0.5f + 0.5f * Rng.Float(Rng.Hash(world.Seed, entity, purpose, counter++)));
            rolled.Add(new(kind, MathF.Round(kind.Min + (kind.Max - kind.Min) * t)));
        }

        Affixes.Write(item, level, rolled);
    }

    #endregion

    #region Effects

    void ApplyAll(World world)
    {
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { Stats: { } stats } cold) continue;

            stats.Modifiers.RemoveAll(ModifierKey);
            if (settings.Enabled) Apply(cold.Inventory, stats);
        }
    }

    static void Apply(Inventory? inventory, Simulation.Bodies.CharacterStats stats)
    {
        var amounts = new Dictionary<StatsEnumerated, float>();
        var armourPoints = new Dictionary<StatsEnumerated, float>();
        foreach (var item in Affixes.Equipped(inventory))
        {
            foreach (var affix in Affixes.Of(item))
            {
                var stat = affix.Kind.Stat;
                if (affix.Kind.Effect == AffixEffect.StatPoints && item.Type == FcsRecordType.Armour)
                    armourPoints[stat] = armourPoints.GetValueOrDefault(stat) + affix.Value;
                else if (affix.Kind.Effect == AffixEffect.StatPoints) amounts[stat] = amounts.GetValueOrDefault(stat) + affix.Value;
                else if (affix.Kind.Effect == AffixEffect.StatPercent)
                    amounts[stat] = amounts.GetValueOrDefault(stat) + stats.Stored(stat) * affix.Value / 100;
            }
        }

        foreach (var (stat, points) in armourPoints) amounts[stat] = amounts.GetValueOrDefault(stat) + Math.Min(points, ArmourPointCap);
        foreach (var (stat, amount) in amounts) stats.Modifiers.Set(ModifierKey, stat, amount);
    }

    #endregion

    static string Line(ItemInstance item)
    {
        var affixes = Affixes.Of(item);
        string text = affixes.Count > 0 ? string.Join(", ", affixes.Select(a => a.Text)) : "no affixes";
        return $"{item.Name} (level {Affixes.LevelOf(item)}): {text}";
    }

    /// <summary>Posts the affixes of the character's equipped gear.</summary>
    static void Show(World world, CharacterId id)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(id, out int slot) || table.Cold(slot) is not { } cold) return;

        var lines = Affixes.Equipped(cold.Inventory).Where(Affixes.Rolled).Select(Line).ToList();
        world.Post(lines.Count == 0 ? $"{cold.Name} wears no gear with affixes." : $"{cold.Name}: " + string.Join("; ", lines));
    }
}
