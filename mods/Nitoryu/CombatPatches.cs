using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Gameplay.Construction;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;

namespace Meitou.Nitoryu;

/// <summary>
/// The rank skills that act inside the engine's own steps, by Harmony (README "Harmony"). All run where the engine runs the step (serial Commit,
/// or the fighter's own partition of the act phase) and read only state the serial update or the serial observers settled:
/// <list type="bullet">
/// <item>pressure: a transpiler on <c>CombatSystem.HasInitiative</c> routes its two <c>CombatSystem.Eff</c> reads through
/// <see cref="InitiativeEff"/>, which adds the asker's pressure points to its own skill (the original adds them to <c>whoAttacksYouOrMe</c>'s
/// score);</item>
/// <item>the same body part: a prefix and finalizer on <c>CombatSystem.LandBlow</c> name the part, a prefix on <c>MedicalState.Rolled</c> takes
/// it;</item>
/// <item>the left blade onto a flank enemy: a prefix on <c>CombatSystem.ApplyBlow</c> applies the blow to the flanker instead;</item>
/// <item>left-hand fighting: a transpiler on <c>CombatSystem.FollowArms</c> routes <c>Fighter.SetArms</c> through <see cref="SetArms"/>;</item>
/// <item>the Pair Training Dummies: a postfix on <c>ProductionSystem.TrainingXp</c>.</item>
/// </list>
/// A failure logs once and leaves the engine's step as it is.
/// </summary>
static class CombatPatches
{
    delegate void ApplyBlowCall(CombatSystem combat, World world, in Effect e);

    /// <summary>The fighters that fight left-handed (written by the serial update).</summary>
    public static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Fighter, object> LeftHanded = [];

    static readonly object Marker = new();

    static ApplyBlowCall? s_applyBlow;
    static Func<System.Numerics.Vector3, System.Numerics.Vector3, CharacterCold?, CharacterCold?, float>? s_gap;
    static MedicalState? s_forcedBody;
    static int s_forcedPart = -1;
    static bool s_splitting;
    static float s_continuousRate = 0.02f;
    static ModContext? s_context;
    static int s_failed;

    public static void Patch(ModContext context)
    {
        s_context = context;
        var combat = typeof(CombatSystem);
        PatchInitiative(context, combat);
        PatchScissor(context, combat);
        PatchSplit(context, combat);
        PatchArms(context, combat);
        PatchTraining(context);
    }

    #region Pressure

    static void PatchInitiative(ModContext context, Type combat)
    {
        if (AccessTools.Method(combat, "HasInitiative") is not { } initiative || AccessTools.Method(combat, "Eff") is null)
        {
            context.LogError("CombatSystem.HasInitiative or Eff not found: no pressure");
            return;
        }

        context.Harmony.Patch(initiative, transpiler: new HarmonyMethod(typeof(CombatPatches), nameof(InitiativeTranspiler)));
    }

    static IEnumerable<CodeInstruction> InitiativeTranspiler(IEnumerable<CodeInstruction> code)
    {
        var eff = AccessTools.Method(typeof(CombatSystem), "Eff", [typeof(CharacterCold), typeof(StatsEnumerated)]);
        var mine = AccessTools.Method(typeof(CombatPatches), nameof(InitiativeEff));
        foreach (var instruction in code)
        {
            if (instruction.Calls(eff)) yield return new CodeInstruction(OpCodes.Call, mine).MoveLabelsFrom(instruction);
            else yield return instruction;
        }
    }

    /// <summary>The engine's stat, plus the asker's pressure on its target and minus the target's on it, for the asker of this thread's choice.</summary>
    public static float InitiativeEff(CharacterCold cold, StatsEnumerated stat)
    {
        float value = CombatAccess.Eff(cold, stat);
        try
        {
            return value + AttackChoice.PressureShift(cold);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Failed(e);
            return value;
        }
    }

    #endregion

    #region Same body part

