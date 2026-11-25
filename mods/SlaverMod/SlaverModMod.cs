using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Population;

namespace Meitou.SlaverMod;

/// <summary>
/// Port of SlaverMod (Workshop 3828708454): enslave from the order menu, a slave squad of the player's faction, submission with supervision,
/// escapes, punishment and reward, freeing, protection from neutral factions and the shackled walk. The behaviour follows the original's
/// <c>SlaverMod.ini</c> and DLL (README "Facts").
/// </summary>
public sealed class SlaverModMod : IMod
{
    public void Initialize(ModContext context)
    {
        var settings = new SlaverSettings();
        context.AddSettings(settings);

        string? original = OriginalFiles.Folder(context.Folder);
        if (original is null) context.Log($"the original (Workshop {OriginalFiles.Item}) is not installed: no race table or lines");

        var races = RaceTrait.Read(original);
        var lines = SlaveLines.Read(original, context.Language);
        context.AddSystem("slaves", build => new SlaverSystem(settings, context.Population, context.RngPurpose, context.Log, races, lines,
            build.Find<PopulationSystem>(), context.Id), SystemAnchor.End);
        context.AddUi(new SlaverUi(context, settings));
        context.AddUi(new TradeWindow(context));
        SpeedPatch.Install(context, settings);
        VetoPatches.Install(context);
        context.Log($"SlaverMod: {races.Count} race traits");
    }
}
