using System.Text;
using Meitou.Data.Gameplay.Ai;
using Meitou.Data.Save;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Player;
using Meitou.Simulation.Production;

namespace Meitou.JobTabs;

/// <summary>A kept job and, for a job per target (Bodyguard, Staying close), its target's lasting id (0: none), found again when restored.</summary>
readonly record struct KeptJob(PermaJob Job, ulong Target);

/// <summary>A character's job tabs: the active one and each other tab's jobs, kept as the job list's own entries.</summary>
sealed class Tabs(int count)
{
    public int Active { get; set; }
    public List<KeptJob>[] Lists { get; } = [.. Enumerable.Range(0, count).Select(_ => new List<KeptJob>())];
}

/// <summary>A selected player character's tabs for the buttons: the active one and each tab's job names.</summary>
sealed record TabView(int Active, string[][] Jobs);

/// <summary>
/// Switches job lists in the serial slow step: the character's jobs (<c>CharacterCold.Jobs</c>) are kept in the active tab and the chosen tab's put
/// in their place, then its next decision is asked for. Tabs are keyed by the lasting character id and saved with the world; the first selected
/// player characters' tabs are published every 5th tick (and after a switch) for the buttons. Each character's tabs are also kept under the
/// original's keys in its orders record (<see cref="OriginalSave"/>), and read from them for a character the sidecar does not know.
/// </summary>
sealed class JobTabsSystem(JobTabsSettings settings) : ITickSystem, IModSaveData, IWorldReady
{
    const int Version = 2;

    /// <summary>The tab count, taken once at the start so a changed setting never changes a running world.</summary>
    readonly int _count = Math.Clamp(settings.Count, 2, JobTabsSettings.Most);

    readonly Dictionary<ulong, Tabs> _tabs = [];
    readonly List<SwitchTabOrder> _orders = [];

    volatile IReadOnlyDictionary<CharacterId, TabView> _views = new Dictionary<CharacterId, TabView>();

