using System.Globalization;
using Meitou.Data.Fcs;
using Meitou.Data.Save;
using Meitou.Simulation;

namespace Meitou.HappyFamilies;

/// <summary>
/// Family links of a save the original wrote (README "Facts"): the script kept them in each character's CHARACTER_APPEARANCE record, which the save
/// stores with the character: the string <c>FamilyTable</c> ("own;mother name;mother handle;father name;father handle[;father of the
/// current child]", "no" for none, handles as <c>hand::toString</c> writes them), the floats <c>ComeOfAgeTime</c> (also an int), <c>WHENADULT_&lt;slider&gt;</c>,
/// <c>WHENADULT_LIMB_&lt;n&gt;</c>, <c>PregnancyEndTime</c>, <c>Stomach_Original</c>, <c>NextAllowedChildCheck</c> (game days) and the bool
/// <c>bJustSaidImPregnant</c>. Read once at load when the save has no data of this port's own.
/// </summary>
sealed partial class FamilySystem
{
    const string Prefix = "WHENADULT_", LimbPrefix = "WHENADULT_LIMB_";

    bool _readOwn;

    /// <summary>
    /// <c>hand::toString</c> (<c>kenshi_x64.exe</c> 1.0.68 RVA 0x9B5750): index, serial, container, container serial and type, joined by '-'; the
    /// four unsigned, the type signed.
    /// </summary>
    static string HandleText(Hand h) => string.Join('-', (uint)h.I, (uint)h.S, (uint)h.C, (uint)h.Cs, h.Type.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Imports every loaded character's family keys; logs how many links were found and how many handles matched their own entry.
    /// </summary>
    void ImportOriginal(World world)
    {
        var table = world.Characters;
        var byHandle = new Dictionary<string, int>(StringComparer.Ordinal);
        var records = new List<(int Slot, FcsRecord Look)>();
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (table.Cold(slot)?.Save?.Source is not { Appearance: { } look } source) continue;

            byHandle.TryAdd(HandleText(source.Handle), slot);
            records.Add((slot, look));
        }

        int links = 0, ownMatched = 0;
        foreach (var (slot, look) in records)
        {
            ImportCharacter(world, slot, look, byHandle, ref links, ref ownMatched);
        }

        if (links > 0 || _book.Pregnancies.Count > 0) context.Log($"original family data: {links} lineages ({ownMatched} own handles matched), "
            + $"{_book.Pregnancies.Count} pregnancies");
    }

    void ImportCharacter(World world, int slot, FcsRecord look, Dictionary<string, int> byHandle, ref int links, ref int ownMatched)
    {
        ulong id = Lasting(world, slot);
        string[]? family = look.Strings.TryGetValue("FamilyTable", out string? text) ? text.Split(';', StringSplitOptions.RemoveEmptyEntries) : null;
        double comeOfAge = look.Floats.TryGetValue("ComeOfAgeTime", out float f) ? f
            : look.Ints.TryGetValue("ComeOfAgeTime", out int i) ? i : double.NaN;

        if (family is { Length: >= 5 } || !double.IsNaN(comeOfAge))
        {
            _book.Lineages[id] = ImportLineage(world, slot, look, family, comeOfAge, byHandle);
            links++;
            if (family is { Length: >= 5 } && byHandle.TryGetValue(family[0], out int own) && own == slot) ownMatched++;
        }

        ImportPregnancy(world, look, id, family, byHandle);
        if (look.Floats.TryGetValue("NextAllowedChildCheck", out float next)) _book.NextTry[id] = next;
    }

    Lineage ImportLineage(World world, int slot, FcsRecord look, string[]? family, double comeOfAge, Dictionary<string, int> byHandle)
    {
        var lineage = new Lineage { ComeOfAge = comeOfAge };
        if (family is { Length: >= 5 })
        {
            (lineage.MotherName, lineage.Mother) = (Entry(family[1]) ?? "", LastingOf(world, Entry(family[2]), byHandle));
            (lineage.FatherName, lineage.Father) = (Entry(family[3]) ?? "", LastingOf(world, Entry(family[4]), byHandle));
        }

        // The script also keeps the parents' last names as their own strings.
        if (lineage.MotherName.Length == 0) lineage.MotherName = look.Strings.GetValueOrDefault("MotherName", "");
        if (lineage.FatherName.Length == 0) lineage.FatherName = look.Strings.GetValueOrDefault("FatherName", "");

        var cold = world.Characters.Cold(slot)!;
        foreach (var (key, value) in look.Floats)
        {
            if (key.StartsWith(Prefix, StringComparison.Ordinal) && !key.StartsWith(LimbPrefix, StringComparison.Ordinal))
                lineage.AdultSliders[key[Prefix.Length..]] = value;
        }

        if (lineage.Growing) lineage.ChildSliders = new(Genetics.ChildSlidersOf(cold.Race), StringComparer.Ordinal);
        ImportParts(look, cold, lineage);
        return lineage;
    }

    /// <summary>
    /// <c>WHENADULT_LIMB_&lt;n&gt;</c>: the script's keys are the positions in the body's part list; taken 1-based when the lowest is 1, else 0-based.
    /// </summary>
    static void ImportParts(FcsRecord look, CharacterCold cold, Lineage lineage)
    {
        var limbs = new SortedDictionary<int, float>();
        foreach (var (key, value) in look.Floats)
        {
            if (key.StartsWith(LimbPrefix, StringComparison.Ordinal) && int.TryParse(key.AsSpan(LimbPrefix.Length), CultureInfo.InvariantCulture, out int n))
                limbs[n] = value;
        }

        if (limbs.Count == 0 || cold.Medical is not { } medical) return;

        int first = limbs.Keys.First() >= 1 ? 1 : 0;
        foreach (var (n, value) in limbs)
        {
            if (n - first is >= 0 and var k && k < medical.Parts.Count) lineage.AdultParts[medical.Parts[k].Template.StringId] = value;
        }
    }

    void ImportPregnancy(World world, FcsRecord look, ulong id, string[]? family, Dictionary<string, int> byHandle)
    {
        if (!look.Floats.TryGetValue("PregnancyEndTime", out float ends)) return;

        float stomach = look.Floats.TryGetValue("Stomach_Original", out float before) ? before : look.Floats.GetValueOrDefault("Stomach", Genetics.StomachDefault);
        _book.Pregnancies[id] = new Pregnancy
        {
            Ends = ends,
            StomachBefore = stomach,
            Father = family is { Length: >= 6 } ? LastingOf(world, Entry(family[5]), byHandle) : 0,
            Said = look.Bools.GetValueOrDefault("bJustSaidImPregnant"),
        };
    }

    static string? Entry(string value) => value == "no" ? null : value;

    static ulong LastingOf(World world, string? handle, Dictionary<string, int> byHandle) =>
        handle is not null && byHandle.TryGetValue(handle, out int slot) ? Lasting(world, slot) : 0;
}
