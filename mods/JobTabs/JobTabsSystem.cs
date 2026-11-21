using System.Text;
using Meitou.Data.Gameplay.Ai;
using Meitou.Data.Save;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Production;

namespace Meitou.JobTabs;

/// <summary>A character's job tabs: the active one and each tab's jobs, kept as the job list's own entries.</summary>
sealed class Tabs(int count)
{
    public int Active { get; set; }
    public List<PermaJob>[] Lists { get; } = [.. Enumerable.Range(0, count).Select(_ => new List<PermaJob>())];
}

/// <summary>
/// Switches job lists in the serial slow step: the character's jobs (<c>CharacterCold.Jobs</c>) are kept in the active tab and the chosen tab's put
/// in their place, then its next decision is asked for. Tabs are keyed by the lasting character id and saved with the world; the first selected
/// character's active tab is published every 5th tick for the buttons.
/// </summary>
sealed class JobTabsSystem(JobTabsSettings settings) : ITickSystem, IModSaveData
{
    const int Version = 1;

    readonly Dictionary<ulong, Tabs> _tabs = [];
    readonly List<SwitchTabOrder> _orders = [];

    /// <summary>The first selected player character's active tab; -1 while none is selected.</summary>
    public volatile int Active = -1;

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands)
        {
            if (command is SwitchTabOrder order) _orders.Add(order);
        }
    }

    public void SlowWorld(World world)
    {
        bool changed = _orders.Count > 0;
        foreach (var order in _orders)
        {
            foreach (var id in world.Player.Selection.ToList()) Switch(world, id, order.Tab);
        }

        _orders.Clear();
        if (changed || world.Tick % 5 == 0) Active = Publish(world);
    }

    void Switch(World world, CharacterId id, int tab)
    {
        var table = world.Characters;
        if (tab < 0 || tab >= settings.Count || !table.TryResolveNext(id, out int slot) || table.Cold(slot) is not { IsPlayer: true } cold) return;

        var lasting = world.LastingIds.GetOrGive(id).Value;
        if (!_tabs.TryGetValue(lasting, out var tabs)) _tabs[lasting] = tabs = new Tabs(settings.Count);
        if (tabs.Active == tab) return;

        var keep = tabs.Lists[tabs.Active];
        keep.Clear();
        keep.AddRange(cold.Jobs);

        cold.Jobs.Clear();
        cold.Jobs.AddRange(tabs.Lists[tab]);
        tabs.Active = tab;
        DecisionSystem.Request(cold);
    }

    int Publish(World world)
    {
        var table = world.Characters;
        foreach (var id in world.Player.Selection)
        {
            if (!table.TryResolveNext(id, out int slot) || table.Cold(slot) is not { IsPlayer: true } cold) continue;

            return !cold.LastingId.IsNone && _tabs.TryGetValue(cold.LastingId.Value, out var tabs) ? tabs.Active : 0;
        }

        return -1;
    }

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

    /// <summary>Each job's type, site and saved handle (its five numbers).</summary>
    static void WriteList(BinaryWriter writer, List<PermaJob> list)
    {
        writer.Write(list.Count);
        foreach (var job in list)
        {
            writer.Write((int)job.Type);
            writer.Write(job.Site ?? "");
            foreach (int n in (int[])[job.Saved.Type, job.Saved.C, job.Saved.Cs, job.Saved.I, job.Saved.S]) writer.Write(n);
        }
    }

    public void Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (reader.ReadInt32() != Version) return;

        _tabs.Clear();
        for (int i = reader.ReadInt32(); i > 0; i--)
        {
            ulong id = reader.ReadUInt64();
            int active = reader.ReadInt32(), count = reader.ReadInt32();
            var tabs = new Tabs(Math.Max(count, settings.Count)) { Active = active };
            for (int t = 0; t < count; t++) ReadList(reader, tabs.Lists[t]);
            _tabs[id] = tabs;
        }
    }

    static void ReadList(BinaryReader reader, List<PermaJob> list)
    {
        for (int j = reader.ReadInt32(); j > 0; j--)
        {
            var type = (TaskType)reader.ReadInt32();
            string site = reader.ReadString();
            var hand = new Hand(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
            list.Add(new PermaJob(type, site.Length == 0 ? null : site, hand));
        }
    }
}
