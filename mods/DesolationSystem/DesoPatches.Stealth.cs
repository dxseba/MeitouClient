using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Aggro;
using Meitou.Simulation.Carrying;
using Meitou.Simulation.Crime;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Player;

namespace Meitou.DesolationSystem;

/// <summary>
/// The stealth and crime patches: Master Key (any lock opens at the first roll), Shadow Sovereign's sure stealth knock-out and blue eye, Silent
/// Touch and Shadow Sovereign's crimes without a pending crime, and Silent Verdict's Sneak (270 s out) and Harvest (a silent kill) on a landed
/// stealth knock-out.
/// </summary>
static partial class DesoPatches
{
    /// <summary>Silent Verdict's Sneak knock-out (the original's 270 s).</summary>
    const float VerdictSleep = 270;

    static void InstallStealth(ModContext context)
    {
        Prefix(typeof(Lockpicking), "Roll", nameof(RollPrefix));
        Postfix(typeof(Lockpicking), nameof(Lockpicking.Allowed), nameof(AllowedPostfix));
        Postfix(typeof(CarrySystem), nameof(CarrySystem.StealthKoChance), nameof(StealthKoPostfix));
        Postfix(typeof(CarrySystem), "KnockOutTarget", nameof(KnockOutPostfix));
        Prefix(typeof(CrimeSystem), nameof(CrimeSystem.SetPending), nameof(SetPendingPrefix));
        Postfix(typeof(AggroSystem), "SenseOne", nameof(SenseOnePostfix));
    }

    static bool Sneaking(CharacterCold cold) => (cold.Orders & CharacterOrders.Sneak) != 0;

#pragma warning disable IDE1006 // Harmony's injected parameter names
    /// <summary>Master Key: the roll always unlocks (no wait, no failure).</summary>
    static bool RollPrefix(CharacterCold c, LockState l, ref PickOutcome __result)
    {
        try
        {
            if (!TraitTable.Of(c).MasterKey) return true;

            l.PickTime = 0;
            l.Locked = false;
            l.NextRoll = LockState.FirstRoll;
            __result = PickOutcome.Success;
            return false;
        }
        catch (Exception e)
        {
            Fault(e);
            return true;
        }
    }

    /// <summary>Master Key: a lock of any level may be picked.</summary>
    static void AllowedPostfix(CharacterCold picker, ref bool __result)
    {
        try
        {
            if (__result || !TraitTable.Of(picker).MasterKey) return;

            __result = picker.Medical is not { Incapacitated: true } && picker.CarriedBy.IsNone && picker.Carrying.IsNone
                && picker.Medical?.Aggregates().BothArmsDisabled != true;
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    /// <summary>Shadow Sovereign: a stealth knock-out always lands.</summary>
    static void StealthKoPostfix(CharacterCold attacker, ref float __result)
    {
        try
        {
            if (TraitTable.Of(attacker).SureKill) __result = 100;
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    /// <summary>Silent Verdict: Sneak keeps the target out for 270 s; Harvest kills it.</summary>
    static void KnockOutPostfix(CharacterCold cold, CharacterCold target)
    {
        try
        {
            var verdict = TraitTable.Of(cold).Verdict;
            if (verdict == Verdict.None || target.Medical is not { Dead: false } medical) return;

            if (verdict == Verdict.Harvest) medical.Kill();
            else medical.KnockOut(VerdictSleep);
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    /// <summary>
    /// No pending crime for a Shadow Sovereign, for a Silent Touch character sneaking, or for a Harvest assassin's assault while sneaking: nobody
    /// can witness it, so no alarm and no bounty.
    /// </summary>
    static bool SetPendingPrefix(World world, int culprit, CrimeCode code, ref bool __result)
    {
        try
        {
            if (world.Characters.Cold(culprit) is not { } cold) return true;

            var traits = TraitTable.Of(cold);
            bool silent = traits.NoCrime || Sneaking(cold) && (traits.SilentTouch || traits.Verdict == Verdict.Harvest && code == CrimeCode.Assault);
            if (!silent) return true;

            __result = false;
            return false;
        }
        catch (Exception e)
        {
            Fault(e);
            return true;
        }
    }

    /// <summary>Shadow Sovereign's blue eye: a sneaking sovereign is neither seen nor heard.</summary>
    static void SenseOnePostfix(CharacterCold c, ref bool seen, ref bool heard, ref bool __result)
    {
        try
        {
            if (!TraitTable.Of(c).BlueEye || !Sneaking(c)) return;

            seen = false;
            heard = false;
            __result = false;
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }
#pragma warning restore IDE1006
}
