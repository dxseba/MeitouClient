using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.Cannibalism;

/// <summary>
/// Port of Cannibalism: an "Eat Alive" row on the right-click menu of an unconscious, caged or racked character. The selected characters eat it
/// through the game's own eat-captive jobs (<see cref="CaptiveJobs"/>), fed while they bite.
/// </summary>
public sealed class CannibalismMod : IMod
{
    internal static readonly CannibalismSettings Settings = new();

    public void Initialize(ModContext context)
    {
        context.AddSettings(Settings);

        var jobs = CaptiveJobs.Create();
        context.AddSystem("cannibalism", _ => new CannibalismSystem(Settings, jobs), SystemAnchor.End);
        context.AddUi(new EatAliveMenu());
        context.Log("Cannibalism initialized");
    }
}

/// <summary>The "Eat Alive" row, through the interface's menu-verb slot: every selected character but the target takes part.</summary>
sealed class EatAliveMenu : IUiExtension
{
    public string Id => "dxseba.cannibalism.ui";

    public void Register(UiRegistration registration) => registration.AddCharacterMenuVerbs(Verbs);

    static IEnumerable<UiMenuVerb> Verbs(UiMenuContext menu)
    {
        if (!CannibalismMod.Settings.Enabled || !Edible(menu.Target)) return [];

        var target = menu.Target.Id;
        var eaters = menu.Selected.Where(id => id != target).ToArray();
        if (eaters.Length == 0) return [];

        return [new UiMenuVerb("Eat Alive", () => menu.Enqueue(new EatAliveOrder(eaters, target) { Tick = menu.Tick }))];
    }

    /// <summary>A living body that is out cold or held in a cage or torture device, and not carried.</summary>
    static bool Edible(CharacterSnapshot target) =>
        target.Body is { Dead: false } body && (body.Unconscious || target.Imprisoned) && target.CarriedBy == CharacterId.None;
}

/// <summary><paramref name="Eaters"/> eat <paramref name="Target"/> alive.</summary>
public sealed record EatAliveOrder(IReadOnlyList<CharacterId> Eaters, CharacterId Target) : SimCommand;
