using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.Disarm;

/// <summary>
/// Adds "Disarm" to the right-click menu of a downed (unconscious) character who is not the player's: a selected squad member within reach drops the
/// target's hip and back weapons on the ground beside it. Taking them this way is not theft: no crime and no relation change.
/// </summary>
public sealed class DisarmMod : IMod
{
    public void Initialize(ModContext context)
    {
        var refresh = EquipmentRefresh.Create(context.Population);
        context.AddSystem("disarm", _ => new DisarmSystem(refresh, context.RngPurpose), SystemAnchor.End);
        context.AddUi(new DisarmMenu());
        context.Log("Disarm initialized");
    }
}

/// <summary>The "Disarm" row on a downed outsider's right-click menu, through the interface's menu-verb slot.</summary>
sealed class DisarmMenu : IUiExtension
{
    public string Id => "dxseba.disarm.ui";

    public void Register(UiRegistration registration) => registration.AddCharacterMenuVerbs(Verbs);

    static IEnumerable<UiMenuVerb> Verbs(UiMenuContext menu)
    {
        var target = menu.Target;
        if (target.IsPlayer || target.Body is not { Unconscious: true, Dead: false } || menu.Selected.Count == 0) return [];

        var disarmers = menu.Selected.ToArray();
        return [new UiMenuVerb("Disarm", () => menu.Enqueue(new DisarmOrder(disarmers, target.Id) { Tick = menu.Tick }))];
    }
}

public sealed record DisarmOrder(IReadOnlyList<CharacterId> Disarmers, CharacterId Target) : SimCommand;
