using System.Globalization;
using Meitou.Simulation.Items;

namespace Meitou.EquipmentDurability;

/// <summary>
/// The original's <c>Verbose = 1</c> diagnostics, written to the mod log: rolls, wear, the weapon's damage change and the reload time, each with
/// the original's cap on how many lines a session writes (a wear line also whenever 5 or fewer points are left). Nothing is formatted while
/// <see cref="DurabilityConfig.Verbose"/> is off. The damage and reload lines come from the filters' threads, so the counters are taken
/// atomically.
/// </summary>
sealed class VerboseLog(DurabilityConfig config, Action<string> log)
{
    /// <summary>
    /// The original's caps: rolls, wear lines (weapons and armour share one), damage changes and reload times.
    /// </summary>
    const int RollLines = 60, WearLines = 40, DamageLines = 30, ReloadLines = 12;

    /// <summary>
    /// Points left at or below which a wear line is written past its cap.
    /// </summary>
    const int LowPoints = 5;

    /// <summary>
    /// Lines written so far of each kind.
    /// </summary>
    int _rolls, _wear, _damage, _reload;

    /// <summary>
    /// The settings read from the cfg (the original logs them at start whatever <c>Verbose</c> says).
    /// </summary>
    public void Settings()
    {
        var range = config.Default;
        string ranges = Invariant($"config: enabled={Flag(config.Enabled)} range={range.Min}-{range.Max} max={range.Cap}");
        string wear = Invariant($" perHit={config.PerHit} perShot={config.PerShot} perBlock={config.PerBlock} perArmourHit={config.PerArmourHit}");
        string rest = $" break={Flag(config.Break)} armour={Flag(config.Armour)} gradeWear={Flag(config.GradeWear)} repair={Flag(config.Repair)}";
        log(ranges + wear + rest + $" verbose={Flag(config.Verbose)}");
    }

    /// <summary>
    /// A new value: rolled in the class's range, or full for a crafted piece.
    /// </summary>
    public void Rolled(ItemInstance item, int value, DurabilityRange range, GearClass c, bool crafted)
    {
        if (!config.Verbose || !TakeShared(ref _rolls, RollLines)) return;

        string how = crafted ? "player-crafted, full" : "rolled";
        log(Invariant($"{how} {value} (range {range.Min}-{range.Max}, max {range.Cap}, class {c}) for '{item.Name}'"));
    }

    /// <summary>
    /// A use that wore a player's piece (<paramref name="what"/>: hit, block, shot or armour hit).
    /// </summary>
    public void Worn(string what, ItemInstance item, int left)
    {
        if (!config.Verbose) return;
        if (!TakeShared(ref _wear, WearLines) && left > LowPoints) return;

        log(Invariant($"player {what} with '{item.Name}' -> {left} left"));
    }

    /// <summary>
    /// A player's blow dealt its weapon's damage factor; any thread.
    /// </summary>
    public void Damage(float factor)
    {
        if (!config.Verbose || !TakeShared(ref _damage, DamageLines)) return;

        log(Invariant($"weapon condition: damage x{(int)(factor * 100 + 0.5f)}%"));
    }

    /// <summary>
    /// A player's reload took its changed time; any thread.
    /// </summary>
    public void Reload(float game, float changed)
    {
        if (!config.Verbose || !TakeShared(ref _reload, ReloadLines)) return;

        log(Invariant($"reload time {game:0.##} -> {changed:0.##} s"));
    }

    /// <summary>
    /// Counts a line of a kind written from any thread; false past the cap.
    /// </summary>
    static bool TakeShared(ref int count, int cap) => Volatile.Read(ref count) < cap && Interlocked.Increment(ref count) <= cap;

    /// <summary>
    /// The original's 0 / 1 switch text.
    /// </summary>
    static string Flag(bool on) => on ? "1" : "0";

    /// <summary>
    /// Formats with the invariant culture.
    /// </summary>
    static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
