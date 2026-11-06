#if BRUTALITY_STOCK_SAVE_PROBE
using System.Reflection;
using HarmonyLib;
using Meitou.Data.Save;
using Meitou.Engine;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Saving;

namespace Meitou.Brutality;

internal sealed class StockTorsoSaveProbe(Type host)
{
    static StockTorsoSaveProbe? installed;
    readonly FieldInfo session = AccessTools.Field(host, "session");
    readonly FieldInfo population = AccessTools.Field(host, "popData");
    readonly MethodInfo captureOptions = AccessTools.Method(host, "CaptureOptions");
    readonly MethodInfo captureModData = AccessTools.Method(host, "CaptureModData");
    readonly MethodInfo swapInSave = AccessTools.Method(host, "SwapInSave");
    readonly string? folder = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_STOCK_SAVE_PROBE");
    readonly string? load = Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_STOCK_LOAD_PROBE");
    readonly bool offscreen = Environment.GetCommandLineArgs().Contains("--screenshot", StringComparer.Ordinal);
    bool ran;

    internal static void Install(ModContext context)
    {
        if (AccessTools.TypeByName("Meitou.Game.GameHost") is not { } host
            || !Environment.GetCommandLineArgs().Contains("--sandbox", StringComparer.Ordinal)) return;
        var probe = new StockTorsoSaveProbe(host);
        if (!string.IsNullOrWhiteSpace(probe.folder))
            context.Harmony.Patch(AccessTools.Method(host, "UpdateModHits"),
                postfix: new HarmonyMethod(typeof(StockTorsoSaveProbe), nameof(Update)));
        else if (!string.IsNullOrWhiteSpace(probe.load))
            context.Harmony.Patch(AccessTools.Method(host, probe.offscreen ? "UpdateModHits" : "HandleSaves"),
                postfix: new HarmonyMethod(typeof(StockTorsoSaveProbe), nameof(Reload)));
        else return;
        installed = probe;
    }

    static void Update(object __instance) => installed?.Check(__instance);
    static void Reload(object __instance) => installed?.CheckLoad(__instance);

    void CheckLoad(object host)
    {
        if (ran || session.GetValue(host) is not WorldSession shown || shown.ShownTick < 60) return;
        ran = true;
        try
        {
            shown.SettleAhead();
            object?[] args = [Task.FromResult(SaveGame.Load(load!)), !offscreen, null, null, null];
            ((Task)swapInSave.Invoke(host, args)!).GetAwaiter().GetResult();
            Console.WriteLine($"Brutality stock native load probe {args[2]}");
            var world = shown.World;
            if (world.Squads.Find(world.Player.Squad) is { Leader.IsNone: false } squad && StockProgression.For(world) is { } progress)
                Console.WriteLine(FormattableString.Invariant($"Brutality stock native load level={progress.LevelFor(world, squad.Leader):F1}"));
        }
        catch (Exception error) { Console.Error.WriteLine($"Brutality stock native load probe failed: {error}"); }
    }

    void Check(object host)
    {
        if (ran || session.GetValue(host) is not WorldSession shown || shown.ShownTick < 180
            || population.GetValue(host) is not PopulationData data
            || !StockHeadSplitSave.HasCuts(shown.World) && !HasLimbCuts(shown.World)
            && !StockTorsoRules.CutOwners(shown.World).Any(owner => StockTorsoSave.For(shown.World, owner) is not null)) return;
        if (Environment.GetEnvironmentVariable("MEITOU_BRUTALITY_STOCK_PROBE_COMBINED") == "1"
            && !StockTorsoRules.CutOwners(shown.World).Any(owner => StockTorsoSave.For(shown.World, owner) is not null)) return;
        ran = true;
        try
        {
            if (Directory.Exists(folder!)) throw new IOException("Probe expects a new save folder.");
            shown.SettleAhead();
            var clock = shown.Clock;
            var game = SaveCapture.Capture(shown.World, data, new SaveClock((int)clock.Day, clock.Hour, clock.Minute),
                options: (SaveCaptureOptions?)captureOptions.Invoke(host, null));
            captureModData.Invoke(host, [shown.World, game]);
            game.Write(folder!);
            var count = game.Characters.Count(actor => actor.GameState?.Strings.ContainsKey("dxseba brutality torso") == true);
            var heads = game.Characters.Count(actor => actor.GameState is { } state &&
                (state.Strings.ContainsKey("dxseba brutality head split") || state.Strings.ContainsKey("dxseba brutality imported head split")));
            var carried = game.Characters.Count(actor => actor.GameState?.Strings.ContainsKey("dxseba brutality proxy") == true);
            int limbs = game.Characters.Count(actor => actor.GameState?.Strings.ContainsKey("dxseba brutality limbs") == true);
            int items = game.Zones.Values.Sum(zone => zone.File.Records.Count(record => record.Strings.ContainsKey("dxseba brutality limb")));
            Console.WriteLine($"Brutality stock save probe wrote cuts={count} heads={heads} limbOwners={limbs} limbItems={items} proxyActors={carried} characters={game.Characters.Count()}");
            using var restored = new World(shown.World.Settings, shown.World.Walkability);
            SaveLoader.Load(restored, SaveGame.Load(folder!), data, new() { ApplyRelations = false, Appearances = data.Appearances });
            var restoredCount = StockTorsoRules.CutOwners(restored).Count(owner => StockTorsoSave.For(restored, owner) is not null);
            int restoredHeads = StockHeadSplitSave.Owners(restored).Count();
            StockLimbSave.Step(restored);
            int restoredLimbs = Enumerable.Range(0, restored.Characters.HighWater).Count(slot => restored.Characters.Next[slot].Alive
                && StockLimbRules.For(restored, restored.Characters.IdOf(slot)) is { Length: > 0 });
            if (count + heads + limbs == 0 || restoredCount != count || restoredHeads != heads || restoredLimbs != limbs || limbs > 0 && items == 0)
                throw new InvalidDataException($"Expected {count}/{heads}/{limbs} torso, head and limb records, restored {restoredCount}/{restoredHeads}/{restoredLimbs}; limb items={items}.");
            Console.WriteLine($"Brutality stock save probe restored cuts={restoredCount} heads={restoredHeads} limbOwners={restoredLimbs}");
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Brutality stock save probe failed: {error}");
        }
    }

    static bool HasLimbCuts(World world) => Enumerable.Range(0, world.Characters.HighWater).Any(slot => world.Characters.Next[slot].Alive
        && StockLimbRules.For(world, world.Characters.IdOf(slot)) is { Length: > 0 });
}
#endif
