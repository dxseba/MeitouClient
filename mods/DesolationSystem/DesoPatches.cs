using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;

namespace Meitou.DesolationSystem;

/// <summary>
/// The perks the registration surface has no hook for, as Harmony patches on Meitou's own methods (README "Harmony"). Each reads a table the
/// system fills in its serial step (<see cref="Guard"/> per body, <see cref="Traits"/> per character, the reach per fighter); a body or
/// character without an entry is left as it is. Small methods the JIT may inline are reached through their callers (<see cref="Calls"/>). A throw
/// is logged once and the call goes on unchanged. This part: the body (Iron Guard, Toughness Reduction, Prosthetic Tuning, Physical
/// Sanctification's limb lock and KO immunity, Master Porter's speed).
/// </summary>
static partial class DesoPatches
{
    /// <summary>A body's patched perks: Iron Guard's flat reductions, the injury penalty reduction, limb lock, KO immunity, prosthetic tuning.</summary>
    internal readonly record struct Guard(float Limb, float Vital, float Injury, bool LimbLock, bool KoImmune, bool Tuned, bool NoDark);

    static readonly ConditionalWeakTable<MedicalState, StrongBox<Guard>> Guards = new();
    static ModContext? s_context;
    static int s_faulted;

    static Action<MedicalState, float, List<MedicalEvent>?>? s_restartKo;
    static Func<World, int, CharacterCold, RaceData, CharacterStats, MedicalState, float, float>? s_topSpeed;

    internal static void Install(ModContext context)
    {
        s_context = context;
        Prefix(typeof(MedicalState), nameof(MedicalState.ApplyHit), nameof(ApplyHitPrefix));
        Patch(typeof(StatMultiplier), nameof(StatMultiplier.Compute), prefix: nameof(ComputePrefix), postfix: nameof(ComputePostfix));
        Postfix(typeof(StatMultiplier), nameof(StatMultiplier.LimbItemFactor), nameof(LimbItemPostfix));
        Postfix(typeof(MedicalState), "VitalPartsKeepDown", nameof(KeepDownPostfix));

        var restart = AccessTools.Method(typeof(MedicalState), "RestartKo");
        if (restart is not null) s_restartKo = AccessTools.MethodDelegate<Action<MedicalState, float, List<MedicalEvent>?>>(restart);
        Calls.Swap(context, restart, Method(nameof(RestartKo)), AccessTools.Method(typeof(MedicalState), "CheckPart"));

        var top = AccessTools.Method(typeof(BodySystem), "TopSpeedNow");
        if (top is not null) s_topSpeed = AccessTools.MethodDelegate<Func<World, int, CharacterCold, RaceData, CharacterStats, MedicalState, float, float>>(top);
        Calls.Swap(context, top, Method(nameof(TopSpeed)), AccessTools.Method(typeof(BodySystem), "ActOn"));

        InstallCombat(context);
        InstallStealth(context);
        InstallCraft(context);
    }

    static MethodInfo Method(string name) => AccessTools.Method(typeof(DesoPatches), name);

    static void Prefix(Type type, string method, string patch) => Patch(type, method, prefix: patch);

    static void Postfix(Type type, string method, string patch) => Patch(type, method, postfix: patch);

    static void Patch(Type type, string method, string? prefix = null, string? postfix = null)
    {
        if (AccessTools.Method(type, method) is not { } target)
        {
            s_context?.LogError($"{type.Name}.{method} not found: its perk is off");
            return;
        }

        s_context!.Harmony.Patch(target, prefix: prefix is null ? null : new HarmonyMethod(Method(prefix)),
            postfix: postfix is null ? null : new HarmonyMethod(Method(postfix)));
    }

    /// <summary>Sets a body's entry (serial step); an empty guard drops it.</summary>
    internal static void SetGuard(MedicalState medical, Guard guard)
    {
        if (guard == default)
        {
            Guards.Remove(medical);
            return;
        }

        if (Guards.TryGetValue(medical, out var box)) box.Value = guard;
        else Guards.Add(medical, new StrongBox<Guard>(guard));
    }

