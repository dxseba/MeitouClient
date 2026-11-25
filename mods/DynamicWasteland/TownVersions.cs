using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;
using Meitou.Modding;

namespace Meitou.DynamicWasteland;

/// <summary>
/// The data the simulation needs that the game data lacks, made in the data phase:
/// <list type="bullet">
/// <item>occupied versions of the towns (the original retags a town with <c>TownBase::setFaction</c>; Meitou changes a town's owner by putting
/// another TOWN in its place, <c>ApplyTownVersion</c>): every TOWN of type town or village gets one copy per other nation, its fields as they are,
/// the nation as its <c>faction</c>, no <c>override town</c> list, and its squads that name another faction swapped for the nation's own;</item>
/// <item>the faction pools the original makes at run time (civil-war rebels, emergent factions): a rebel FACTION per nation and
/// <see cref="EmergentCount"/> emergent ones, each with squad templates of its own, and their town versions (a rebel's of its nation's towns, an
/// emergent faction's of the villages).</item>
/// </list>
/// </summary>
static class TownVersions
{
    const string Prefix = "dw.";

    /// <summary>The emergent factions' pool (the original's <c>[EmergentFactions] MaxActive</c> 12).</summary>
    public const int EmergentCount = 12;

    /// <summary>The emergent factions' names in the data (port's).</summary>
    public static readonly string[] EmergentNames =
    [
        "The Hungry Band", "Ashen Covenant", "Dust Wardens", "Free Company of the Wastes", "The Broken Chain", "Children of the Drought",
        "Iron Remnant", "Red Sand Brotherhood", "The Unbowed", "Salt Prophets", "Black Market Union", "The Last Watch",
    ];

    /// <summary>A faction a version or a pool is made for: its id, name and squad lists.</summary>
    sealed record Owner(string Id, string Name, Func<string, IReadOnlyList<FcsReference>> Lists);

    public static bool Contested(TownType type) => type is TownType.Town or TownType.Village;

    public static string Id(string town, string faction) => Prefix + town + "." + faction;

    /// <summary>The rebel faction of a nation.</summary>
    public static string Rebels(string nation) => Prefix + "rebels." + nation;

    /// <summary>The emergent faction of pool slot <paramref name="k"/> (0 based).</summary>
    public static string Emergent(int k) => Prefix + "emergent." + k.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static bool IsPool(string faction) => faction.StartsWith(Prefix, StringComparison.Ordinal);

    public static void Add(ModDataContext context)
    {
        var data = context.Data;
        var factions = data.OfType(FcsRecordType.Faction).ToDictionary(r => r.StringId, r => r, StringComparer.Ordinal);
        var towns = data.OfType(FcsRecordType.Town)
            .Where(t => Contested((TownType)t.GetInt("type")) && OwnerOf(t) is { } f && factions.ContainsKey(f))
            .OrderBy(t => t.StringId, StringComparer.Ordinal)
            .ToList();
        var nations = towns
            .Select(t => OwnerOf(t)!)
            .Distinct()
            .Where(f => f != FactionData.PlayerId && !factions[f].GetBool("not real"))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        int added = 0;
        foreach (var nation in nations)
        {
            var owner = Loaded(data, factions[nation]);
            foreach (var town in towns.Where(t => OwnerOf(t) != nation)) added += Copy(context, town, owner);
        }

        foreach (var nation in nations)
        {
            var rebels = Pool(context, factions[nation], Rebels(nation), factions[nation].Name + " Rebels");
            foreach (var town in towns.Where(t => OwnerOf(t) == nation)) added += Copy(context, town, rebels);
        }

        if (EmergentBase(factions, nations) is { } source)
        {
            for (int k = 0; k < EmergentCount; k++)
            {
                var emergent = Pool(context, source, Emergent(k), EmergentNames[k]);
                foreach (var town in towns.Where(t => (TownType)t.GetInt("type") == TownType.Village)) added += Copy(context, town, emergent);
            }
        }

        context.Log($"Dynamic Wasteland: {added} town versions for {nations.Count} nations, their rebels and {EmergentCount} emergent factions");
    }

    static string? OwnerOf(GameRecord town) => town.GetReferences("faction") is [var first, ..] ? first.TargetStringId : null;

    /// <summary>A faction of the data as an owner.</summary>
    static Owner Loaded(GameDatabase data, GameRecord faction) =>
        new(faction.StringId, faction.Name, list => faction.GetReferences(list).Where(r => data.Find(r.TargetStringId) is not null).Select(Ref).ToList());

