using System.Text;
using Meitou.Simulation;

namespace Meitou.HappyFamilies;

/// <summary>
/// A child's family record: its parents (lasting ids, 0 for none) and their names when it was born, and while it grows the game day it comes of
/// age, its slider values as a child and as an adult, and its body parts' adult maximum health.
/// </summary>
sealed class Lineage
{
    public ulong Mother, Father;
    public string MotherName = "", FatherName = "";

    /// <summary>
    /// The game day it becomes an adult; NaN once it is one.
    /// </summary>
    public double ComeOfAge = double.NaN;

    public Dictionary<string, float> ChildSliders = new(StringComparer.Ordinal);
    public Dictionary<string, float> AdultSliders = new(StringComparer.Ordinal);

    /// <summary>
    /// The adult maximum health by LOCATIONAL_DAMAGE string id (the parents' average); kept after it grows up.
    /// </summary>
    public Dictionary<string, float> AdultParts = new(StringComparer.Ordinal);

    /// <summary>
    /// The growth the look was last drawn at (not saved: NaN after a load, so the first pass draws it).
    /// </summary>
    public float DrawnGrowth = float.NaN;

    public bool Growing => !double.IsNaN(ComeOfAge);
}

/// <summary>
/// A mother's pregnancy: when it ends, her stomach slider before it, the father (lasting id), whether she said she is pregnant, and the
/// stomach value last drawn (not saved).
/// </summary>
sealed class Pregnancy
{
    public double Ends;
    public float StomachBefore;
    public ulong Father;
    public bool Said;
    public float DrawnStomach = float.NaN;
}

/// <summary>
/// Everything the mod keeps, by lasting id, and its save format (binary, versioned).
/// </summary>
sealed class FamilyBook
{
    const int Version = 1;

    public readonly Dictionary<ulong, Lineage> Lineages = [];
    public readonly Dictionary<ulong, Pregnancy> Pregnancies = [];

    /// <summary>
    /// The game day before which a woman whose last try failed tries no more.
    /// </summary>
    public readonly Dictionary<ulong, double> NextTry = [];

    public void Clear()
    {
        Lineages.Clear();
        Pregnancies.Clear();
        NextTry.Clear();
    }

    /// <summary>
    /// Everything that decides tries and births, by id and key order (names and drawn values left out: display only).
    /// </summary>
    public void Hash(ref StateHasher h)
    {
        h.Add(Lineages.Count);
        foreach (var (id, l) in Lineages.OrderBy(p => p.Key))
        {
            h.Add(id);
            h.Add(l.Mother);
            h.Add(l.Father);
            h.Add(l.ComeOfAge);
            HashFloats(ref h, l.AdultSliders);
            HashFloats(ref h, l.AdultParts);
        }

        h.Add(Pregnancies.Count);
        foreach (var (id, p) in Pregnancies.OrderBy(p => p.Key))
        {
            h.Add(id);
            h.Add(p.Ends);
            h.Add(p.StomachBefore);
            h.Add(p.Father);
            h.Add(p.Said);
        }

        h.Add(NextTry.Count);
        foreach (var (id, day) in NextTry.OrderBy(p => p.Key))
        {
            h.Add(id);
            h.Add(day);
        }
    }

    static void HashFloats(ref StateHasher h, Dictionary<string, float> values)
    {
        h.Add(values.Count);
        foreach (var (key, value) in values.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            h.Add(Rng.StableHash(key));
            h.Add(value);
        }
    }

    public void Write(Stream stream)
    {
        using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        w.Write(Version);

        w.Write(Lineages.Count);
        foreach (var (id, l) in Lineages.OrderBy(p => p.Key)) WriteLineage(w, id, l);

        w.Write(Pregnancies.Count);
        foreach (var (id, p) in Pregnancies.OrderBy(p => p.Key))
        {
            w.Write(id);
            w.Write(p.Ends);
            w.Write(p.StomachBefore);
            w.Write(p.Father);
            w.Write(p.Said);
        }

        w.Write(NextTry.Count);
        foreach (var (id, day) in NextTry.OrderBy(p => p.Key))
        {
            w.Write(id);
            w.Write(day);
        }
    }

    public void Read(Stream stream)
    {
        using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (r.ReadInt32() != Version) return;

        Clear();
        for (int n = r.ReadInt32(); n > 0; n--) Lineages[r.ReadUInt64()] = ReadLineage(r);

        for (int n = r.ReadInt32(); n > 0; n--)
            Pregnancies[r.ReadUInt64()] = new Pregnancy { Ends = r.ReadDouble(), StomachBefore = r.ReadSingle(), Father = r.ReadUInt64(), Said = r.ReadBoolean() };

        for (int n = r.ReadInt32(); n > 0; n--) NextTry[r.ReadUInt64()] = r.ReadDouble();
    }

    static void WriteLineage(BinaryWriter w, ulong id, Lineage l)
    {
        w.Write(id);
        w.Write(l.Mother);
        w.Write(l.Father);
        w.Write(l.MotherName);
        w.Write(l.FatherName);
        w.Write(l.ComeOfAge);
        WriteFloats(w, l.ChildSliders);
        WriteFloats(w, l.AdultSliders);
        WriteFloats(w, l.AdultParts);
    }

    static Lineage ReadLineage(BinaryReader r) => new()
    {
        Mother = r.ReadUInt64(),
        Father = r.ReadUInt64(),
        MotherName = r.ReadString(),
        FatherName = r.ReadString(),
        ComeOfAge = r.ReadDouble(),
        ChildSliders = ReadFloats(r),
        AdultSliders = ReadFloats(r),
        AdultParts = ReadFloats(r),
    };

    static void WriteFloats(BinaryWriter w, Dictionary<string, float> values)
    {
        w.Write(values.Count);
        foreach (var (key, value) in values.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            w.Write(key);
            w.Write(value);
        }
    }

    static Dictionary<string, float> ReadFloats(BinaryReader r)
    {
        var values = new Dictionary<string, float>(StringComparer.Ordinal);
        for (int n = r.ReadInt32(); n > 0; n--) values[r.ReadString()] = r.ReadSingle();
        return values;
    }
}
