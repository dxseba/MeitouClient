using System.Runtime.CompilerServices;
using System.Text;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;

namespace Meitou.LimbToughness;

/// <summary>
/// Sets each living character's part multipliers to their baseline times 1 + Toughness x the rate, the maximum rounded to a whole number, in the
/// serial slow step: the tick a character is first seen (made, or loaded with the world) and the tick its Toughness changes, as the original's
/// hooks on part setup, medical init and load and on Toughness experience; again every 60th tick for what those miss (an animal's growth). The
/// baseline is the multiplier first seen (kept by part object, so a new world starts afresh), for an animal its size now, as its growth resets
/// the multiplier. The health is rescaled with the maximum, as the original's (floored). Each raised character's part ratios are saved (by lasting
/// id), so a load rescales its saved health from the maximum it was saved with.
/// </summary>
sealed class LimbSystem(ToughnessSettings settings, Action<string> log) : ITickSystem, IModSaveData
{
    const int Version = 1;

    /// <summary>How often saved ratios of characters the game no longer keeps are dropped (ticks).</summary>
    const int PruneEvery = 18000;

    /// <summary>The original's tolerance for a part at full baseline health, and its flooring nudge.</summary>
    const float FullWithin = 1, Nudge = 1e-4f;

    readonly ConditionalWeakTable<HealthPart, StrongBox<float>> _baseline = new();

    /// <summary>The ratios (multiplier / baseline) of each part as saved, by lasting id; each is used once, at the character's first apply.</summary>
    readonly Dictionary<LastingId, float[]> _saved = [];

    /// <summary>Per character slot: the generation and Toughness last applied (generation -1: none).</summary>
    int[] _generation = [];
    float[] _toughness = [];

    World? _world;
    bool _modeLogged;

