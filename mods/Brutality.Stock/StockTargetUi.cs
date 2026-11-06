using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Engine;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;

namespace Meitou.Brutality;

internal static class StockTargetUi
{
    static FieldInfo? session, menu, mouse;
    static MethodInfo? buildMenu;

    internal static void Install(ModContext context)
    {
        if (AccessTools.TypeByName("Meitou.Game.PlayerInterface") is not { } ui) return;
        session = ui.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .SingleOrDefault(field => field.FieldType == typeof(WorldSession));
        menu = AccessTools.Field(ui, "contextMenu");
        mouse = AccessTools.Field(ui, "mouse");
        buildMenu = AccessTools.Method(ui, "Menu", [typeof(string), typeof(Vector2), typeof(List<(string, Action)>)]);
        var verbs = AccessTools.Method(ui, "Verbs", [typeof(CharacterSnapshot)]);
        if (session is null || menu is null || mouse is null || buildMenu is null || verbs is null)
        { context.Log("Brutality body-part menu unavailable on this host"); return; }
        context.Harmony.Patch(verbs, postfix: new HarmonyMethod(typeof(StockTargetUi), nameof(TargetVerbs)));
    }

    static bool CanTarget(CharacterSnapshot target) => !target.IsPlayer && target.HostileToPlayer
        && target.CarriedBy.IsNone && !target.Imprisoned && target.Prison is null && StockTargetSystem.Helpless(target);

    static CharacterId[] Attackers(WorldSession active, CharacterSnapshot target) => active.CurrentSnapshot.Characters
        .Where(actor => actor.Selected && actor.IsPlayer && !actor.Animal && actor.Id != target.Id && actor.CarriedBy.IsNone
            && !actor.Imprisoned && actor.Turret is null && actor.Body is { Dead: false, Unconscious: false, Coma: false })
        .Select(actor => actor.Id).ToArray();

    static void TargetVerbs(object __instance, CharacterSnapshot target, List<(string Caption, Action Give)> __result)
    {
        if (!CanTarget(target) || session?.GetValue(__instance) is not WorldSession active || Attackers(active, target).Length == 0) return;
        __result.RemoveAll(verb => verb.Caption is "Attack Target" or "Attack All" or "Attack Unprovoked");
        __result.Add(("Attack body part...", () => Open(__instance, target.Id)));
    }

    static void Open(object ui, CharacterId id)
    {
        if (session?.GetValue(ui) is not WorldSession active
            || active.CurrentSnapshot.Characters.FirstOrDefault(actor => actor.Id == id) is not { } target || !CanTarget(target)) return;
        var attackers = Attackers(active, target);
        if (attackers.Length == 0) return;
        var entries = new List<(string Caption, Action Give)>();
        void Add(string caption, int part, string? template = null) => entries.Add((caption, () =>
        {
            if (ReferenceEquals(session?.GetValue(ui), active))
                active.World.Commands.Enqueue(new StockTargetOrder(attackers, id, part, template) { Tick = active.World.Tick });
        }));
        Add("Any body part", -1);
        if (target.HealthParts.Count != 0)
        {
            for (int index = 0; index < target.HealthParts.Count; index++)
            {
                var part = target.HealthParts[index];
                if (part.Limb != LimbState.Stump) Add(part.Name.Length != 0 ? part.Name : part.Type.ToString(), index, part.Id);
            }
        }
        else if (active.World.Systems.OfType<StockTargetSystem>().FirstOrDefault() is { } targeting)
        {
            var parts = targeting.MenuParts(id);
            for (int index = 0; index < parts.Count; index++)
                if (parts[index].Present) Add(parts[index].Name, index, parts[index].Template);
        }
        var at = mouse?.GetValue(ui) is Vector2 cursor ? cursor : Vector2.Zero;
        menu?.SetValue(ui, buildMenu?.Invoke(ui, [target.Name + " — Attack", at, entries]));
    }
}
