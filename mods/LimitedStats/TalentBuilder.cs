using Meitou.Data;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;

namespace Meitou.LimitedStats;

/// <summary>
/// Rolls a character's talent as the original does (<b>Verified</b>, original source): an entry of <c>[SpecialCharacter]</c> (by the character's
/// record id, then its name) or <c>[Stats]</c> (its STATS record, record id, then name) sets its stats, optionally rolling a random talent for the
/// rest; without one the talent is random: a slave is a Loser at SlaveLoserPercent and otherwise never one, else a type by the weights (heaviest
/// first), then its skills drawn from a random first one by summed correlation plus jitter.
/// </summary>
sealed class TalentBuilder(TalentConfig config, GameDatabase data, Action<string> log)
{
    public Talent Build(CharacterCold cold, TalentRoll roll, Rules rules)
    {
        var spec = Find(cold, out string? matched);
        var talent = spec is null ? Random(cold, roll, rules, null, [], []) : FromSpec(cold, roll, rules, spec);
        talent.Key = cold.RecordId;
        log($"talent assigned {cold.RecordId} name='{cold.Name}' = {talent.Type}{(matched is null ? "" : " (" + matched + ")")}");
        return talent;
    }

    TalentSpec? Find(CharacterCold cold, out string? matched)
    {
        string id = cold.RecordId, name = cold.Name, stats = StatsId(cold);
        (string What, Dictionary<string, TalentSpec> In, string Key)[] order =
        [
            ("special char match (id)", config.Specials, id), ("special char match (name)", config.Specials, name),
            ("override match (stats)", config.Overrides, stats), ("override match (id)", config.Overrides, id),
            ("override match (name)", config.Overrides, name),
        ];
        foreach (var (what, table, key) in order)
        {
            if (key.Length > 0 && table.TryGetValue(key, out var spec))
            {
                matched = what + " " + key;
                return spec;
            }
        }

        matched = null;
        return null;
    }

    /// <summary>The first STATS record the character's record names, "" for none.</summary>
    string StatsId(CharacterCold cold) =>
        cold.RecordId.Length > 0 && data.Find(cold.RecordId) is { } record && record.GetReferences("stats") is [var first, ..] ? first.TargetStringId : "";

    Talent FromSpec(CharacterCold cold, TalentRoll roll, Rules rules, TalentSpec spec)
    {
        var talent = new Talent();
        var explicitRate = new HashSet<StatsEnumerated>();
        var explicitCap = new HashSet<StatsEnumerated>();
        foreach (var (statName, functions) in spec.Groups)
        {
            bool all = string.Equals(statName, "all", StringComparison.OrdinalIgnoreCase);
            var stat = all ? StatsEnumerated.None : StatNames.Find(statName);
            if (!all && stat == StatsEnumerated.None)
            {
                log($"unknown stat '{statName}'");
                continue;
            }

            foreach (var s in all ? StatNames.Stats : [stat]) Apply(talent, s, functions, roll, explicitRate, explicitCap);
        }

        if (spec.RandomOthers) MergeRandom(cold, roll, rules, spec, talent, explicitRate, explicitCap);
        return talent;
    }

    static void Apply(Talent t, StatsEnumerated s, StatFunctions f, TalentRoll roll, HashSet<StatsEnumerated> rate, HashSet<StatsEnumerated> cap)
    {
        if (f.Buff is { } buff)
        {
            t.Rates[(int)s] = buff.Evaluate(roll);
            rate.Add(s);
        }

        if (f.Cap is { } c)
        {
            t.Caps[s] = c.Evaluate(roll);
            cap.Add(s);
        }

        if (f.Mode != CapMode.None && f.ModeValue is { } m)
        {
            t.RaceCaps[s] = (f.Mode, m.Evaluate(roll));
            cap.Add(s);
        }
    }

    /// <summary>
    /// <c>[rand_other_skills...]</c>: a random talent (one named type forced, several picked among, or with <c>_not</c> those weighted out) fills the
    /// rates and cap breaks of the stats the entry left unset; the talent takes its type.
    /// </summary>
    void MergeRandom(CharacterCold cold, TalentRoll roll, Rules rules, TalentSpec spec, Talent talent, HashSet<StatsEnumerated> explicitRate,
        HashSet<StatsEnumerated> explicitCap)
    {
        TalentType? forced = !spec.RandomOthersExcept && spec.RandomTypes.Count == 1 ? spec.RandomTypes[0] : null;
        var random = Random(cold, roll, rules, forced, spec.RandomOthersExcept ? [] : spec.RandomTypes,
            spec.RandomOthersExcept ? spec.RandomTypes : []);
        foreach (var s in StatNames.Stats)
        {
            if (!explicitRate.Contains(s)) talent.Rates[(int)s] = random.Rates[(int)s];
            if (!explicitCap.Contains(s) && random.Breaks.TryGetValue(s, out float b)) talent.Breaks[s] = b;
        }

        talent.Type = random.Type;
    }

