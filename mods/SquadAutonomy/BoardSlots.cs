using System.Reflection;
using Meitou.Data.Gameplay.Ai;
using Meitou.Simulation.Ai;

namespace Meitou.SquadAutonomy;

/// <summary>
/// Puts a squad's packages on its board by priority, as the original adds each with <c>Blackboard::_addPackage(package, priority)</c>: the engine's
/// board keeps packages by slot and tries the highest slot first (<c>Blackboard.Update</c>), but its only command, <c>ChangeAi</c>, sets one
/// package in slot 0. The slots are the board's private <c>_slots</c>; without it (another engine build) the top package alone runs.
/// </summary>
static class BoardSlots
{
    /// <summary>
    /// The board's slot table, or null when this engine build has none by that name.
    /// </summary>
    static readonly FieldInfo? SlotsField = typeof(Blackboard).GetField("_slots", BindingFlags.Instance | BindingFlags.NonPublic) is
        { FieldType: var t } f && t == typeof(SortedDictionary<int, List<AiPackage>>) ? f : null;

    /// <summary>
    /// Whether the slots can be set.
    /// </summary>
    public static bool Available => SlotsField is not null;

    /// <summary>
    /// Whether <paramref name="board"/> holds exactly <paramref name="wanted"/> by slot, in order within a slot.
    /// </summary>
    public static bool Holds(Blackboard board, IReadOnlyList<(int Slot, AiPackage Package)> wanted)
    {
        var held = board.Packages.Select(p => (p.Slot, p.Package)).ToList();
        var order = Ordered(wanted);
        return held.Count == order.Count && held.Zip(order).All(x => x.First.Slot == x.Second.Slot && x.First.Package == x.Second.Package);
    }

    /// <summary>
    /// Replaces the board's packages with <paramref name="wanted"/> by slot; the running package goes on until the next package choice.
    /// </summary>
    public static void Set(Blackboard board, IReadOnlyList<(int Slot, AiPackage Package)> wanted)
    {
        if (SlotsField?.GetValue(board) is not SortedDictionary<int, List<AiPackage>> slots) return;

        slots.Clear();
        foreach (var (slot, package) in wanted)
        {
            if (!slots.TryGetValue(slot, out var list)) slots[slot] = list = [];
            if (!list.Contains(package)) list.Add(package);
        }

        board.Touch();
    }

    /// <summary>
    /// The packages as the board lists them: highest slot first, a slot's in the order added.
    /// </summary>
    static List<(int Slot, AiPackage Package)> Ordered(IReadOnlyList<(int Slot, AiPackage Package)> wanted) =>
        [.. wanted
            .Select((p, i) => (p.Slot, p.Package, i))
            .DistinctBy(p => (p.Slot, p.Package))
            .OrderByDescending(p => p.Slot)
            .ThenBy(p => p.i)
            .Select(p => (p.Slot, p.Package))];
}
