using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;

namespace Meitou.AdvancedMedicAi;

/// <summary>
/// Port of Advanced Medic AI 2.0 (workshop 3742288543, <c>AdvancedMedicAI.dll</c>): player characters with the Medic job and a first aid kit (a
/// robot repair kit for robots) are sent by the game's own First Aid order to downed characters nearby, by group and urgency, never overriding
/// an order the player just gave. KenshiExtensionPlugin, which the original needs for its settings tab, is not ported: the settings are on the
/// Mods page.
/// </summary>
public sealed class AdvancedMedicAiMod : IMod
{
    internal static readonly MedicSettings Settings = new();

    public void Initialize(ModContext context)
    {
        OriginalTexts.Add(context);
        var group = context.AddSettings(Settings);
        var rules = group.Settings.First(s => s.Key == nameof(MedicSettings.FactionRules));
        context.AddUi(new FactionRulesWindow(context, rules));
        var tuning = OriginalFolder.Tuning(context);
        context.AddSystem("advanced-medic-ai", build => new MedicSystem(Settings, tuning, context, build.Find<BodySystem>()), SystemAnchor.End);
        OrderWatch.Install(context);
        context.Log("Advanced Medic AI initialized");
    }
}
