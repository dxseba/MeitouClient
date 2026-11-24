using System.Globalization;
using Meitou.Data.Fcs;
using Meitou.Data.Save;

namespace Meitou.ShopCounterRestock;

/// <summary>
/// One counter as the original plugin saved it: the counter's and the worker's handles, the whitelist and the switch.
/// </summary>
internal sealed record SavedCounter(Hand Counter, Hand Worker, List<(string Item, int Target)> Items, bool Enabled);

/// <summary>
/// Reads the original plugin's counters from a save folder, read only (README "Original saves"): its <c>PlayerInterface::serialise</c> keys in the
/// save's CAMERA record of <c>quick.save</c> (the handle list <c>ShopCounterRestock.v1.counters</c>, and per index i the handle
/// <c>ShopCounterRestock.v1.&lt;i&gt;.worker</c> and the strings <c>.items</c> and <c>.enabled</c>), and the counters' site ids from the zone
/// files' building states (their <c>handle</c>).
/// </summary>
internal static class OriginalSave
{
    const string Prefix = "ShopCounterRestock.v1.";
    const string List = Prefix + "counters";
    const int MaxCounters = 4096;

    public static List<SavedCounter> Read(string folder)
    {
        string quick = Path.Combine(folder, "quick.save");
        if (!File.Exists(quick)) return [];

        var camera = FcsReader.ReadFile(quick).Records.FirstOrDefault(r => r.Ints.ContainsKey(List + "0I"));
        return camera is null ? [] : Counters(camera);
    }

    static List<SavedCounter> Counters(FcsRecord camera)
    {
        var counters = new List<SavedCounter>();
        for (int i = 0; i < MaxCounters && camera.Ints.ContainsKey($"{List}{i}I"); i++)
        {
            string key = Prefix + i.ToString(CultureInfo.InvariantCulture);
            if (Items(camera.Strings.GetValueOrDefault(key + ".items")) is not { } items) continue;

            bool enabled = camera.Strings.GetValueOrDefault(key + ".enabled") == "1";
            counters.Add(new SavedCounter(Hand.Read(camera, $"{List}{i}"), Hand.Read(camera, key + ".worker"), items, enabled));
        }

        return counters;
    }

    /// <summary>
    /// The original's whitelist text: the entry count on the first line, then one line per item, <c>&lt;id length&gt;:&lt;id&gt; &lt;target&gt;</c>
    /// (the id read by its length, so any bytes it holds survive). Null when malformed.
    /// </summary>
    public static List<(string, int)>? Items(string? text)
    {
        if (text is null) return null;

        int at = text.IndexOf('\n');
        if (at < 0 || !int.TryParse(text.AsSpan(0, at), NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count is < 0 or > 4096)
            return null;

        var items = new List<(string, int)>();
        for (int k = 0; k < count; k++)
        {
            if (Entry(text, ref at) is not { } entry) return null;

            items.Add(entry);
        }

        return items;
    }

    static (string, int)? Entry(string text, ref int at)
    {
        int colon = text.IndexOf(':', at + 1);
        if (colon < 0 || !int.TryParse(text.AsSpan(at + 1, colon - at - 1), NumberStyles.None, CultureInfo.InvariantCulture, out int length))
            return null;

        int space = colon + 1 + length;
        int end = space < text.Length ? text.IndexOf('\n', space) : -1;
        if (end < 0 || text[space] != ' ' || !int.TryParse(text.AsSpan(space + 1, end - space - 1), out int target)) return null;

        at = end;
        return (text.Substring(colon + 1, length), target);
    }

    /// <summary>
    /// The site ids of the wanted building handles, from the zone files' building states.
    /// </summary>
    public static Dictionary<Hand, string> SiteIds(string folder, IReadOnlyCollection<Hand> wanted)
    {
        var ids = new Dictionary<Hand, string>();
        string zones = Path.Combine(folder, "zone");
        if (wanted.Count == 0 || !Directory.Exists(zones)) return ids;

        foreach (string file in Directory.EnumerateFiles(zones, "*.zone").Order(StringComparer.Ordinal))
        {
            foreach (var state in FcsReader.ReadFile(file).Records.Where(r => r.RecordType == FcsRecordType.GamestateBuilding))
            {
                var hand = Hand.Read(state, "handle");
                if (wanted.Contains(hand)) ids.TryAdd(hand, state.StringId);
            }
        }

        return ids;
    }
}