    /// <summary>A selected player character's tabs as last published; null for another character.</summary>
    public TabView? View(CharacterId id) => _views.GetValueOrDefault(id);

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands)
        {
            if (command is SwitchTabOrder order) _orders.Add(order);
        }
    }

    public void SlowWorld(World world)
    {
        bool switched = _orders.Count > 0, changed = false;
        foreach (var order in _orders)
        {
            foreach (var id in world.Player.Selection.ToList()) changed |= Switch(world, id, order.Tab);
        }

        _orders.Clear();
        if (changed) world.PostInterfaceSound("General_Click");   // the original clicks only when a list changed
        if (switched || world.Tick % 5 == 0) _views = Publish(world);
    }

    /// <summary>Switches one selected player character; true when its list changed.</summary>
    bool Switch(World world, CharacterId id, int tab)
    {
        var table = world.Characters;
        if (tab < 0 || tab >= _count || !table.TryResolveNext(id, out int slot) || table.Cold(slot) is not { IsPlayer: true } cold) return false;

        var lasting = world.LastingIds.GetOrGive(id).Value;
        if (!_tabs.TryGetValue(lasting, out var tabs)) _tabs[lasting] = tabs = new Tabs(_count);
        if (tabs.Active == tab || tab >= tabs.Lists.Length) return false;   // the count was raised since: it applies at the next start

        var keep = tabs.Lists[tabs.Active];
        keep.Clear();
        keep.AddRange(cold.Jobs.Select(job => Keep(world, job)));

        cold.Jobs.Clear();
        cold.Jobs.AddRange(tabs.Lists[tab].Select(kept => Restore(world, kept)));
        tabs.Lists[tab].Clear();
        tabs.Active = tab;
        DecisionSystem.Request(cold);
        OriginalSave.Write(world, id, tabs, job => JobName(world, job));
        return true;
    }

    /// <summary>
    /// After a load or a new game: a player character with tabs in the sidecar gets the original's keys written; one without, whose save holds
    /// the original's keys (a save from the original game), takes its tabs from them.
    /// </summary>
    public void Ready(World world, bool fromSave)
    {
        var table = world.Characters;
        for (int slot = 0; slot < table.HighWater; slot++)
        {
            if (table.Cold(slot) is not { IsPlayer: true } cold) continue;

            var id = table.IdOf(slot);
            if (!cold.LastingId.IsNone && _tabs.TryGetValue(cold.LastingId.Value, out var tabs))
                OriginalSave.Write(world, id, tabs, job => JobName(world, job));
            else if (fromSave && OriginalSave.Read(world, id, _count) is { } read)
                _tabs[world.LastingIds.GetOrGive(id).Value] = read;
        }
    }

    static KeptJob Keep(World world, PermaJob job) =>
        new(job, job.Character.IsNone || !PermaJobs.PerTarget(job.Type) ? 0 : world.LastingIds.GetOrGive(job.Character).Value);

    static PermaJob Restore(World world, KeptJob kept) => OriginalSave.Restore(world, kept);

    Dictionary<CharacterId, TabView> Publish(World world)
    {
        var views = new Dictionary<CharacterId, TabView>();
        var table = world.Characters;
        foreach (var id in world.Player.Selection)
        {
            if (!table.TryResolveNext(id, out int slot) || table.Cold(slot) is not { IsPlayer: true } cold) continue;

            views[id] = ViewOf(world, cold);
        }

        return views;
    }

    /// <summary>The character's tabs: the active tab holds its current jobs (a character never switched is on tab 0).</summary>
    TabView ViewOf(World world, CharacterCold cold)
    {
        var tabs = !cold.LastingId.IsNone && _tabs.TryGetValue(cold.LastingId.Value, out var kept) ? kept : null;
        int active = tabs?.Active ?? 0;
        var jobs = new string[_count][];
        for (int t = 0; t < jobs.Length; t++)
        {
            var list = t == active ? cold.Jobs : tabs is not null && t < tabs.Lists.Length ? tabs.Lists[t].Select(k => Restore(world, k)) : [];
            jobs[t] = [.. list.Select(job => JobName(world, job))];
        }

        return new(active, jobs);
    }

    /// <summary>The original's tooltip line: the job's name, and for a job per target ": " and the target's name.</summary>
    static string JobName(World world, PermaJob job)
    {
        string name = PermaJobs.Text(job.Type) ?? ModJobName(world, job.Type) ?? job.Type.ToString();
        if (!PermaJobs.PerTarget(job.Type) || !world.Characters.TryResolveNext(job.Character, out int slot)) return name;

        return world.Characters.Cold(slot) is { Name.Length: > 0 } target ? name + ": " + target.Name : name;
    }

    static string? ModJobName(World world, TaskType type) =>
        world.Systems.OfType<PlayerSystem>().FirstOrDefault()?.Extensions?.JobTypes.GetValueOrDefault(type);

    public void Write(Stream stream)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Version);
        writer.Write(_tabs.Count);
        foreach (var (id, tabs) in _tabs.OrderBy(t => t.Key))
        {
            writer.Write(id);
            writer.Write(tabs.Active);
            writer.Write(tabs.Lists.Length);
            foreach (var list in tabs.Lists) WriteList(writer, list);
        }
    }

    /// <summary>Each job's type, site, saved handle (its five numbers) and target lasting id.</summary>
    static void WriteList(BinaryWriter writer, List<KeptJob> list)
    {
        writer.Write(list.Count);
        foreach (var (job, target) in list)
        {
            writer.Write((int)job.Type);
            writer.Write(job.Site ?? "");
            foreach (int n in (int[])[job.Saved.Type, job.Saved.C, job.Saved.Cs, job.Saved.I, job.Saved.S]) writer.Write(n);
            writer.Write(target);
        }
    }

    public void Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        int version = reader.ReadInt32();
        if (version is not (1 or Version)) return;

        _tabs.Clear();
        for (int i = reader.ReadInt32(); i > 0; i--)
        {
            ulong id = reader.ReadUInt64();
            int active = reader.ReadInt32(), count = reader.ReadInt32();
            var tabs = new Tabs(Math.Max(count, _count)) { Active = active };
            for (int t = 0; t < count; t++) ReadList(reader, tabs.Lists[t], version);
            _tabs[id] = tabs;
        }
    }

    static void ReadList(BinaryReader reader, List<KeptJob> list, int version)
    {
        for (int j = reader.ReadInt32(); j > 0; j--)
        {
            var type = (TaskType)reader.ReadInt32();
            string site = reader.ReadString();
            var hand = new Hand(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
            ulong target = version >= 2 ? reader.ReadUInt64() : 0;
            list.Add(new(new PermaJob(type, site.Length == 0 ? null : site, hand), target));
        }
    }
}
