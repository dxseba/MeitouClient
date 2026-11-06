using System.Numerics;
using Meitou.Brutality.Anatomy;
using Meitou.Data.Characters;
using Meitou.Engine;
using Meitou.Simulation;
using Meitou.Simulation.Ragdolls;

namespace Meitou.Brutality.Effects;

/// <summary>Feeds the original organ and cable solvers from the two rendered surfaces of a stock torso cut.</summary>
internal sealed class StockTorsoEffects(string folder) : IDisposable
{
    const int PendingOrganLimit = 24, RobotSourceLimit = RobotCableSimulation.Capacity / 2;

    sealed record Source(StockTorsoRules.CutRequest Cut, CharacterAppearance Look, SkeletonRig Rig, double Time, long Key);
    sealed record WoundSource(CharacterAppearance Look, SkeletonRig Rig, CutFluidContact Contact, double Seen);
    readonly Dictionary<CharacterId, Source> sources = [];
    readonly Dictionary<CharacterId, WoundSource> wounds = [];
    readonly HashSet<CharacterId> visible = [];
    readonly List<RobotCableContact> contacts = [];
    readonly RobotCableSimulation cables = new();
    WalkabilityEnvironment? ground;
    BowelPresentation? bowels;
    long serial;
    bool reported;
    readonly bool trace = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_TRACE_HITS") == "1";

    internal bool Active => sources.Count != 0 || wounds.Count != 0 || bowels?.Count > 0 || cables.Count > 0;
    internal bool HasSpilled(CharacterId owner) => bowels?.HasSpilled(owner) == true;

    internal void BeginWound(CharacterId owner, CharacterAppearance look, SkeletonRig rig, CutFluidContact contact, double now)
    {
        if (sources.ContainsKey(owner) || HasSpilled(owner) || wounds.Count >= 16 && !wounds.ContainsKey(owner)) return;
        EnsureBowels();
        wounds[owner] = new(look, rig, contact, now);
    }

    void EnsureBowels()
    {
        if (bowels is not null) return;
        var anatomy = new AnatomyLibrary(new AnatomyAssets(folder));
        bowels = new(anatomy, new(scale => BowelPresentation.BuildBowelAssets(anatomy, scale)));
    }

    internal void Begin(in StockTorsoRules.CutRequest request, CharacterAppearance look, SkeletonRig rig, double time)
    {
        if (sources.ContainsKey(request.Victim) || !request.Robot && HasSpilled(request.Victim)) return;
        bool robot = request.Robot;
        int limit = robot ? RobotSourceLimit : PendingOrganLimit;
        var sameKind = sources.Where(pair => pair.Value.Cut.Robot == robot).ToArray();
        if (sameKind.Length >= limit)
        {
            if (robot) return;
            // Only unspawned organic sources are kept here. An old missing asset cannot block new cuts.
            sources.Remove(sameKind.MinBy(pair => pair.Value.Key).Key);
        }
        sources.Add(request.Victim, new(request, look, rig, time, ++serial));
        if (!robot) EnsureBowels();
    }

    internal void Advance(object host, WorldSession shown, double now, GoreEffectsSimulation scene)
    {
        var environment = ground ??= new(shown.World.Walkability);
        visible.Clear();
        foreach (var actor in shown.CurrentSnapshot.Characters) visible.Add(actor.Id);
        contacts.Clear();
        foreach (var (owner, source) in sources.ToArray())
        {
            if (!visible.Contains(owner) || now - source.Time >= BowelPresentation.Lifetime)
            {
                sources.Remove(owner);
                continue;
            }
            if (source.Cut.Robot)
            {
                for (int half = 0; half < 2; half++)
                    if (StockTorsoFragments.Contact(host, owner, half) is { } point)
                        contacts.Add(new(source.Key * 2 + half, point));
                continue;
            }
            // Time the organ's emergence from the first visible frame after its asset is ready.
            if (bowels is { } organs && (organs.HasSpilled(owner)
                || organs.Begin(owner, source.Look, source.Rig, source.Cut.Restoring ? source.Time - GutEmergence.ReleaseAge : now, now,
                    half => StockTorsoFragments.Contact(host, owner, half), source.Cut.Travel, source.Cut.BloodColour, environment)))
                sources.Remove(owner);
        }
        foreach (var (owner, wound) in wounds.ToArray())
        {
            if (!visible.Contains(owner) || now - wound.Seen > .5 || sources.ContainsKey(owner) || HasSpilled(owner))
            {
                wounds.Remove(owner);
                continue;
            }
            uint blood = unchecked((uint)wound.Look.Race.GetInt("blood colour", 0x800000)) & 0xFFFFFF;
            if (bowels?.BeginWound(owner, wound.Look, wound.Rig, now, now, wound.Contact, blood, environment) == true)
            {
                wounds.Remove(owner);
                if (trace) Console.WriteLine($"Brutality stock standing bowel spilled at={wound.Contact.Position}");
            }
        }
        bowels?.Advance(now, (owner, half) => StockTorsoFragments.Contact(host, owner, half), visible.Contains,
            environment, shown.ShownTick, (float)shown.TimeScale);
        cables.Advance(now, contacts, position => environment.GroundHeight(position.X, position.Z, position.Y));
        scene.Bowels = BowelDiagnostics.Hide || bowels is null ? [] : bowels.Rendered;
        scene.Cables = cables.Rendered;
        StockPartLabels.Publish(shown.World, "organs", bowels is null ? [] : bowels.Labels);
        if (trace && !reported && (scene.Bowels.Count != 0 || scene.Cables.Count != 0))
        {
            reported = true;
            Console.WriteLine($"Brutality stock tissue organs={scene.Bowels.Count} attached={bowels?.Attached ?? 0} cables={scene.Cables.Count}");
        }
    }

    public void Dispose()
    {
        bowels?.Dispose();
        cables.Clear();
        sources.Clear();
        wounds.Clear();
    }
}
