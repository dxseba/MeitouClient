using Meitou.Game.Host.Input;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;

namespace Meitou.AttackSpeedUncapped;

/// <summary>
/// Port of Attack &amp; Block Speed Uncapped (Workshop 3798599010, <c>SpeedCapTuner.dll</c>): an <see cref="Simulation.Combat.ICombatSpeedFilter"/>
/// lets a swing or block that reached the game's cap play faster, up to the cap the player picks on four cards at the top centre of the screen
/// (attack cap, block cap, language, toggle key). The rules follow the original's DLL (README "Facts").
/// </summary>
public sealed class AttackSpeedUncappedMod : IMod
{
    internal static readonly SpeedCapSettings Settings = new();

    public void Initialize(ModContext context)
    {
        bool chinese = context.Language.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        Settings.Language = chinese ? SpeedCapSettings.Chinese : SpeedCapSettings.English;
        var group = context.AddSettings(Settings);
        context.AddSystem("speed-cap", _ => new SpeedCapSystem(new XpService(context.Population.Bodies.Constants, context.Population.BodyOptions)),
            SystemAnchor.End);

        var cards = new SpeedCards(group);
        cards.Toggle = context.AddKeyAction("toggle", "Show or hide the speed cap cards", _ => cards.ToggleHidden(),
            Binding.Of(Key.J).With(KeyModifiers.Ctrl));
        context.AddUi(cards);
        context.Log("Attack & Block Speed Uncapped initialized");
    }
}
