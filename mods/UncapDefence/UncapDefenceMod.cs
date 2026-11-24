using Meitou.Game.Host.Input;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.UncapDefence;

/// <summary>
/// Port of Uncap Defence Success Rate (Workshop 3799167934, <c>UncapDefenceSuccessRate.dll</c>): the block and dodge chances lose the game's 95 %
/// cap and follow the level gap towards a chosen ceiling (or down towards a floor) for every fighter; six cards at the top left set the mode, scale,
/// ceiling, toggle key and language and show the last change. The rules follow the original's DLL (README "Facts").
/// </summary>
public sealed class UncapDefenceMod : IMod
{
    internal static readonly UncapSettings Settings = new();

    public void Initialize(ModContext context)
    {
        Settings.Language = context.Language.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? UncapSettings.Chinese : UncapSettings.English;
        var group = context.AddSettings(Settings);
        context.AddSystem("uncap-defence", _ => new UncapFilter(), SystemAnchor.End);

        var cards = new UncapCards(group);
        cards.Toggle = context.AddKeyAction("toggle", "Show or hide the defence cap cards", _ => cards.ToggleHidden(),
            Binding.Of(Key.K).With(KeyModifiers.Ctrl));
        context.AddUi(cards);
        context.Log("Uncap Defence Success Rate initialized");
    }
}
