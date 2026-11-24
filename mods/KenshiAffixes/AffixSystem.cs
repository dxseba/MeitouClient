using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Items;

namespace Meitou.KenshiAffixes;

/// <summary>
/// Rolls affixes (every 60th tick, once per item, kept in its tag) on the equipped gear of NPCs, on shop stock and on Meitou weapons; applies the
/// stat affixes of each character's equipped gear as stat modifiers (every 30th tick; not saved, so set again after a load); weapon damage and
/// critical hits as a hit filter, the armour resistance affixes as an armour filter, and Leeching as a heal of the attacker's stun.
/// </summary>
sealed partial class AffixSystem(AffixSettings settings, RngPurpose purpose, CombatDatabase? combat, Profiles profiles, Action<string> log)
    : ICombatHitFilter, IArmourFilter, ICombatHitObserver
{
    readonly List<ShowAffixesOrder> _shows = [];

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands)
        {
            if (command is ShowAffixesOrder show) _shows.Add(show);
            else if (command is TestRollOrder or InspectLootOrder) _orders.Add(command);
        }
    }

    public void SlowWorld(World world)
    {
        foreach (var show in _shows) Show(world, show.Character);
        _shows.Clear();
        Orders(world);

        if (world.Tick % 60 == 0 && settings.Enabled && combat is not null) RollAll(world);
        if (world.Tick % 30 == 0) ApplyAll(world);
        Heal(world);
    }

    static string Line(ItemInstance item)
    {
        var affixes = AffixTag.Read(item.ModTag);
        string text = affixes.Count > 0 ? string.Join(", ", affixes.Select(a => a.Text)) : "no affixes";
        return $"{item.Name} (iLvl {AffixTag.Level(item.ModTag) ?? 0}): {text}";
    }

    /// <summary>Posts the affixes of the character's equipped gear.</summary>
    static void Show(World world, CharacterId id)
    {
        var table = world.Characters;
        if (!table.TryResolveNext(id, out int slot) || table.Cold(slot) is not { } cold) return;

        var lines = Wearing.Equipment(cold.Inventory).Where(i => AffixTag.Rolled(i.ModTag)).Select(Line).ToList();
        world.Post(lines.Count == 0 ? $"{cold.Name} wears no gear with affixes." : $"{cold.Name}: " + string.Join("; ", lines));
    }
}
