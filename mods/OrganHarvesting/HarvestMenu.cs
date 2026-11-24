using Meitou.Data;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.OrganHarvesting;

/// <summary>The original's harvest rules on races (its <c>CanHarvest</c> and <c>CanHarvestAsHarvester</c>).</summary>
static class HarvestRules
{
    /// <summary>A race that heals itself (RACE <c>self healing</c>) can neither be harvested nor harvest.</summary>
    public static bool SelfHealing(GameRecord race) => race.GetBool("self healing");

    /// <summary>A target: a human (not an animal), not a robot, not self-healing.</summary>
    public static bool Harvestable(GameRecord race, bool animal) => !animal && !race.GetBool("is robot") && !SelfHealing(race);
}

/// <summary>
/// Whether the first selected character can do the cutting (not of a self-healing race; robots can), noted once a frame from the snapshot, as the
/// menu source gets no snapshot of the selection.
/// </summary>
sealed class HarvesterCache
{
    volatile Entry? _entry;

    sealed record Entry(CharacterId Id, bool CanHarvest);

    public void Update(ModFrame frame)
    {
        if (frame.Selected is not [var first, ..])
        {
            _entry = null;
            return;
        }

        if (_entry is { } known && known.Id == first) return;

        var snapshot = frame.Snapshot.Characters.FirstOrDefault(c => c.Id == first);
        bool can = snapshot?.Appearance is { } look && !HarvestRules.SelfHealing(look.Race);
        _entry = snapshot is null ? null : new Entry(first, can);
    }

    public bool CanHarvest(CharacterId id) => _entry is { } e && e.Id == id && e.CanHarvest;
}

/// <summary>
/// The "Harvest Organs" row, on a character that is out cold and alive, not an animal, a robot or of a self-healing race, while the first selected
/// character can cut (the original shows it only then). Choosing it orders every selected character to the kill.
/// </summary>
sealed class HarvestMenu(HarvesterCache harvesters) : IUiExtension
{
    public string Id => "dxseba.organharvesting.ui";

    public void Register(UiRegistration registration) => registration.AddCharacterMenuVerbs(Verbs);

    IEnumerable<UiMenuVerb> Verbs(UiMenuContext menu)
    {
        var target = menu.Target;
        if (target.Body is not { Unconscious: true, Dead: false } || target.Appearance is not { } look) return [];
        if (!HarvestRules.Harvestable(look.Race, look.Animal is not null)) return [];
        if (menu.Selected is not [var first, ..] || !harvesters.CanHarvest(first)) return [];

        var workers = menu.Selected.Where(id => id != target.Id).ToArray();
        return [new UiMenuVerb("Harvest Organs", () => menu.Enqueue(new HarvestOrder(workers, target.Id) { Tick = menu.Tick }))];
    }
}

/// <summary>The selected <paramref name="Workers"/> (the first is the one the original checks) go to kill <paramref name="Victim"/> for an organ.</summary>
public sealed record HarvestOrder(IReadOnlyList<CharacterId> Workers, CharacterId Victim) : SimCommand;
