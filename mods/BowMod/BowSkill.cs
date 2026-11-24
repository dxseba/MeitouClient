using HarmonyLib;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;

namespace Meitou.BowMod;

/// <summary>
/// The Bows skill on CROSSBOW records with <c>bowmod bow</c> set (README "Facts"). While such a weapon's holder runs the game's ranged steps, the
/// game reads its Crossbows level as the Bows level's effective proficiency (<see cref="Effective"/>; the injury, gear and light multipliers
/// still apply) and the Crossbows experience those steps give goes to Bows, as the original does by swapping the stat inside the game's crossbow
/// functions. Done by Harmony around <c>Meitou.Simulation.Combat.CombatSystem.TickRanged</c> (reload time, aim, reload and aim experience) and
/// <c>Meitou.Simulation.Combat.CombatSystem.ApplyFire</c> (the shot's spread and its skill): a prefix sets the stored Crossbows level to the
/// effective Bows level, a finalizer puts it back and moves what it gained to Bows. The steps run in the holder's own partition, so only its own
/// stats change, deterministically. The HUD's Current Skill line (<c>Meitou.Simulation.World.SkillLine</c>, published per character by one
/// worker) takes the same swap, so its accuracy figure is the bow's. A failure logs once and leaves the vanilla step as it is.
/// </summary>
static class BowSkill
{
    /// <summary>Where a swap stands between its prefix and finalizer.</summary>
    public readonly record struct Swap(CharacterStats? Stats, float Saved, float Effective);

    static HashSet<string> s_bows = [];
    static ModSkill? s_skill;
    static ModContext? s_context;
    static int s_failed;

    /// <summary>
    /// The effective proficiency of a Bows level (the original's curve): 0.65 L up to 60, then 39 + 61 ((L - 60) / 40)^1.25, 100 from 100 up;
    /// 0 at or below 0.
    /// </summary>
    public static float Effective(float level) => level switch
    {
        <= 0 or float.NaN => 0,
        <= 60 => level * 0.65f,
        < 100 => MathF.Pow((level - 60) / 40, 1.25f) * 61 + 39,
        _ => 100,
    };

    public static void Patch(ModContext context, ModSkill skill)
    {
        s_context = context;
        s_skill = skill;
        s_bows = [.. context.Data.OfType(FcsRecordType.Crossbow).Where(r => r.GetBool("bowmod bow")).Select(r => r.StringId)];
        context.Log($"{s_bows.Count} bows");

        const string combat = "Meitou.Simulation.Combat.CombatSystem";
        var tick = AccessTools.Method($"{combat}:TickRanged");
        var fire = AccessTools.Method($"{combat}:ApplyFire");
        if (tick is null || fire is null)
        {
            context.LogError($"{combat}.TickRanged or ApplyFire not found: bows use Crossbows");
            return;
        }

        var finalizer = new HarmonyMethod(typeof(BowSkill), nameof(Finalizer));
        context.Harmony.Patch(tick, prefix: new HarmonyMethod(typeof(BowSkill), nameof(TickPrefix)), finalizer: finalizer);
        context.Harmony.Patch(fire, prefix: new HarmonyMethod(typeof(BowSkill), nameof(FirePrefix)), finalizer: finalizer);

        // The HUD's Current Skill line (its accuracy figure) of a bow holder, published from the same swap.
        if (AccessTools.Method("Meitou.Simulation.World:SkillLine") is { } line)
            context.Harmony.Patch(line, prefix: new HarmonyMethod(typeof(BowSkill), nameof(TickPrefix)), finalizer: finalizer);
        else
            context.Log("World.SkillLine not found: the HUD shows the Crossbows figure for bows");
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void TickPrefix(CharacterCold cold, out Swap __state) => __state = Begin(cold);

    static void FirePrefix(World world, in Effect e, out Swap __state) => __state = Begin(world.Characters.Cold(e.Source));

    static Exception? Finalizer(Exception? __exception, Swap __state)
    {
        End(__state);
        return __exception;
    }
#pragma warning restore IDE1006

    /// <summary>Sets a bow holder's stored Crossbows level to its effective Bows level; no swap for anyone else.</summary>
    static Swap Begin(CharacterCold? cold)
    {
        try
        {
            if (s_skill is not { } skill || cold is not { Stats: { } stats, Fighter.Crossbow.Data.StringId: { } id } || !s_bows.Contains(id))
                return default;

            float saved = stats.Stored(StatsEnumerated.Crossbows);
            if (float.IsNaN(saved)) return default;

            float effective = Effective(skill.Level(stats));
            stats[StatsEnumerated.Crossbows] = effective;
            return new Swap(stats, saved, effective);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Failed(e);
            return default;
        }
    }

    /// <summary>
    /// Puts the Crossbows level back and trains Bows by what the step gave: the gain on the effective level taken back through the core curve
    /// (<c>((101 - level) / 101)^2</c>) and the Crossbows XP rate, then given to Bows on its own level.
    /// </summary>
    static void End(Swap swap)
    {
        if (swap.Stats is not { } stats) return;

        float gained = stats.Stored(StatsEnumerated.Crossbows) - swap.Effective;
        stats[StatsEnumerated.Crossbows] = swap.Saved;
        try
        {
            float curve = (CharacterStats.XpCap - swap.Effective) / CharacterStats.XpCap;
            float rate = CrossbowRate(stats);
            if (gained > 0 && curve > 0 && rate > 0) s_skill!.Gain(stats, gained / (curve * curve) / rate);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Failed(e);
        }
    }

    /// <summary>The rate Crossbows gains were multiplied by: its own Squad XP rate when set, else the character's XP rate.</summary>
    static float CrossbowRate(CharacterStats stats)
    {
        int s = (int)StatsEnumerated.Crossbows;
        return stats.SkillXpRates is { } rates && s < rates.Count && rates[s] >= 0 ? rates[s] : stats.XpRate;
    }

    static void Failed(Exception e)
    {
        if (Interlocked.Exchange(ref s_failed, 1) == 0) s_context?.LogError($"bow skill step failed ({e.GetType().Name}: {e.Message})");
    }
}
