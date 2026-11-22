using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Items;

namespace Meitou.KenshiAffixes;

/// <summary>A deterministic draw stream for one roll (<see cref="Rng.Hash"/> over the item's key and a counter).</summary>
sealed class RollStream(ulong seed, ulong entity, RngPurpose purpose)
{
    ulong _counter;

    /// <summary>A whole number from <paramref name="low"/> to <paramref name="high"/>, both included.</summary>
    public int Between(int low, int high) => low + Rng.Int(Rng.Hash(seed, entity, purpose, _counter++), high - low + 1);

    /// <summary>A number in [0, 1).</summary>
    public float Unit() => Rng.Float(Rng.Hash(seed, entity, purpose, _counter++));
}

/// <summary>An item's gear values the affix rules test (the original's frozen baseline), and whether it is armour.</summary>
sealed record Baseline(bool Armour, float[] Values)
{
    public float this[AffixField field] => (int)field < Values.Length ? Values[(int)field] : 0;
}

/// <summary>
/// The original's roll rules (AffixRules.h, KenshiAffixes.cpp): quality bands, item levels, candidate filtering, weighted picks and values.
/// </summary>
static class Rolling
{
    const int GearFields = (int)AffixField.BluntResistance + 1;

    /// <summary>
    /// The quality band 0-5 from the gear level: armour (level + 5) / 20 (its grades 5/20/40/60/80/95), a weapon by the cut-offs 15, 30, 45, 60, 80.
    /// </summary>
    public static int Band(ItemInstance item)
    {
        int level = item.GearLevel;
        if (item.Type == FcsRecordType.Armour) return Math.Clamp((level + 5) / 20, 0, 5);

        int band = 0;
        foreach (int cut in (int[])[15, 30, 45, 60, 80])
        {
            if (level >= cut) band++;
        }

        return band;
    }

    /// <summary>A shop's source level: base + step x band (12 + 16 x band by default).</summary>
    public static int ShopLevel(int band, int baseLevel, int step) =>
        Math.Clamp(Math.Clamp(baseLevel, 1, 100) + Math.Clamp(step, 0, 20) * Math.Clamp(band, 0, 5), 1, 100);

    /// <summary>
    /// An NPC's source level: the mean of its physical stats ((strength + dexterity + toughness) / 3) and its combat level (the best of
    /// (melee attack + melee defence) / 2, martial arts and the bow skill), from 1 to 100; trained values only.
    /// </summary>
    public static float NpcLevel(Simulation.Bodies.CharacterStats stats)
    {
        float S(Data.Gameplay.Bodies.StatsEnumerated s) => stats.Stored(s);

        float physical = (S(Data.Gameplay.Bodies.StatsEnumerated.Strength) + S(Data.Gameplay.Bodies.StatsEnumerated.Dexterity)
            + S(Data.Gameplay.Bodies.StatsEnumerated.Toughness)) / 3;
        float melee = (S(Data.Gameplay.Bodies.StatsEnumerated.MeleeAttack) + S(Data.Gameplay.Bodies.StatsEnumerated.MeleeDefence)) / 2;
        float other = Math.Max(S(Data.Gameplay.Bodies.StatsEnumerated.MartialArts), S(Data.Gameplay.Bodies.StatsEnumerated.Crossbows));
        float combat = Math.Max(melee, other);
        float level = (physical + combat) / 2;
        return float.IsFinite(level) ? Math.Clamp(level, 1, 100) : 1;
    }

    /// <summary>The item's values the candidates test: a weapon's damage multipliers, an armour piece's stat multipliers and resistances.</summary>
    public static Baseline? BaselineOf(ItemInstance item, CombatDatabase combat)
    {
        var values = new float[GearFields];
        if (item.Type == FcsRecordType.Weapon && combat.Weapon(item.Record) is { } w)
        {
            values[(int)AffixField.CutDamage] = w.CutDamageMultiplier;
            values[(int)AffixField.BluntDamage] = w.BluntDamageMultiplier;
            values[(int)AffixField.BleedDamage] = w.BleedMult;
            return new(false, values);
        }

        if (item.Type != FcsRecordType.Armour || combat.Armour(item.Record) is not { } a) return null;

        var piece = ArmourPiece.Create(a, ArmourPiece.QualityOf(a, item.Level), ArmourPiece.Graded(a, item.Level));
        values[(int)AffixField.AthleticsMult] = a.AthleticsMult;
        values[(int)AffixField.StealthMult] = a.StealthMult;
        values[(int)AffixField.DexterityMult] = a.DexterityMult;
        values[(int)AffixField.CutResistance] = piece.CutDefence;
        values[(int)AffixField.BluntResistance] = piece.BluntDefence;
        return new(true, values);
    }

