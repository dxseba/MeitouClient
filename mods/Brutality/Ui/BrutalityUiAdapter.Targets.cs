using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.Brutality.Ui;

public static partial class BrutalityUiAdapter
{
    static MethodInfo? menuMethod;
    static FieldInfo? menuField, mouseField;
    static void InstallTargetMenu(ModContext context, Type type)
    {
        menuMethod = AccessTools.Method(type, "Menu");
        menuField = AccessTools.Field(type, "contextMenu"); mouseField = AccessTools.Field(type, "mouse");
        context.Harmony.Patch(AccessTools.Method(type, "Verbs"), postfix: new HarmonyMethod(typeof(BrutalityUiAdapter), nameof(TargetVerbs)));
    }
    static void TargetVerbs(object __instance, CharacterSnapshot target, List<(string Caption, Action Give)> __result)
    {
        if (Session(__instance) is not { } session || !target.HostileToPlayer || !IncapacitatedCombatAdapter.IsHelpless(target)) return;
        var attackers = session.CurrentSnapshot.Characters.Where(c => c.Selected && IncapacitatedCombatAdapter.CanAttack(c, target, null)).Select(c => c.Id).ToArray();
        if (attackers.Length == 0) return;
        __result.RemoveAll(verb => verb.Caption is "Attack Target" or "Attack Unprovoked" or "Attack All");
        __result.Add(("Attack body part...", () => OpenBodyPartMenu(__instance, target.Id)));
    }
    static void OpenBodyPartMenu(object ui, CharacterId targetId)
    {
        if (Session(ui) is not { } session || session.CurrentSnapshot.Characters.FirstOrDefault(c => c.Id == targetId) is not { } target
            || !target.HostileToPlayer || !IncapacitatedCombatAdapter.IsHelpless(target)) return;
        var selected = session.CurrentSnapshot.Characters.Where(c => c.Selected).ToArray();
        var entries = new List<(string Caption, Action Give)>();
        void Add(string caption, int part)
        {
            var attackers = selected.Where(c => IncapacitatedCombatAdapter.CanAttack(c, target, null, part)).Select(c => c.Id).ToArray();
            if (attackers.Length == 0) return;
            entries.Add((caption, () =>
            {
                if (Session(ui) is { } live) live.World.Commands.Enqueue(new TargetBodyPartAttackOrder(attackers, targetId, part) { Tick = live.CurrentSnapshot.Tick });
            }));
        }
        Add("Any body part", -1);
        for (int part = 0; part < target.HealthParts.Count; part++)
            Add(target.HealthParts[part].Name.Length > 0 ? target.HealthParts[part].Name : target.HealthParts[part].Type.ToString(), part);
        if (entries.Count == 0 || menuMethod is null || menuField is null) return;
        var at = mouseField?.GetValue(ui) is Vector2 mouse ? mouse : Vector2.Zero;
        menuField.SetValue(ui, menuMethod.Invoke(ui, [target.Name + " — Attack", at, entries]));
    }
}