    Talent Random(CharacterCold cold, TalentRoll roll, Rules rules, TalentType? forced, IReadOnlyList<TalentType> allowed,
        IReadOnlyList<TalentType> excluded)
    {
        var type = forced ?? (allowed.Count > 0 ? allowed[roll.Below(allowed.Count)] : Weighted(cold, roll, rules, excluded));
        var talent = new Talent { Type = type };
        int need = type switch
        {
            TalentType.Lazy => rules.LazySkills,
            TalentType.Loser => rules.LoserSkills,
            TalentType.Diligent => rules.DiligentSkills,
            TalentType.Genius => rules.GeniusSkills,
            TalentType.Superhuman => rules.SuperBreakCount,
            _ => 0,
        };

        var skills = need > 0 ? Draw(roll, need, rules.CorrelationJitter) : [];
        Shape(talent, skills, rules);
        return talent;
    }

    static void Shape(Talent t, List<StatsEnumerated> skills, Rules rules)
    {
        switch (t.Type)
        {
            case TalentType.Lazy:
                foreach (var s in skills) t.Rates[(int)s] = rules.LazyRate;
                break;
            case TalentType.Loser:
                foreach (var s in StatNames.Stats) t.Rates[(int)s] = rules.LoserRate;
                foreach (var s in skills) t.Rates[(int)s] = rules.LoserSkillRate;
                break;
            case TalentType.Diligent:
                foreach (var s in skills) t.Rates[(int)s] = rules.DiligentRate;
                break;
            case TalentType.Genius:
                foreach (var s in skills) t.Rates[(int)s] = rules.GeniusRate;
                foreach (var s in skills.Take(rules.GeniusBreakCount)) t.Breaks[s] = rules.GeniusBreakMult;
                break;
            case TalentType.Superhuman:
                foreach (var s in skills.Take(rules.SuperBreakCount)) t.Breaks[s] = rules.SuperBreakMult;
                break;
        }
    }

    /// <summary>A type by the weights, the excluded ones at 0; a slave rolls Loser at SlaveLoserPercent first and otherwise has its weight at 0.</summary>
    static TalentType Weighted(CharacterCold cold, TalentRoll roll, Rules rules, IReadOnlyList<TalentType> excluded)
    {
        TalentType[] types = [TalentType.Normal, TalentType.Lazy, TalentType.Diligent, TalentType.Loser, TalentType.Genius, TalentType.Superhuman];
        float[] weights = [.. rules.Weights];
        for (int i = 0; i < types.Length; i++)
            if (excluded.Contains(types[i])) weights[i] = 0;

        if (cold.SlaveState == 1)
        {
            if (roll.Unit() * 100 < rules.SlaveLoserPercent) return TalentType.Loser;

            weights[3] = 0;
        }

        float total = weights.Sum();
        if (total <= 0) total = 1;
        float left = roll.Unit() * total;
        foreach (int i in Enumerable.Range(0, types.Length).OrderByDescending(i => weights[i]))
        {
            left -= weights[i];
            if (left < 0) return types[i];
        }

        return TalentType.Normal;
    }

    /// <summary><paramref name="count"/> skills: a random first one, then each the one with the highest summed correlation to those taken plus jitter.</summary>
    List<StatsEnumerated> Draw(TalentRoll roll, int count, float jitter)
    {
        var pool = StatNames.Stats;
        count = Math.Min(count, pool.Length);
        var taken = new List<StatsEnumerated> { pool[roll.Below(pool.Length)] };
        while (taken.Count < count)
        {
            StatsEnumerated best = StatsEnumerated.None;
            float bestScore = float.MinValue;
            foreach (var candidate in pool)
            {
                if (taken.Contains(candidate)) continue;

                float score = taken.Sum(t => config.Correlation(candidate, t)) + roll.Unit() * jitter;
                if (score > bestScore) (best, bestScore) = (candidate, score);
            }

            if (best == StatsEnumerated.None) break;
            taken.Add(best);
        }

        return taken;
    }
}
