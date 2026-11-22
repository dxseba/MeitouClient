using Meitou.Data.Gameplay.Bodies;
using Meitou.Simulation;

namespace Meitou.LimitedStats;

/// <summary>
/// A character's talent (<b>Verified</b>, original source): its type, an XP rate per stat (1 unless set), and the caps an entry or the type set:
/// absolute caps, race-relative caps and cap-break multipliers. Rolled once per character and kept with the save.
/// </summary>
sealed class Talent
{
    public TalentType Type;
    public string Key = "";
    public float[] Rates { get; } = Enumerable.Repeat(1f, StatNames.Count).ToArray();
    public SortedDictionary<StatsEnumerated, float> Caps { get; } = [];
    public SortedDictionary<StatsEnumerated, (CapMode Mode, float Value)> RaceCaps { get; } = [];
    public SortedDictionary<StatsEnumerated, float> Breaks { get; } = [];

    /// <summary>Whether an entry or the type gave <paramref name="stat"/> a cap of its own (the "special" colour without GoldCapByValue).</summary>
    public bool OwnCap(StatsEnumerated stat) => Caps.ContainsKey(stat) || RaceCaps.ContainsKey(stat) || Breaks.ContainsKey(stat);

    public void Write(BinaryWriter w)
    {
        w.Write(Key);
        w.Write((byte)Type);
        WriteMap(w, Caps);
        WriteMap(w, Breaks);
        w.Write(RaceCaps.Count);
        foreach (var (stat, (mode, value)) in RaceCaps)
        {
            w.Write((byte)stat);
            w.Write((byte)mode);
            w.Write(value);
        }

        WriteMap(w, new SortedDictionary<StatsEnumerated, float>(StatNames.Stats.Where(s => Rates[(int)s] != 1).ToDictionary(s => s, s => Rates[(int)s])));
    }

    public static Talent Read(BinaryReader r)
    {
        var t = new Talent { Key = r.ReadString(), Type = (TalentType)r.ReadByte() };
        ReadMap(r, t.Caps);
        ReadMap(r, t.Breaks);
        int count = r.ReadInt32();
        for (int i = 0; i < count; i++) t.RaceCaps[(StatsEnumerated)r.ReadByte()] = ((CapMode)r.ReadByte(), r.ReadSingle());
        var rates = new SortedDictionary<StatsEnumerated, float>();
        ReadMap(r, rates);
        foreach (var (stat, rate) in rates)
            if ((int)stat < StatNames.Count) t.Rates[(int)stat] = rate;
        return t;
    }

    public void Hash(ref StateHasher h)
    {
        h.Add((int)Type);
        h.Add(Rng.StableHash(Key));
        foreach (var s in StatNames.Stats) h.Add(Rates[(int)s]);
        foreach (var (s, v) in Caps) { h.Add((int)s); h.Add(v); }
        foreach (var (s, (m, v)) in RaceCaps) { h.Add((int)s); h.Add((int)m); h.Add(v); }
        foreach (var (s, v) in Breaks) { h.Add((int)s); h.Add(v); }
    }

    static void WriteMap(BinaryWriter w, SortedDictionary<StatsEnumerated, float> map)
    {
        w.Write(map.Count);
        foreach (var (stat, value) in map)
        {
            w.Write((byte)stat);
            w.Write(value);
        }
    }

    static void ReadMap(BinaryReader r, SortedDictionary<StatsEnumerated, float> map)
    {
        int count = r.ReadInt32();
        for (int i = 0; i < count; i++) map[(StatsEnumerated)r.ReadByte()] = r.ReadSingle();
    }
}

/// <summary>A character's rolls: the world's seeded hash of its lasting id under the mod's purpose, one counter step per roll.</summary>
sealed class TalentRoll(ulong seed, ulong lasting, RngPurpose purpose)
{
    ulong _counter;

    ulong Next() => Rng.Hash(seed, lasting, purpose, _counter++);

    /// <summary>A float in [0, 1).</summary>
    public float Unit() => Rng.Float(Next());

    /// <summary>An int in [0, <paramref name="bound"/>).</summary>
    public int Below(int bound) => bound <= 1 ? 0 : Rng.Int(Next(), bound);
}
