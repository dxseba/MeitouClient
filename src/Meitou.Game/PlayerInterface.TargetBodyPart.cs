using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;

namespace Meitou.Game;

sealed partial class PlayerInterface
{
    bool CanChooseAttackPart(CharacterSnapshot target) => !target.IsPlayer && Hostile(target)
        && target.Body is { Dead: false } && (target.Down || target.Asleep)
        && target.CarriedBy.IsNone && target.Prison is null;

    static IEnumerable<HealthPartView> AttackableParts(CharacterSnapshot target) =>
        target.HealthParts.Where(part => part.Id.Length > 0 && part.Name.Length > 0 && part.Limb != LimbState.Stump);

    void ChooseAttackPart(CharacterId id, IReadOnlyList<CharacterId> attackers)
    {
        if (session.CurrentSnapshot.Characters.FirstOrDefault(c => c.Id == id) is not { } target || !CanChooseAttackPart(target)) return;
        // This second step survives the release which chose its parent row. Left-clicking a part confirms; Esc or clicking outside cancels.
        var verbs = AttackableParts(target).Select(part => (part.Name, (Action)(() => AttackPart(id, attackers, part.Id)))).ToArray();
        if (verbs.Length > 0) contextMenu = new(target.Name + " — " + Loc.Tr("Attack body part"), mouse, verbs, DismissOnOutside: true);
    }

    void AttackPart(CharacterId id, IReadOnlyList<CharacterId> attackers, string part)
    {
        if (session.CurrentSnapshot.Characters.FirstOrDefault(c => c.Id == id) is not { } target
            || !CanChooseAttackPart(target) || !AttackableParts(target).Any(p => p.Id == part)) return;
        Say(new AttackOrder(attackers, id)
        {
            Tick = session.World.Tick, Picked = true, AllowIncapacitated = true,
            TargetBodyPart = part, Defensive = target.FightsPlayer,
        });
    }

    internal bool SceneAttackDowned(CharacterId id)
    {
        if (session.CurrentSnapshot.Characters.FirstOrDefault(c => c.Id == id) is not { } target) return false;
        var parent = Verbs(target).FirstOrDefault(v => v.Caption == "Attack body part");
        if (parent.Give is null) return false;
        parent.Give();
        // Exercise the same second-step action as a real user; native scenes choose the chest when the race has one.
        var menu = contextMenu;
        int index = menu is null ? -1 : menu.Verbs.ToList().FindIndex(v => v.Caption.Equals("Chest", StringComparison.OrdinalIgnoreCase));
        if (index < 0) { CloseContextMenu(); return false; }
        ContextAction(index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return true;
    }
}
