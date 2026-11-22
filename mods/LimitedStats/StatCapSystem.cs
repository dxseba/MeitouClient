using System.Text;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;

namespace Meitou.LimitedStats;

/// <summary>
/// Every 15th tick in the serial slow phase: rolls a talent for each character without one (kept by lasting id and saved), works out each
/// character's caps and XP rates (<see cref="CapEntry"/>), pulls stats above their cap back to it (animals excepted, as the original), and publishes
/// the entries for the experience filter and the interface. The filter (<see cref="Gain"/>) is a pure read of the published entries and of the
/// gaining character's own stat.
/// </summary>
sealed class StatCapSystem(LimitedStatsSettings settings, TalentConfig config, TalentBuilder builder, CapBoard board, RngPurpose purpose)
    : IExperienceFilter, IModSaveData, IStateHashed, IWorldReady
{
    const int Version = 1;
    const int ScanTicks = 15;
    const int PruneTicks = 30 * 60 * 30;

    readonly Dictionary<ulong, Talent> _talents = [];

    public void Ready(World world, bool fromSave) => Prune(world);

    public void SlowWorld(World world)
    {
        if (world.Tick % ScanTicks != 0) return;
        if (world.Tick % PruneTicks == 0) Prune(world);

        var rules = settings.Snapshot();
        var entries = new Dictionary<CharacterId, CapEntry>();
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { Stats: { } stats } cold) continue;
            if (rules.PlayerOnly && !cold.IsPlayer) continue;

            var id = table.IdOf(slot);
            var entry = CapEntry.For(cold, TalentOf(world, id, cold, rules), config, rules);
            entries[id] = entry;
            if (cold.Animal is null) Clamp(stats, entry, rules);
        }

        board.Publish(entries, rules);
    }

    /// <summary>The character's talent, rolled on first sight and again when its record changes (the original's key check).</summary>
    Talent TalentOf(World world, CharacterId id, CharacterCold cold, Rules rules)
    {
        ulong lasting = world.LastingIds.GetOrGive(id).Value;
        if (_talents.TryGetValue(lasting, out var talent) && talent.Key == cold.RecordId) return talent;

        talent = builder.Build(cold, new TalentRoll(world.Seed, lasting, purpose), rules);
        _talents[lasting] = talent;
        return talent;
    }

    /// <summary>Pulls each stat above its cap back to it, unless over-cap protection leaves it (at least ProtectGap above).</summary>
    static void Clamp(CharacterStats stats, CapEntry entry, Rules rules)
    {
        foreach (var stat in StatNames.Stats)
        {
            float cap = entry.Caps[(int)stat], value = stats.Stored(stat);
            if (cap < 0 || value <= cap) continue;
            if (rules.ProtectGap >= 0 && value - cap >= rules.ProtectGap) continue;

            stats[stat] = cap;
        }
    }

    /// <summary>Drops the talents of characters the game no longer keeps.</summary>
    void Prune(World world)
    {
        var kept = world.LastingIds.KeptIds().Select(id => id.Value).ToHashSet();
        foreach (ulong id in _talents.Keys.Where(k => !kept.Contains(k)).ToList()) _talents.Remove(id);
    }

    /// <summary>
    /// The original's XP hook: no XP at or above the cap; the gain divided by the race's own XP multiplier for the stat, times the talent's rate
    /// and the player or NPC rate, trained in 0.5 steps on the core curve with the cap as its top (held there); the result handed to the game as the
    /// amount its 101 curve needs for the same rise. First aid (added past the curve, which the original's hook never sees) is left alone.
    /// </summary>
    public float Gain(World world, in ExperienceGain gain)
    {
        if (gain.Source == ExperienceSource.FirstAid || board.Read(gain.Character) is not var (entry, rules)) return gain.Amount;

        var stat = StatsInfo.StorageOf(gain.Skill);
        if (stat == StatsEnumerated.None || (int)stat >= entry.Caps.Length || entry.Caps[(int)stat] < 0) return gain.Amount;
        if (!world.Characters.TryResolveNext(gain.Character, out int slot) || world.Characters.Cold(slot)?.Stats is not { } stats) return gain.Amount;

        return CurveMath.Amount(stats.Stored(stat), gain.Amount, entry, stat, rules, gain.Source == ExperienceSource.Training);
    }

    public void Hash(ref StateHasher hasher)
    {
        hasher.Add(_talents.Count);
        foreach (var (id, talent) in _talents.OrderBy(t => t.Key))
        {
            hasher.Add(id);
            talent.Hash(ref hasher);
        }
    }

    public void Write(Stream stream)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Version);
        writer.Write(_talents.Count);
        foreach (var (id, talent) in _talents.OrderBy(t => t.Key))
        {
            writer.Write(id);
            talent.Write(writer);
        }
    }

    public void Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (reader.ReadInt32() != Version) return;

        _talents.Clear();
        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            ulong id = reader.ReadUInt64();
            _talents[id] = Talent.Read(reader);
        }
    }
}

/// <summary>The XP hook's arithmetic (<b>Verified</b>, original source <c>increaseStat_hook</c>).</summary>
static class CurveMath
{
    const float Chunk = 0.5f;
    const int Guard = 40000;

    public static float Amount(float value, float amount, CapEntry entry, StatsEnumerated stat, Rules rules, bool dummy)
    {
        float cap = entry.Caps[(int)stat];
        bool protect = rules.ProtectGap >= 0 && value > cap && value - cap >= rules.ProtectGap;
        if (value >= cap && !protect) return 0;

        float race = entry.RaceXp[(int)stat];
        if (race > 0.001f && MathF.Abs(race - 1) > 0.0001f) amount /= race;
        float scaled = amount * entry.Rates[(int)stat] * entry.XpRate;

        // A training dummy's gain keeps the game's curve, whose top is the dummy's level (not told to filters): only the rate is applied.
        if (dummy) return scaled;

        float after = protect ? CharacterStats.CoreGain(value, scaled, rules.CapMax) : Chunked(value, scaled, MathF.Min(cap, rules.CapMax));
        return AsGameAmount(value, MathF.Min(after, protect ? after : cap));
    }

    /// <summary>The original's training: 0.5 at a time on the curve topped at <paramref name="limit"/>, until spent or nothing more is added.</summary>
    static float Chunked(float value, float amount, float limit)
    {
        if (limit <= value) return value;

        for (int i = 0; amount > 0.000001f && value < limit && i < Guard; i++)
        {
            float chunk = MathF.Min(amount, Chunk);
            float next = CharacterStats.CoreGain(value, chunk, limit);
            if (next <= value) break;

            value = next;
            amount -= chunk;
        }

        return value;
    }

    /// <summary>The amount the game's own curve (top 101) needs to raise <paramref name="value"/> to <paramref name="target"/>; 0 for no rise.</summary>
    static float AsGameAmount(float value, float target)
    {
        float f = (CharacterStats.XpCap - value) / CharacterStats.XpCap;
        f *= f;
        if (!(target > value) || !(f > 0 && f <= 20)) return 0;

        return (target - value) / f;
    }
}
