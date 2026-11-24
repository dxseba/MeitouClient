using System.Collections.Concurrent;
using Meitou.Data;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Gameplay.Construction;

namespace Meitou.SquadAutonomy;

/// <summary>
/// The original's Labour categories (its window's sliders, in its order; <c>SquadSettingsInfo::getLabourPriority</c> maps a workplace's
/// <c>stat used</c> to one, a machine with no operator slots to Automatic Machine, any other stat to Other).
/// </summary>
public enum LabourKind
{
    Science,
    Labouring,
    Farming,
    Cooking,
    Medic,
    Engineer,
    Robotics,
    WeaponSmith,
    ArmourSmith,
    CrossbowSmith,
    AutomaticMachine,
    Other,
}

/// <summary>
/// A squad's labour priorities, one per <see cref="LabourKind"/> (the original's 0 to 10, default 1; 0 forbids the work, a bigger number wins).
/// </summary>
public sealed record LabourPriorities(IReadOnlyList<int> Values)
{
    /// <summary>The original's slider top.</summary>
    public const int Max = 10;

    /// <summary>How many categories there are.</summary>
    public static readonly int Count = Enum.GetValues<LabourKind>().Length;

    /// <summary>The original's defaults: every category 1.</summary>
    public static readonly LabourPriorities Default = All(1);

    /// <summary>Every category at <paramref name="value"/> (the original's Set All).</summary>
    public static LabourPriorities All(int value) => new([.. Enumerable.Repeat(Math.Clamp(value, 0, Max), Count)]);

    /// <summary>The priority of <paramref name="kind"/>.</summary>
    public int Of(LabourKind kind) => (int)kind < Values.Count ? Values[(int)kind] : 1;

    /// <summary>These priorities with <paramref name="kind"/> at <paramref name="value"/> (held to 0 to 10).</summary>
    public LabourPriorities With(LabourKind kind, int value)
    {
        var values = Enumerable.Range(0, Count).Select(i => Of((LabourKind)i)).ToArray();
        values[(int)kind] = Math.Clamp(value, 0, Max);
        return new(values);
    }

    /// <summary>The category of a workplace's <c>stat used</c>.</summary>
    public static LabourKind KindOf(StatsEnumerated stat) => stat switch
    {
        StatsEnumerated.Science => LabourKind.Science,
        StatsEnumerated.Labouring => LabourKind.Labouring,
        StatsEnumerated.Farming => LabourKind.Farming,
        StatsEnumerated.Engineering => LabourKind.Engineer,
        StatsEnumerated.Cooking => LabourKind.Cooking,
        StatsEnumerated.Robotics => LabourKind.Robotics,
        StatsEnumerated.Medic => LabourKind.Medic,
        StatsEnumerated.SmithingWeapon => LabourKind.WeaponSmith,
        StatsEnumerated.SmithingArmour => LabourKind.ArmourSmith,
        StatsEnumerated.SmithingBow => LabourKind.CrossbowSmith,
        _ => LabourKind.Other,
    };
}

/// <summary>
/// The <c>stat used</c> of each workplace's BUILDING record (<see cref="MachineData.Stat"/>), looked up once; any thread.
/// </summary>
sealed class WorkplaceStats(GameDatabase db)
{
    readonly ConcurrentDictionary<string, StatsEnumerated> _stats = new(StringComparer.Ordinal);

    public StatsEnumerated Of(string record) => _stats.GetOrAdd(record, Read);

    StatsEnumerated Read(string record) =>
        db.Find(record) is { } building && MachineData.From(db, building) is { } machine ? (StatsEnumerated)machine.Stat : StatsEnumerated.None;
}
