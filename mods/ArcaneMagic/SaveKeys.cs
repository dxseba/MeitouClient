using HarmonyLib;
using Meitou.Data.Save;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Population;

namespace Meitou.ArcaneMagic;

/// <summary>
/// The original's faction save keys (README "Facts"): the player's GAMESTATE_FACTION gets <c>AM_summon_unlocked</c>, <c>AM_earth_spike_unlocked</c>,
/// <c>AM_arcane_bomb_unlocked</c> and <c>AM_whirlwind_unlocked</c> (floats 1 or 0, as <c>Faction::saveState</c> writes them), so a save taken to the
/// original keeps the unlocks. Harmony: a postfix on <c>Meitou.Simulation.Saving.SaveCapture.WriteFactionsAndTowns</c>, which runs between ticks
/// while the save is captured and changes only the save's records, never the world. A failure logs once and writes nothing.
/// </summary>
static class SaveKeys
{
    static ModContext? s_context;
    static int s_failed;

    public static void Patch(ModContext context)
    {
        s_context = context;
        var write = AccessTools.Method("Meitou.Simulation.Saving.SaveCapture:WriteFactionsAndTowns");
        if (write is null)
        {
            context.Log("SaveCapture.WriteFactionsAndTowns not found: the unlocks are kept in the port's save data only");
            return;
        }

        context.Harmony.Patch(write, postfix: new HarmonyMethod(typeof(SaveKeys), nameof(Written)));
    }

    static void Written(SaveBuilder builder, PopulationData data)
    {
        if (s_failed != 0 || ArcaneSystem.Running is not { } system || data.PlayerFaction < 0) return;

        try
        {
            string player = data.Factions[data.PlayerFaction].Id;
            foreach (var faction in builder.Game.Factions)
            {
                if (faction.Id != player) continue;

                var floats = faction.Record.Floats;
                floats["AM_summon_unlocked"] = system.Unlocked(Spell.Summon) ? 1 : 0;
                floats["AM_earth_spike_unlocked"] = system.Unlocked(Spell.EarthSpike) ? 1 : 0;
                floats["AM_arcane_bomb_unlocked"] = system.Unlocked(Spell.Bomb) ? 1 : 0;
                floats["AM_whirlwind_unlocked"] = system.Unlocked(Spell.Whirlwind) ? 1 : 0;
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            if (Interlocked.Exchange(ref s_failed, 1) == 0) s_context?.LogError($"Arcane faction save keys failed ({e.GetType().Name}: {e.Message})");
        }
    }
}