    static void PatchScissor(ModContext context, Type combat)
    {
        var land = AccessTools.Method(combat, "LandBlow");
        var rolled = AccessTools.Method(typeof(MedicalState), "Rolled");
        if (land is null || rolled is null)
        {
            context.LogError("CombatSystem.LandBlow or MedicalState.Rolled not found: twin strikes hit parts at random");
            return;
        }

        context.Harmony.Patch(land, prefix: new HarmonyMethod(typeof(CombatPatches), nameof(LandPrefix)),
            finalizer: new HarmonyMethod(typeof(CombatPatches), nameof(LandFinalizer)));
        context.Harmony.Patch(rolled, prefix: new HarmonyMethod(typeof(CombatPatches), nameof(RolledPrefix)));
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static void LandPrefix(World world, in Effect e, CharacterCold dc)
    {
        s_forcedBody = null;
        try
        {
            if (NitoryuSystem.Of(world) is { } system && system.SamePart(world, e) is { } part and >= 0)
            {
                (s_forcedBody, s_forcedPart) = (dc.Medical, part);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Failed(ex);
        }
    }

    static Exception? LandFinalizer(Exception? __exception)
    {
        s_forcedBody = null;
        return __exception;
    }

    static bool RolledPrefix(MedicalState __instance, ref int __result)
    {
        if (!ReferenceEquals(s_forcedBody, __instance) || s_forcedPart < 0 || s_forcedPart >= __instance.Parts.Count) return true;

        __result = s_forcedPart;
        return false;
    }
#pragma warning restore IDE1006

    #endregion

    #region Split

    static void PatchSplit(ModContext context, Type combat)
    {
        var apply = AccessTools.Method(combat, "ApplyBlow");
        var gap = AccessTools.Method(typeof(CombatTuning), "Gap");
        if (apply is null || gap is null)
        {
            context.LogError("CombatSystem.ApplyBlow or CombatTuning.Gap not found: the left blade stays on the target");
            return;
        }

        s_applyBlow = apply.CreateDelegate<ApplyBlowCall>();
        s_gap = gap.CreateDelegate<Func<System.Numerics.Vector3, System.Numerics.Vector3, CharacterCold?, CharacterCold?, float>>();
        context.Harmony.Patch(apply, prefix: new HarmonyMethod(typeof(CombatPatches), nameof(ApplyPrefix)));
    }

    /// <summary>The gap between two characters as combat counts it (bodies taken off).</summary>
    public static float Gap(World world, int a, int b)
    {
        var prev = world.Characters.Previous;
        var table = world.Characters;
        return s_gap?.Invoke(prev[a].Position, prev[b].Position, table.Cold(a), table.Cold(b))
            ?? System.Numerics.Vector3.Distance(prev[a].Position, prev[b].Position);
    }

#pragma warning disable IDE1006 // Harmony's injected parameter names
    static bool ApplyPrefix(CombatSystem __instance, World world, in Effect e)
    {
        if (s_splitting || e.Kind != CombatSystem.BlowEffect || s_applyBlow is null) return true;

        try
        {
            if (NitoryuSystem.Of(world)?.Flanker(world, e) is not { } flanker) return true;

            var moved = e;
            moved.Target = flanker;
            s_splitting = true;
            s_applyBlow(__instance, world, moved);
            return false;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Failed(ex);
            return true;
        }
        finally
        {
            s_splitting = false;
        }
    }
#pragma warning restore IDE1006

    #endregion

    #region Left hand

    static void PatchArms(ModContext context, Type combat)
    {
        if (AccessTools.Method(combat, "FollowArms") is not { } follow)
        {
            context.LogError("CombatSystem.FollowArms not found: no left-hand fighting");
            return;
        }

        context.Harmony.Patch(follow, transpiler: new HarmonyMethod(typeof(CombatPatches), nameof(ArmsTranspiler)));
    }

    static IEnumerable<CodeInstruction> ArmsTranspiler(IEnumerable<CodeInstruction> code)
    {
        var set = AccessTools.Method(typeof(Fighter), nameof(Fighter.SetArms));
        var mine = AccessTools.Method(typeof(CombatPatches), nameof(SetArms));
        foreach (var instruction in code)
        {
            if (instruction.Calls(set)) yield return new CodeInstruction(OpCodes.Call, mine).MoveLabelsFrom(instruction);
            else yield return instruction;
        }
    }

    /// <summary>
    /// <c>Fighter.SetArms</c>, except that a left-handed fighter (Dual Wielding 50+, the left arm working, the right not) keeps its blade: the
    /// fighter takes the right arm as working, so the weapon in hand is the one the game would give with both.
    /// </summary>
    public static bool SetArms(Fighter fighter, bool right, bool left)
    {
        if (!right && left && LeftHanded.TryGetValue(fighter, out _)) right = true;

        return fighter.SetArms(right, left);
    }

    public static void MarkLeftHanded(Fighter fighter, bool on)
    {
        if (on) LeftHanded.AddOrUpdate(fighter, Marker);
        else LeftHanded.Remove(fighter);
    }

    #endregion

    #region Training

    static void PatchTraining(ModContext context)
    {
        var training = AccessTools.Method("Meitou.Simulation.Production.ProductionSystem:TrainingXp");
        if (AccessTools.Field(typeof(XpService), "ContinuousRate") is { IsLiteral: true } rate && rate.GetRawConstantValue() is float r)
            s_continuousRate = r;
        if (training is null)
        {
            context.LogError("ProductionSystem.TrainingXp not found: the Pair Training Dummies do not train Dual Wielding");
            return;
        }

        context.Harmony.Patch(training, postfix: new HarmonyMethod(typeof(CombatPatches), nameof(TrainingPostfix)));
    }

    /// <summary>The training dummies' rate per second (<c>ProductionSystem.TrainingRate</c> x <c>XpService.ContinuousRate</c>).</summary>
    public static float TrainingPerSecond => 2 * s_continuousRate;

    static void TrainingPostfix(CharacterCold cold, MachineData data, float dt)
    {
        try
        {
            NitoryuSystem.Latest?.Train(cold, data, dt);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Failed(e);
        }
    }

    #endregion

    static void Failed(Exception e)
    {
        if (Interlocked.Exchange(ref s_failed, 1) == 0) s_context?.LogError($"dual wielding step failed ({e.GetType().Name}: {e.Message})");
    }
}
