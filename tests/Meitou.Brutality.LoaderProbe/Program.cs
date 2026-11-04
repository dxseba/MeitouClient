using System.Runtime.Loader;
using System.Text.Json;
using Meitou.Content;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

// Keep Brutality out of this process's trusted platform assemblies: exercise the real mod load context.
string folder = Path.GetFullPath(args[0]);
var data = new PopulationData { Db = new GameDatabase(), Sites = [], Factions = [] };
var host = ModHost.Load(new([new ModInfo("Brutality", Path.Combine(folder, "Brutality.mod"), ModSource.Local)],
    args.Contains("--untrusted") ? new HashSet<string>() : new HashSet<string> { "Brutality" }, data.Db, data, () => null));
var header = FcsReader.ReadFile(Path.Combine(folder, "Brutality.mod"));
var mod = host.Mods.SingleOrDefault();
var anatomy = mod?.Instance?.GetType().GetProperty("Anatomy")?.GetValue(mod.Instance);
var anatomyVertices = new Dictionary<string, int>();
if (args.Contains("--validate-assets"))
{
    if (anatomy is null) throw new InvalidOperationException("The anatomy library did not initialize");
    var get = anatomy.GetType().GetMethod("Get")!;
    var kind = get.GetParameters()[0].ParameterType;
    foreach (string name in new[] { "Body", "Brain", "Mouth" })
    {
        var batches = (System.Collections.IEnumerable)get.Invoke(anatomy, [Enum.Parse(kind, name)])!;
        int vertices = 0;
        foreach (var batch in batches) vertices += ((Array)batch.GetType().GetProperty("Vertices")!.GetValue(batch)!).Length;
        if (vertices == 0) throw new InvalidDataException("Missing anatomy: " + name);
        anatomyVertices.Add(name, vertices);
    }
}
var registration = host.Simulation?.Systems.SingleOrDefault(s => s.Name == "gore");
var system = registration?.Create(new SystemBuildContext(data, []));
Console.WriteLine("BRUTALITY_PROBE:" + JsonSerializer.Serialize(new
{
    loaded = host.Mods.Count, notices = host.Notices, id = mod?.Manifest.Id,
    context = mod is null ? null : AssemblyLoadContext.GetLoadContext(mod.Instance!.GetType().Assembly)?.Name,
    enabled = mod?.Instance?.GetType().GetProperty("Enabled")?.GetValue(mod.Instance),
    anatomy = anatomy is not null, anatomyVertices, dataRecords = header.Records.Count,
    systems = host.Simulation?.Systems.Count(s => s.Name == "gore") ?? 0,
    afterCombat = registration?.Anchor == SystemAnchor.AfterPart(StandardParts.Combat),
    observer = system is ICombatHitObserver,
}));
