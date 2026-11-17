using Meitou.Game.Host;
using System.Reflection;
using HarmonyLib;
using Meitou.Data.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.KillCounter;

/// <summary>Adds the selected character's tally to the HUD bindings (a postfix on the private <c>PlayerInterface.BindUi</c>, as Brutality does).</summary>
static class HudAdapter
{
    public const string Key = "killcounter.text";
    static FieldInfo? s_sessionField;
    static MethodInfo? s_leadMethod;

    public static void Install(ModContext context)
    {
        var type = AccessTools.TypeByName("Meitou.Game.PlayerInterface");
        if (type is null)
        {
            context.Log("no PlayerInterface: HUD line disabled");
            return;
        }

        s_sessionField = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SingleOrDefault(f => f.FieldType == typeof(WorldSession))
            ?? throw new MissingFieldException(type.FullName, "WorldSession");
        s_leadMethod = AccessTools.Method(type, "Lead") ?? throw new MissingMethodException(type.FullName, "Lead");
        var bind = AccessTools.Method(type, "BindUi") ?? throw new MissingMethodException(type.FullName, "BindUi");
        context.Harmony.Patch(bind, postfix: new HarmonyMethod(typeof(HudAdapter), nameof(Bound)));
    }

    #pragma warning disable IDE1006 // Harmony binds __result / __instance by name
    static void Bound(object __instance, UiBindings __result)
    #pragma warning restore IDE1006
    {
        if (s_sessionField?.GetValue(__instance) is not WorldSession session || s_leadMethod?.Invoke(__instance, null) is not CharacterSnapshot lead) return;
        if (session.World.Systems.OfType<KillCounterSystem>().FirstOrDefault() is not { } counter) return;

        if (!counter.Published.TryGetValue(lead.Id, out var tally)) return;
        __result.Text[Key] = $"Kills {tally.Kills}   Knockouts {tally.Knockouts}   Limbs {tally.Limbs}";
    }
}
