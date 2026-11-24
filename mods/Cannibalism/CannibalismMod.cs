using System.Numerics;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.Cannibalism;

/// <summary>
/// Port of Cannibalism: an "Eat Alive" row on the right-click menu of an unconscious, caged or racked character. The selected characters get the
/// game's task 220 on it and eat it (<see cref="CannibalismSystem"/>), fed while they bite.
/// </summary>
public sealed class CannibalismMod : IMod
{
    internal static readonly CannibalismSettings Settings = new();

    public void Initialize(ModContext context)
    {
        context.AddSettings(Settings);

        context.AddSystem("cannibalism", _ => new CannibalismSystem(Settings), SystemAnchor.End);
        context.AddUi(new EatAliveMenu());
        context.AddFrame(frame => EatAliveMenu.Shown = frame.Snapshot);
        context.Log("Cannibalism initialized");
    }
}

/// <summary>
/// The "Eat Alive" row, through the interface's menu-verb slots: on the character, and on a cage whose occupant qualifies (the original's
/// <c>BF_CAGE</c> row): a player-built one (<see cref="UiMenuTargetKind.Building"/>) or a town's or camp's
/// (<see cref="UiMenuTargetKind.Furniture"/>). Every selected character but the target takes part.
/// </summary>
sealed class EatAliveMenu : IUiExtension
{
    /// <summary>
    /// World units from a cage building's origin within which a cage fixture counts as that building's.
    /// </summary>
    const float CageMatch = 6;

    /// <summary>
    /// The snapshot on screen, noted each frame on the host's thread for the cage row (a menu's context carries no snapshot).
    /// </summary>
    internal static volatile WorldSnapshot? Shown;

    public string Id => "dxseba.cannibalism.ui";

    public void Register(UiRegistration registration)
    {
        registration.AddCharacterMenuVerbs(menu => Verbs(menu.Target, menu.Selected, () => menu.Tick, menu.Enqueue));
        registration.AddMenuVerbs(CageVerbs);
    }

    static IEnumerable<UiMenuVerb> Verbs(CharacterSnapshot victim, IReadOnlyList<CharacterId> selected, Func<long> tick,
        Action<SimCommand> enqueue)
    {
        if (!CannibalismMod.Settings.Enabled || !Edible(victim)) return [];

        var target = victim.Id;
        var eaters = selected.Where(id => id != target).ToArray();
        if (eaters.Length == 0) return [];

        return [new UiMenuVerb("Eat Alive", () => enqueue(new EatAliveOrder(eaters, target) { Tick = tick() }))];
    }

    /// <summary>
    /// The row on a cage: its fixture (same id, else the nearest cage spot at the building) holds a character that qualifies.
    /// </summary>
    static IEnumerable<UiMenuVerb> CageVerbs(UiMenuTargetContext menu)
    {
        if (menu.Target is not { Kind: UiMenuTargetKind.Building or UiMenuTargetKind.Furniture } target || Shown is not { } snapshot) return [];

        var cage = snapshot.Cages.FirstOrDefault(c => c.Id == target.Id)
            ?? snapshot.Cages.Where(c => Vector3.Distance(c.Position, target.Position) <= CageMatch)
                .OrderBy(c => Vector3.Distance(c.Position, target.Position)).FirstOrDefault();
        if (cage is null || cage.Occupant.IsNone) return [];

        var occupant = snapshot.Characters.FirstOrDefault(c => c.Id == cage.Occupant);
        return occupant is null ? [] : Verbs(occupant, menu.Selected, () => menu.Tick, menu.Enqueue);
    }

    /// <summary>
    /// A living body that is out cold or held in a cage or torture device, as the original's row (carried, robots and beds included: the
    /// game's task 220 decides, and refuses a target it cannot eat).
    /// </summary>
    static bool Edible(CharacterSnapshot target) => target.Body is { Dead: false } body && (body.Unconscious || target.Imprisoned);
}

/// <summary><paramref name="Eaters"/> eat <paramref name="Target"/> alive.</summary>
public sealed record EatAliveOrder(IReadOnlyList<CharacterId> Eaters, CharacterId Target) : SimCommand;