    /// <summary>Whether <paramref name="kind"/> may roll on gear of <paramref name="baseline"/> at item level <paramref name="level"/>.</summary>
    static bool Applies(AffixKind kind, Baseline baseline, int level)
    {
        if (!kind.Fits(baseline.Armour) || level < kind.MinLevel) return false;
        if (kind.Positive && baseline[kind.Field] <= 0) return false;
        return kind.Op != AffixOp.Resistance || baseline[kind.Field] is > 0 and < 1;
    }

    /// <summary>How far along its range an affix is at <paramref name="level"/>: 0 to 1, raised to 1.35 but for the combat effects.</summary>
    static float Strength(AffixKind kind, int level)
    {
        float t = kind.MinLevel > 1 ? (level - kind.MinLevel + 1f) / (101 - kind.MinLevel) : (level - 1f) / 99;
        t = Math.Clamp(t, 0, 1);
        return kind.Op == AffixOp.Combat ? t : MathF.Pow(t, 1.35f);
    }

    static float Value(AffixKind kind, int level, float jitter)
    {
        float v = kind.Low + (kind.High - kind.Low) * Strength(kind, level) * jitter;
        return kind.Whole ? Math.Max(1, MathF.Floor(v + 0.5f)) : v;
    }

    /// <summary>
    /// An ordinary roll: item level = round(source) + a jitter of -5 to +3 (1-100); the band's count of distinct affixes, each picked by weight
    /// among those that apply, valued at low + (high - low) x strength x (0.9 to 1).
    /// </summary>
    public static (int Level, List<Affix> Affixes) Generate(Baseline baseline, int band, float source, RollStream rng)
    {
        int level = Math.Clamp((int)MathF.Floor(source + 0.5f) + rng.Between(AffixTable.LevelLow, AffixTable.LevelHigh), 1, 100);
        var candidates = AffixTable.All.Where(k => Applies(k, baseline, level)).ToList();
        var slots = AffixTable.Slots[Math.Clamp(band, 0, 5)];
        int count = Math.Min(candidates.Count, rng.Between(slots.Low, slots.High));

        var affixes = new List<Affix>();
        for (int i = 0; i < count; i++)
        {
            var kind = Pick(candidates, baseline.Armour, rng);
            candidates.Remove(kind);
            affixes.Add(new(kind, Value(kind, level, 0.9f + 0.1f * rng.Unit())));
        }

        return (level, affixes);
    }

    static AffixKind Pick(List<AffixKind> candidates, bool armour, RollStream rng)
    {
        int ticket = rng.Between(1, candidates.Sum(k => k.WeightFor(armour)));
        foreach (var kind in candidates)
        {
            ticket -= kind.WeightFor(armour);
            if (ticket <= 0) return kind;
        }

        return candidates[^1];
    }

    /// <summary>
    /// An authored roll (a fixed NPC's or a Meitou's): no jitter; the affixes in <paramref name="priorities"/> order that apply, at most the band's
    /// most and three, valued at their level's strength.
    /// </summary>
    public static (int Level, List<Affix> Affixes) Fixed(Baseline baseline, int band, float source, IEnumerable<string> priorities)
    {
        int level = Math.Clamp((int)MathF.Floor(source + 0.5f), 1, 100);
        int most = Math.Min(3, AffixTable.Slots[Math.Clamp(band, 0, 5)].High);
        var affixes = new List<Affix>();
        foreach (string id in priorities)
        {
            if (AffixTable.Find(id) is not { } kind || affixes.Any(a => a.Kind == kind) || !Applies(kind, baseline, level)) continue;
            if (affixes.Count >= most) break;

            affixes.Add(new(kind, Value(kind, level, 1)));
        }

        return (level, affixes);
    }
}