    static Guard GuardOf(MedicalState medical) => Guards.TryGetValue(medical, out var box) ? box.Value : default;

#pragma warning disable IDE1006 // Harmony's injected parameter names
    /// <summary>Iron Guard's flat reduction, then the limb lock: a limb never goes below 1 HP from a blow.</summary>
    static void ApplyHitPrefix(MedicalState __instance, int part, ref HitDamage hit)
    {
        try
        {
            var guard = GuardOf(__instance);
            if (guard == default || (uint)part >= (uint)__instance.Parts.Count) return;

            var p = __instance.Parts[part];
            bool limb = p.Template.Type is BodyPartType.Arm or BodyPartType.Leg;
            float flat = DesoSystem.Dealing ? 0 : p.IsVital ? guard.Vital : limb ? guard.Limb : 0;
            Scale(ref hit, flat, float.MaxValue);
            if (guard.LimbLock && limb && !p.IsVital) Scale(ref hit, 0, Math.Max(0, p.Flesh - p.Stun + p.Rig - 1));
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    /// <summary>Takes <paramref name="flat"/> off the blow, then holds what it does to the part to <paramref name="most"/>, in proportion.</summary>
    static void Scale(ref HitDamage hit, float flat, float most)
    {
        float sum = hit.Cut + hit.Blunt + hit.Pierce + hit.ExtraStun;
        if (sum <= 0) return;

        float left = Math.Min(Math.Max(0, sum - flat), most);
        if (left >= sum) return;

        float keep = left / sum;
        hit = hit with { Cut = hit.Cut * keep, Blunt = hit.Blunt * keep, Pierce = hit.Pierce * keep, ExtraStun = hit.ExtraStun * keep };
    }

    /// <summary>Heavenly Artifice: no darkness penalty on the stats.</summary>
    static void ComputePrefix(MedicalState medical, ref float light)
    {
        try
        {
            if (light < 1 && GuardOf(medical).NoDark) light = 1;
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    static void ComputePostfix(MedicalState medical, ref float __result)
    {
        try
        {
            if (__result >= 1 || GuardOf(medical).Injury is var injury && injury <= 0) return;

            __result = 1 - (1 - __result) * (1 - Math.Clamp(injury, 0, 1));
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    /// <summary>Prosthetic Tuning: a fitted limb's stat bonus (a factor above 1) grows by a quarter.</summary>
    static void LimbItemPostfix(MedicalState medical, ref float __result)
    {
        try
        {
            if (__result > 1 && GuardOf(medical).Tuned) __result = 1 + (__result - 1) * 1.25f;
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }

    /// <summary>Physical Sanctification: vital parts under 0 never keep the character out.</summary>
    static void KeepDownPostfix(MedicalState __instance, ref bool __result)
    {
        try
        {
            if (__result && GuardOf(__instance).KoImmune) __result = false;
        }
        catch (Exception e)
        {
            Fault(e);
        }
    }
#pragma warning restore IDE1006

    /// <summary><c>CheckPart</c>'s call of <c>RestartKo</c>: a blow leaving a vital part under 0 knocks out only a body without KO immunity.</summary>
    static void RestartKo(MedicalState medical, float seconds, List<MedicalEvent>? events)
    {
        bool skip = false;
        try
        {
            skip = GuardOf(medical).KoImmune;
        }
        catch (Exception e)
        {
            Fault(e);
        }

        if (!skip) s_restartKo!(medical, seconds, events);
    }

    /// <summary><c>BodySystem.ActOn</c>'s call of <c>TopSpeedNow</c>: Master Porter's +25 % while encumbered or carrying someone.</summary>
    static float TopSpeed(World world, int i, CharacterCold cold, RaceData race, CharacterStats stats, MedicalState medical, float encumbrance)
    {
        float speed = s_topSpeed!(world, i, cold, race, stats, medical, encumbrance);
        try
        {
            float porter = TraitTable.Of(cold).PorterSpeed;
            if (porter > 1 && (encumbrance < 1 || !cold.Carrying.IsNone)) speed *= porter;
        }
        catch (Exception e)
        {
            Fault(e);
        }

        return speed;
    }

    static void Fault(Exception e)
    {
        if (Interlocked.Exchange(ref s_faulted, 1) == 0) s_context?.LogError($"a Desolation patch failed and is skipped: {e}");
    }
}
