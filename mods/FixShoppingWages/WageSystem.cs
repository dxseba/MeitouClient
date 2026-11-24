using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Simulation;
using Meitou.Simulation.Factions;

namespace Meitou.FixShoppingWages;

/// <summary>
/// The original's hook on <c>GameWorld::dailyUpdates</c>, run in the serial slow step when the game day changes (not on the first day seen, so a
/// loaded game keeps its purses until the next day): every loaded non-player character gets its daily wage added to its money (its squad's purse,
/// the money an NPC shopper pays from), the purse never pushed past <see cref="WageSettings.MaxSavingsMultiplier"/> x that wage. Members are paid one
/// after another into their shared purse, each against its own cap, as the original pays each character in turn.
/// </summary>
sealed partial class WageSystem(WageSettings settings, GameDatabase data, RngPurpose purpose, int driedMeat, Action<string> log)
    : IPausedCommandHandler
{
    /// <summary>
    /// The game day last seen (<see cref="long.MinValue"/> before the first).
    /// </summary>
    long _day = long.MinValue;

    public void SlowWorld(World world)
    {
        if (settings.Enabled && settings.VerboseDebugLogging) HourlyDiff(world);

        long day = (long)Math.Floor(world.GameHours / 24);
        bool newDay = _day != long.MinValue && day != _day;
        _day = day;
        if (!newDay || !settings.Enabled) return;

        PayAll(world, day);
    }

    void PayAll(World world, long day)
    {
        bool verbose = settings.VerboseDebugLogging;
        if (verbose) log($"[GameWorld] dailyUpdates START: totalDays={day} totalHours={world.GameHours:0.##}");

        int universal = Universal();
        if (verbose) log($"universal wage {universal} ({UniversalSource()})");

        var count = new PayCount();
        foreach (var squad in world.Squads.All) PaySquad(world, squad, day, universal, verbose ? count : null);

        if (verbose) log($"[GameWorld] dailyUpdates END: npcCount={count.Npcs} changedCount={count.Changed}");
    }

    string UniversalSource() => settings.BaseWageOverride ? "override" : driedMeat > 0 ? "Dried Meat average price" : "fallback";

    /// <summary>
    /// The wage of those without one of their own: <c>baseWageOverrideValue</c> with the override on, else Dried Meat's average price, else
    /// <c>baseWageFallback</c>.
    /// </summary>
    int Universal() => settings.BaseWageOverride ? settings.BaseWageOverrideValue : driedMeat > 0 ? driedMeat : settings.BaseWageFallback;

    /// <summary>
    /// Pays the squad's living non-player members in turn; <paramref name="count"/> (verbose logging only) counts them and logs each.
    /// </summary>
    void PaySquad(World world, Squad squad, long day, int universal, PayCount? count)
    {
        if (squad.Dead) return;

        foreach (var id in squad.Members)
        {
            if (!world.Characters.TryResolveNext(id, out int slot) || !world.Characters.Next[slot].Alive) continue;
            if (world.Characters.Cold(slot) is not { IsPlayer: false } cold) continue;

            long before = squad.Money;
            if (WageOf(world, cold.RecordId, id, day, universal) is { } wage)
            {
                long capped = Math.Min(squad.Money + wage, (long)settings.MaxSavingsMultiplier * wage);
                if (capped > squad.Money) squad.Money = capped;
            }

            if (count is not null) count.Note(log, Describe(cold, id), before, squad.Money);
        }
    }

    /// <summary>
    /// A character's daily wage (the original's tiers): none without a <c>wages</c> field or with <c>wages</c> -1; a non-zero <c>wages</c> itself;
    /// with <c>wages</c> 0, none when <c>money max</c> is missing or -1, else a roll in <c>money min</c>..<c>money max</c> when min is below max (a
    /// roll of 0 falls through), else the universal wage.
    /// </summary>
    int? WageOf(World world, string recordId, CharacterId id, long day, int universal)
    {
        if (data.Find(recordId) is not { } record || !record.Ints.TryGetValue("wages", out int wages) || wages == -1) return null;
        if (wages != 0) return wages;
        if (!record.Ints.TryGetValue("money max", out int max) || max == -1) return null;

        int min = record.GetInt("money min", 0);
        if (min >= max) return universal;

        int roll = min + Rng.Int(Rng.Hash(world.Seed, Rng.Key(id), purpose, (ulong)day), max - min + 1);
        return roll != 0 ? roll : universal;
    }
}