    public void SlowWorld(World world)
    {
        _world = world;
        LogMode();

        if (world.Tick % PruneEvery == 0) Prune(world);

        bool sweep = world.Tick % 60 == 0;
        var table = world.Characters;
        Grow(table.HighWater);
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (!table.Next[slot].Alive || table.Cold(slot) is not { Medical: { Dead: false } medical, Stats: { } stats, Race: { } race } cold)
            {
                _generation[slot] = -1;
                continue;
            }

            var id = table.IdOf(slot);
            float toughness = stats.ScaledToughness;
            if (!sweep && _generation[slot] == id.Generation && _toughness[slot] == toughness) continue;

            _generation[slot] = id.Generation;
            _toughness[slot] = toughness;
            Update(world, id, cold, medical, race, toughness);
        }
    }

    /// <summary>Drops saved ratios whose character is gone for good (not among the ids the game keeps).</summary>
    void Prune(World world)
    {
        if (_saved.Count == 0) return;

        var kept = world.LastingIds.KeptIds().ToHashSet();
        foreach (var id in _saved.Keys.Where(id => !kept.Contains(id)).ToList()) _saved.Remove(id);
    }

    void LogMode()
    {
        if (_modeLogged || settings.Mode != 0) return;

        _modeLogged = true;
        log("ApplicationMode 0 (getter-only) is not available: the stored maximum is raised, as in mode 4");
    }

    void Grow(int count)
    {
        if (_generation.Length >= count) return;

        int old = _generation.Length;
        Array.Resize(ref _generation, Math.Max(count, old * 2));
        Array.Resize(ref _toughness, _generation.Length);
        Array.Fill(_generation, -1, old, _generation.Length - old);
    }

    void Update(World world, CharacterId id, CharacterCold cold, MedicalState medical, RaceData race, float toughness)
    {
        float factor = Applies(cold, race) ? Factor(toughness) : 1;
        float[]? saved = !cold.LastingId.IsNone && _saved.Remove(cold.LastingId, out var ratios) ? ratios : null;
        var parts = medical.Parts;
        for (int i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            bool excluded = part.IsRobotic(race) && !settings.RoboticParts;
            float? savedRatio = saved is not null && i < saved.Length ? saved[i] : null;
            Apply(part, BaselineOf(part, cold, medical), excluded ? 1 : factor, savedRatio);
        }

        if (factor != 1 && cold.LastingId.IsNone) world.LastingIds.GetOrGive(id);
    }

    bool Applies(CharacterCold cold, RaceData race) => (!race.IsRobot || settings.RobotRaces) && (cold.Animal is null || settings.Animals);

    /// <summary>1 + rate / 100 x toughness, 1 for a rate or toughness at or below 0 (an invalid rate is the default 0.5).</summary>
    float Factor(float toughness)
    {
        float rate = float.IsFinite(settings.PercentPerPoint) && settings.PercentPerPoint >= 0 ? settings.PercentPerPoint : 0.5f;
        return rate > 0 && float.IsFinite(toughness) && toughness > 0 ? 1 + rate / 100 * toughness : 1;
    }

    float BaselineOf(HealthPart part, CharacterCold cold, MedicalState medical)
    {
        if (cold.Animal is not null) return medical.ScaleK;

        return _baseline.GetValue(part, p => new StrongBox<float>(p.HpMultiplier)).Value;
    }

    /// <summary>
    /// Sets the part's multiplier and rescales its health from the maximum before (with <paramref name="savedRatio"/>, the maximum the save was
    /// written with; without one, a part loaded with health above its plain maximum is taken as saved with the bonus and only held to the new one).
    /// </summary>
    void Apply(HealthPart part, float baseline, float factor, float? savedRatio)
    {
        float unit = part.EffectiveBaseHp * part.BodyScale;
        if (unit <= 0 || baseline <= 0) return;

        float wanted = MathF.Floor(unit * baseline * factor + 0.5f) / unit;
        if (savedRatio is null && MathF.Abs(part.HpMultiplier - wanted) < 1e-6f) return;

        float plain = unit * baseline - part.Wear;
        float before = savedRatio is { } ratio ? unit * baseline * ratio - part.Wear : part.MaxHp;
        bool savedRaised = savedRatio is null && part.Flesh > before + FullWithin;
        if (!savedRaised && MathF.Abs(part.Flesh - plain) <= FullWithin && plain < before) part.Flesh = before;

        part.HpMultiplier = wanted;
        if (savedRaised) part.Flesh = MathF.Min(part.Flesh, part.MaxHp);
        else if (before > 0 && part.Flesh >= 0) part.Flesh = MathF.Min(MathF.Floor(part.Flesh * part.MaxHp / before + Nudge), part.MaxHp);
    }

    #region Save

    /// <summary>
    /// The raised characters' part ratios, loaded and stored off screen, by lasting id (a character without one is left out), and the loaded
    /// ratios not yet used (an NPC whose platoon has not come back since the load).
    /// </summary>
    public void Write(Stream stream)
    {
        var entries = Raised().ToList();
        var written = entries.Select(e => e.Id).ToHashSet();
        entries.AddRange(_saved.Where(e => !written.Contains(e.Key)).Select(e => (e.Key, e.Value)));
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Version);
        writer.Write(entries.Count);
        foreach (var (id, ratios) in entries)
        {
            writer.Write(id.Value);
            writer.Write(ratios.Length);
            foreach (float ratio in ratios) writer.Write(ratio);
        }
    }

    public void Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (reader.ReadInt32() != Version) return;

        _saved.Clear();
        for (int n = reader.ReadInt32(); n > 0; n--)
        {
            var id = new LastingId(reader.ReadUInt64());
            var ratios = new float[reader.ReadInt32()];
            for (int i = 0; i < ratios.Length; i++) ratios[i] = reader.ReadSingle();
            _saved[id] = ratios;
        }
    }

    IEnumerable<(LastingId Id, float[] Ratios)> Raised()
    {
        if (_world is not { } world) yield break;

        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (table.Next[slot].Alive && table.Cold(slot) is { } cold && Ratios(cold) is { } ratios) yield return (cold.LastingId, ratios);
        }

        foreach (var member in world.Platoons.All.SelectMany(p => p.Stored?.Members ?? []))
        {
            if (Ratios(member.Cold) is { } ratios) yield return (member.Cold.LastingId, ratios);
        }
    }

    /// <summary>Each part's multiplier over its baseline, when the character has a lasting id and a raised part; else null.</summary>
    float[]? Ratios(CharacterCold cold)
    {
        if (cold.LastingId.IsNone || cold.Medical is not { Dead: false } medical) return null;

        var ratios = new float[medical.Parts.Count];
        bool raised = false;
        for (int i = 0; i < ratios.Length; i++)
        {
            var part = medical.Parts[i];
            float baseline = cold.Animal is not null ? medical.ScaleK : _baseline.TryGetValue(part, out var box) ? box.Value : 0;
            ratios[i] = baseline > 0 ? part.HpMultiplier / baseline : 1;
            raised |= MathF.Abs(ratios[i] - 1) > 1e-6f;
        }

        return raised ? ratios : null;
    }

    #endregion
}
