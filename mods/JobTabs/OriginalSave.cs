using System.Globalization;
using Meitou.Data.Gameplay.Ai;
using Meitou.Data.Save;
using Meitou.Simulation;
using Meitou.Simulation.Production;
using Meitou.Simulation.Saving;

namespace Meitou.JobTabs;

/// <summary>
/// The original's keys in a character's orders record (<c>OrdersReceiver::serialise</c>, <b>Verified</b> decompile and DLL strings): int
/// <c>jt_v</c> 1, int <c>jt_active</c>, and per job i of each other tab t the int <c>jt&lt;t&gt;_pjob&lt;i&gt;</c> (task type), the handle
/// <c>jt&lt;t&gt;_pjobT&lt;i&gt;</c> (target; five ints TYPE, C, CS, I, S as the game writes a handle, <b>Observed</b>) and the string
/// <c>jt&lt;t&gt;_txt&lt;i&gt;</c> (the job's tooltip name). Written through <see cref="CharacterSaveFields"/> so a save taken to the original keeps
/// the tabs, and read back for a character the port's own data does not know (a save from the original).
/// </summary>
static class OriginalSave
{
    const CharacterSaveRecord Orders = CharacterSaveRecord.Orders;
    const string Version = "jt_v", Active = "jt_active";

    static readonly string[] HandParts = ["TYPE", "C", "CS", "I", "S"];

    /// <summary>The original reads a tab number above 8 as tab 0.</summary>
    const int MostTab = 8;

    static string Key(int tab, string name, int i) =>
        "jt" + tab.ToString(CultureInfo.InvariantCulture) + "_" + name + i.ToString(CultureInfo.InvariantCulture);

    /// <summary>Writes <paramref name="tabs"/> of <paramref name="who"/> under the original's keys, dropping entries a shorter list left.</summary>
    public static void Write(World world, CharacterId who, Tabs tabs, Func<PermaJob, string> name)
    {
        var fields = world.SaveFields;
        fields.SetInt(who, Orders, Version, 1);
        fields.SetInt(who, Orders, Active, tabs.Active);
        for (int t = 0; t < tabs.Lists.Length; t++)
        {
            var list = t == tabs.Active ? [] : tabs.Lists[t];
            for (int i = 0; i < list.Count; i++) WriteJob(world, who, t, i, list[i], name);
            RemoveFrom(fields, who, t, list.Count);
        }
    }

    static void WriteJob(World world, CharacterId who, int tab, int i, KeptJob kept, Func<PermaJob, string> name)
    {
        var fields = world.SaveFields;
        var job = Restore(world, kept);
        fields.SetInt(who, Orders, Key(tab, "pjob", i), (int)job.Type);

        var hand = HandOf(world, job);
        int[] parts = [hand.Type, hand.C, hand.Cs, hand.I, hand.S];
        for (int p = 0; p < parts.Length; p++) fields.SetInt(who, Orders, Key(tab, "pjobT", i) + HandParts[p], parts[p]);

        fields.SetString(who, Orders, Key(tab, "txt", i), name(job));
    }

    /// <summary>Removes the keys of jobs <paramref name="from"/> on of a tab (what an earlier write or the loaded save held).</summary>
    static void RemoveFrom(CharacterSaveFields fields, CharacterId who, int tab, int from)
    {
        for (int i = from; fields.Int(who, Orders, Key(tab, "pjob", i)) is not null; i++)
        {
            fields.Remove(who, Orders, Key(tab, "pjob", i));
            foreach (string part in HandParts) fields.Remove(who, Orders, Key(tab, "pjobT", i) + part);
            fields.Remove(who, Orders, Key(tab, "txt", i));
        }
    }

    /// <summary>
    /// A job's target handle: a character target's as its save named it, else the handle the job was loaded with (null for a job given since).
    /// </summary>
    static Hand HandOf(World world, PermaJob job)
    {
        if (job.Character.IsNone || !world.Characters.TryResolveNext(job.Character, out int slot)) return job.Saved;

        return world.Characters.Cold(slot)?.Save?.Source.Handle is { IsNull: false } hand ? hand : job.Saved;
    }

    public static PermaJob Restore(World world, KeptJob kept) =>
        kept.Target == 0 ? kept.Job : kept.Job with { Character = world.LastingIds.Find(new LastingId(kept.Target)) };

    /// <summary>
    /// The tabs the loaded save holds for <paramref name="who"/> under the original's keys, or null without <c>jt_v</c> 1. A character target is
    /// matched by its save handle; another target keeps only its handle (the job is written back as it was).
    /// </summary>
    public static Tabs? Read(World world, CharacterId who, int count)
    {
        var fields = world.SaveFields;
        if (fields.Int(who, Orders, Version) != 1) return null;

        int active = fields.Int(who, Orders, Active) ?? 0;
        var tabs = new Tabs(count) { Active = active is < 0 or > MostTab || active >= count ? 0 : active };
        var targets = SavedCharacters(world);
        for (int t = 0; t <= MostTab && t < count; t++)
        {
            if (t != tabs.Active) ReadTab(world, who, t, tabs.Lists[t], targets);
        }

        return tabs;
    }

    static void ReadTab(World world, CharacterId who, int tab, List<KeptJob> list, Dictionary<(int, int), CharacterId> targets)
    {
        var fields = world.SaveFields;
        for (int i = 0; fields.Int(who, Orders, Key(tab, "pjob", i)) is { } type; i++)
        {
            var parts = HandParts.Select(p => fields.Int(who, Orders, Key(tab, "pjobT", i) + p)).ToArray();
            if (parts.Any(p => p is null)) continue;   // the original skips a job whose handle does not read

            var hand = new Hand(parts[0]!.Value, parts[1]!.Value, parts[2]!.Value, parts[3]!.Value, parts[4]!.Value);
            var job = new PermaJob((TaskType)type, null, hand);
            ulong target = 0;
            if (!hand.IsNull && targets.TryGetValue((hand.C, hand.I), out var character))
            {
                job = job with { Character = character };
                target = world.LastingIds.GetOrGive(character).Value;
            }

            list.Add(new(job, target));
        }
    }

    /// <summary>The loaded characters by their save handle's (container, slot).</summary>
    static Dictionary<(int, int), CharacterId> SavedCharacters(World world)
    {
        var table = world.Characters;
        var result = new Dictionary<(int, int), CharacterId>();
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (table.Cold(slot)?.Save?.Source.Handle is { IsNull: false } hand) result.TryAdd((hand.C, hand.I), table.IdOf(slot));
        }

        return result;
    }
}
