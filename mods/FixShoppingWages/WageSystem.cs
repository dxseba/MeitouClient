using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Simulation;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Population;

namespace Meitou.FixShoppingWages;

/// <summary>
/// Pays the allowances in the serial slow step when the game day changes (not on the first day seen, so a loaded game keeps its purses until the
/// next day). A squad with a member of fixed <c>wages</c> is left to the engine's own daily pay; any other NPC squad gets its members' daily wages
/// added to its money, up to <see cref="WageSettings.SavingsDays"/> days of them. Members of the player's faction and the poor by design (wages
/// -1, or wages 0 with money max -1) get nothing.
/// </summary>
sealed class WageSystem(WageSettings settings, GameDatabase data, PopulationData population, RngPurpose purpose, int driedMeat) : ITickSystem
{
    /// <summary>A member's daily pay: none (poor or the player's), the engine's (fixed wages), or this mod's amount.</summary>
    readonly record struct Pay(bool Engine, int Amount);

    long _day = long.MinValue;

    public void SlowWorld(World world)
    {
        long day = (long)Math.Floor(world.GameHours / 24);
        bool newDay = _day != long.MinValue && day != _day;
        _day = day;
        if (!newDay || !settings.Enabled) return;

        foreach (var squad in world.Squads.All) PaySquad(world, squad, day);
    }

    void PaySquad(World world, Squad squad, long day)
    {
        if (squad.Dead || population.PlayerFaction >= 0 && squad.Faction == population.PlayerFaction) return;

        long daily = 0;
        foreach (var id in squad.Members)
        {
            if (!world.Characters.TryResolveNext(id, out int slot) || world.Characters.Cold(slot) is not { } cold || cold.IsPlayer) continue;

            var pay = PayOf(world, cold.RecordId, id, day);
            if (pay.Engine) return;

            daily += pay.Amount;
        }

        long cap = daily * settings.SavingsDays;
        if (daily > 0 && squad.Money < cap) squad.Money = Math.Min(cap, squad.Money + daily);
    }

    Pay PayOf(World world, string recordId, CharacterId id, long day)
    {
        if (data.Find(recordId) is not { Type: FcsRecordType.Character } record) return new(false, 0);

        int wages = record.GetInt("wages", 0), min = record.GetInt("money min", 0), max = record.GetInt("money max", 0);
        if (wages >= 1) return new(true, 0);
        if (wages < 0 || wages == 0 && max < 0) return new(false, 0);
        if (max > 0) return new(false, min + Rng.Int(Rng.Hash(world.Seed, Rng.Key(id), purpose, (ulong)day), Math.Max(1, max - min + 1)));

        return new(false, settings.UseOverride ? settings.OverrideWage : driedMeat > 0 ? driedMeat : settings.FallbackWage);
    }
}