    /// <summary>The emergent factions' model: the first faction named for bandits, else the first nation.</summary>
    static GameRecord? EmergentBase(Dictionary<string, GameRecord> factions, List<string> nations) =>
        factions.Values.Where(f => f.Name.Contains("Bandit", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f.StringId, StringComparer.Ordinal)
            .FirstOrDefault() ?? (nations.Count > 0 ? factions[nations[0]] : null);

    /// <summary>
    /// A pool FACTION <paramref name="id"/> copied from <paramref name="source"/> under <paramref name="name"/>, with copies of the source's first
    /// three squad templates naming the new faction as its squads, residents and bar squads.
    /// </summary>
    static Owner Pool(ModDataContext context, GameRecord source, string id, string name)
    {
        var data = context.Data;
        var faction = context.Add(id, FcsRecordType.Faction, name);
        CopyFields(source, faction);
        foreach (var list in source.ReferenceLists)
        {
            if (list is "squads" or "residents" or "bar squads" or "campaigns") continue;

            faction.References[list] = source.GetReferences(list).Select(Ref).ToList();
        }

        var squads = new List<FcsReference>();
        var templates = source.GetReferences("squads").Select(r => data.Find(r.TargetStringId))
            .Where(r => r is { Type: FcsRecordType.SquadTemplate }).Take(3).ToList();
        for (int k = 0; k < templates.Count; k++)
        {
            var template = templates[k]!;
            string squadId = id + ".squad" + k.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var squad = context.Add(squadId, FcsRecordType.SquadTemplate, name + " " + template.Name);
            CopyFields(template, squad);
            foreach (var list in template.ReferenceLists) squad.References[list] = template.GetReferences(list).Select(Ref).ToList();
            squad.References["faction"] = [new FcsReference(id, 0, 0, 0)];
            squads.Add(new FcsReference(squadId, 1, 0, 0));
        }

        foreach (var list in new[] { "squads", "residents", "bar squads" }) faction.References[list] = [.. squads];
        return new(id, name, _ => squads);
    }

    static void CopyFields(GameRecord from, FcsRecord to)
    {
        foreach (var (k, v) in from.Bools) to.Bools[k] = v;
        foreach (var (k, v) in from.Floats) to.Floats[k] = v;
        foreach (var (k, v) in from.Ints) to.Ints[k] = v;
        foreach (var (k, v) in from.Vector3S) to.Vector3S[k] = v;
        foreach (var (k, v) in from.Vector4S) to.Vector4S[k] = v;
        foreach (var (k, v) in from.Strings) to.Strings[k] = v;
        foreach (var (k, v) in from.Filenames) to.Filenames[k] = v;
    }

    /// <summary>One version: <paramref name="town"/> held by <paramref name="owner"/>; 0 when it exists already.</summary>
    static int Copy(ModDataContext context, GameRecord town, Owner owner)
    {
        string id = Id(town.StringId, owner.Id);
        if (context.Data.Find(id) is not null) return 0;

        var copy = context.Add(id, FcsRecordType.Town, town.Name + " (DW " + owner.Name + ")");
        CopyFields(town, copy);
        foreach (var list in town.ReferenceLists)
        {
            if (list is "faction" or "override town") continue;

            var refs = list is "residents" or "roaming squads" or "bar squads"
                ? Squads(context.Data, town.GetReferences(list), owner, list)
                : town.GetReferences(list).Select(Ref).ToList();
            if (refs.Count > 0) copy.References[list] = refs;
        }

        copy.References["faction"] = [new FcsReference(owner.Id, 0, 0, 0)];
        return 1;
    }

    /// <summary>
    /// A squad list of the version: the town's squads that name no faction or the owner stay; those naming another faction are dropped, and when
    /// any were the owner's own list of that kind takes their place (its <c>residents</c>, <c>squads</c> or <c>bar squads</c>).
    /// </summary>
    static List<FcsReference> Squads(GameDatabase data, IReadOnlyList<GameReference> refs, Owner owner, string list)
    {
        var kept = refs.Where(r => SquadFaction(data, r.TargetStringId) is not { } f || f == owner.Id).Select(Ref).ToList();
        if (kept.Count == refs.Count) return kept;

        string own = list switch
        {
            "residents" => "residents",
            "roaming squads" => "squads",
            _ => "bar squads",
        };
        kept.AddRange(owner.Lists(own));
        return kept;
    }

    static string? SquadFaction(GameDatabase data, string template) =>
        data.Find(template) is { } squad && squad.GetReferences("faction") is [var first, ..] ? first.TargetStringId : null;

    static FcsReference Ref(GameReference r) => new(r.TargetStringId, r.Values.Value0, r.Values.Value1, r.Values.Value2);
}
