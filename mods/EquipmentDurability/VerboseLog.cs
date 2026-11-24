using System.Globalization;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Items;

namespace Meitou.EquipmentDurability;

/// <summary>
/// The original's <c>Verbose = 1</c> diagnostics, written to the mod log, each kind with the original's cap on how many lines a session writes: rolls,
/// wear (a wear line also whenever 5 or fewer points are left), the weapon's damage change, a worn armour piece's resistance change, body part hits,
/// shots and blocks, the reload time, ignored gear (once per item), weapon sections and armour slots a repair looks at, menus and the selected
/// character's items. Nothing is formatted while <see cref="DurabilityConfig.Verbose"/> is off. The filters (damage, armour, reload, ignore test)
/// and the menu run on other threads than the slow step, so the counters are taken atomically.
/// </summary>
sealed class VerboseLog(DurabilityConfig config, Action<string> log)
{
    #region Caps

    /// <summary>
    /// The original's caps: rolls, wear lines (weapons and armour share one), damage changes and reload times.
    /// </summary>
    const int RollLines = 60, WearLines = 40, DamageLines = 30, ReloadLines = 12;

    /// <summary>
    /// The original's caps: ignored gear, armour resistance changes, body part hits, shots, blocks, weapon sections, menus and item listings.
    /// </summary>
    const int IgnoredLines = 20, ArmourLines = 20, PartLines = 40, ShotLines = 10, BlockLines = 10, SectionLines = 8, MenuLines = 12, ListingLines = 3;

    /// <summary>
    /// The most items one listing names.
    /// </summary>
    const int ListedItems = 60;

    /// <summary>
    /// Points left at or below which a wear line is written past its cap.
    /// </summary>
    const int LowPoints = 5;

    /// <summary>
    /// Lines written so far of each kind.
    /// </summary>
    int _rolls, _wear, _damage, _reload, _ignored, _armour, _parts, _shots, _blocks, _sections, _menus, _listings;

    /// <summary>
    /// The ignored items already written (the original writes an item once: it caches its answer per item). Holds at most
    /// <see cref="IgnoredLines"/> items.
    /// </summary>
    readonly HashSet<ItemInstance> _ignoredSeen = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Whether the diagnostics are on.
    /// </summary>
    public bool On => config.Verbose;

    #endregion

    #region Start and rolls

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
    /// A piece the ignore list leaves out (<paramref name="text"/>: its "name | material | maker" as tested), once per item; any thread.
    /// </summary>
    public void Ignored(ItemInstance item, string text)
    {
        if (!config.Verbose || Volatile.Read(ref _ignored) >= IgnoredLines) return;

        lock (_ignoredSeen)
        {
            if (!_ignoredSeen.Add(item)) return;
        }

        if (TakeShared(ref _ignored, IgnoredLines)) log("left out by the ignore list: " + text);
    }

    #endregion

    #region Combat

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
    /// A player character's shot: a bolt loaded (the engine reports reloads).
    /// </summary>
    public void Shot()
    {
        if (config.Verbose && TakeShared(ref _shots, ShotLines)) log("shot by a player character (bolt loaded)");
    }

    /// <summary>
    /// A player character blocked a blow.
    /// </summary>
    public void Block()
    {
        if (config.Verbose && TakeShared(ref _blocks, BlockLines)) log("blow blocked by a player character");
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
    /// A player's worn armour piece met a melee hit with changed cut and blunt resistance; any thread.
    /// </summary>
    public void Armour(ItemInstance item, int dur, int cap, in ArmourPiece before, in ArmourPiece after)
    {
        if (!config.Verbose || !TakeShared(ref _armour, ArmourLines)) return;

        string cut = Invariant($"cut {before.CutDefence:0.000} -> {after.CutDefence:0.000}");
        string blunt = Invariant($"blunt {before.BluntDefence:0.000} -> {after.BluntDefence:0.000}");
        log(Invariant($"armour '{item.Name}' at {dur} / {cap}: {cut}, {blunt}"));
    }

    /// <summary>
    /// A melee hit on a player character's <paramref name="part"/> and one worn piece: whether it covers that part (null: the piece has no
    /// coverage list).
    /// </summary>
    public void PartHit(string part, ItemInstance piece, bool? covers)
    {
        if (!config.Verbose || !TakeShared(ref _parts, PartLines)) return;

        string answer = covers switch { true => "covers it", false => "does not cover it", null => "has no coverage list" };
        log($"melee hit on the {part}: '{piece.Name}' {answer}");
    }

    /// <summary>
    /// A player's reload took its changed time; any thread.
    /// </summary>
    public void Reload(float game, float changed)
    {
        if (!config.Verbose || !TakeShared(ref _reload, ReloadLines)) return;

        log(Invariant($"reload time {game:0.##} -> {changed:0.##} s"));
    }

    #endregion

    #region Repairs and menus

    /// <summary>
    /// A weapon repair looked at weapon slot <paramref name="slot"/> (its section) and what is there.
    /// </summary>
    public void WeaponSection(int slot, string section, ItemInstance? item)
    {
        if (!config.Verbose || !TakeShared(ref _sections, SectionLines)) return;

        string held = item is null ? "empty" : "'" + item.Name + "'";
        log(Invariant($"weapon slot {slot} (section '{section}'): {held}"));
    }

    /// <summary>
    /// An armour repair being applied passed over an armour slot with nothing it can repair, or picked it (not capped, as the original's).
    /// </summary>
    public void ArmourSlot(string slot, bool picked)
    {
        if (config.Verbose) log("armour repair: " + slot + (picked ? " slot chosen" : " slot holds nothing to repair"));
    }

    /// <summary>
    /// A character's right-click menu was built: whether its character is a player character and the repair rows were added.
    /// </summary>
    public void Menu(string name, bool player, bool adding)
    {
        if (!config.Verbose || !TakeShared(ref _menus, MenuLines)) return;

        string who = player ? "a player character" : "not a player character";
        log($"menu for '{name}' ({who}){(adding ? ": repair rows added" : "")}");
    }

    /// <summary>
    /// Whether an item listing may still be written (the menu asks before it queues one).
    /// </summary>
    public bool WantsListing => config.Verbose && Volatile.Read(ref _listings) < ListingLines;

    /// <summary>
    /// The selected player character's items: shown name, record name and record id of each, at most <see cref="ListedItems"/>.
    /// </summary>
    public void Listing(string who, IEnumerable<ItemInstance> items, Func<string, string?> recordName)
    {
        if (!config.Verbose || !TakeShared(ref _listings, ListingLines)) return;

        var named = items.Take(ListedItems).Select(i => "{" + i.Name + " | " + (recordName(i.Record) ?? "no record") + " | " + i.Record + "}");
        log($"items of the selected '{who}': " + string.Join(" ", named));
    }

    #endregion

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
