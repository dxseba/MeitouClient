using Meitou.Game.Host;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using Meitou.Brutality.Ui;
using Meitou.Data.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.Brutality;

/// <summary>Supplies stock player UI bindings for the shared Brutality progression and preferences overlay.</summary>
internal sealed class StockProgressionUi(Type ui)
{
    static StockProgressionUi? installed;
    readonly FieldInfo? session = AccessTools.GetDeclaredFields(ui).SingleOrDefault(field => field.FieldType == typeof(WorldSession));
    readonly MethodInfo? lead = AccessTools.Method(ui, "Lead", Type.EmptyTypes);
    readonly PropertyInfo? title = AccessTools.Property(ui, "TitleOpen");
    readonly PropertyInfo editor = StockHostParts.Property(ui, "CharacterEditor");
    readonly PropertyInfo editorOpen = StockHostParts.Property(StockHostParts.Property(ui, "CharacterEditor").PropertyType, "EditorOpen");

    internal static void Install(ModContext context)
    {
        BrutalityUiAdapter.Preferences = new(context.Folder);
        if (AccessTools.TypeByName("Meitou.Game.PlayerInterface") is not { } ui) return;
        var binding = new StockProgressionUi(ui);
        if (binding.session is null || binding.lead is null || AccessTools.Method(ui, "BindUi", Type.EmptyTypes) is not { } bind)
        {
            context.Log("Brutality progression HUD unavailable on this host");
            return;
        }
        context.Harmony.Patch(bind, postfix: new HarmonyMethod(typeof(StockProgressionUi), nameof(BindProgression)));
        installed = binding;
        context.AddUi(new BrutalityUiExtension(BrutalityUiAdapter.Preferences));
    }

    static void BindProgression(object __instance, UiBindings __result) => installed?.Bind(__instance, __result);

    void Bind(object player, UiBindings bindings)
    {
        if (session?.GetValue(player) is not WorldSession shown || title?.GetValue(player) is true || editorOpen.GetValue(editor.GetValue(player)) is true) return;
        bindings.Text["brutality.available"] = "1";
        if (lead?.Invoke(player, null) is not CharacterSnapshot actor || StockProgression.For(shown.World) is not { } progression) return;

        progression.TryExperience(actor.Id, out float experience);
        var progress = new BrutalityProgress(experience);
        int level = (int)MathF.Floor(progress.Level);
        bindings.Text["brutality.caption"] = string.Create(CultureInfo.InvariantCulture, $"Brutality  {level}");
        bindings.Text["brutality.progress"] = level >= BrutalityProgress.MaximumLevel
            ? "Mastered"
            : string.Create(CultureInfo.InvariantCulture, $"Next level: {progress.Progress01:P0}");
        bindings.Text["brutality.chance"] = string.Create(CultureInfo.InvariantCulture,
            $"Cleave chance on eligible hits: {BrutalityPolicy.BisectionChance(progress.Level):P0}");
    }
}
