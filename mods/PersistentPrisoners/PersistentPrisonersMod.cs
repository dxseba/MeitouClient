using HarmonyLib;
using Meitou.Game.Host.Input;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Factions;

namespace Meitou.PersistentPrisoners;

/// <summary>
/// Port of PersistentPrisoners (workshop 3798215878, GPL-3.0-or-later; rebuilt from its description): a platoon holding a prisoner in a
/// player-owned cage is always kept when it leaves the loaded area, whatever the keep switches say; an overview of the prisoners (Ctrl+Shift+P);
/// optional feeding instead of starvation.
/// </summary>
public sealed class PersistentPrisonersMod : IMod
{
    internal static readonly PrisonerSettings Settings = new();

    /// <summary>The running world's system; null before a world starts.</summary>
    internal static volatile PrisonerSystem? Running;

    internal static PrisonerWindow Window { get; private set; } = null!;

    public void Initialize(ModContext context)
    {
        context.AddSettings(Settings);
        context.AddSystem("prisoners", _ => Running = new PrisonerSystem(Settings, context.Population), SystemAnchor.End);
        Window = new PrisonerWindow();
        context.AddUi(Window);
        Hooks.Install(context);
        context.Log("PersistentPrisoners initialized");
    }
}

public sealed class PrisonerSettings
{
    [Setting("Keep caged prisoners", Hint = "A prisoner in one of your cages is never despawned when you leave the area.")]
    public bool Protect { get; set; } = true;

    [Setting("Starvation", Hint = "Off: your caged prisoners are slowly fed up to the minimum below instead of starving.")]
    public bool Starvation { get; set; } = true;

    [Setting("Minimum nutrition", Hint = "With starvation off, prisoners are fed up to this (the game's 0-300 hunger scale).", Min = 1, Max = 300)]
    public int MinimumNutrition { get; set; } = 100;

    [Setting("Overview shortcut", Hint = "Ctrl+Shift+P opens the prisoner overview.")]
    public bool Shortcut { get; set; } = true;
}

/// <summary>
/// The hooks: a postfix on the population's keep test (<c>PopulationSystem.Kept</c>) keeps a platoon with a prisoner in a player cage, and a
/// prefix on <c>GameHotkeys.Key</c> takes Ctrl+Shift+P.
/// </summary>
static class Hooks
{
    static System.Reflection.FieldInfo? s_ctrlField;
    static System.Reflection.PropertyInfo? s_shiftProperty;
    static System.Reflection.FieldInfo? s_hotkeysPlayer;

    public static void Install(ModContext context)
    {
        var kept = AccessTools.Method(typeof(Simulation.Population.PopulationSystem), "Kept", [typeof(Platoon)])
            ?? throw new MissingMethodException("PopulationSystem", "Kept");
        context.Harmony.Patch(kept, postfix: new HarmonyMethod(typeof(Hooks), nameof(AfterKept)));

        var player = AccessTools.TypeByName("Meitou.Game.PlayerInterface") ?? throw new TypeLoadException("Meitou.Game.PlayerInterface");
        s_ctrlField = AccessTools.Field(player, "CtrlHeld") ?? throw new MissingFieldException(player.FullName, "CtrlHeld");
        s_shiftProperty = AccessTools.Property(player, "SlowModifier") ?? throw new MissingMemberException(player.FullName, "SlowModifier");

        var hotkeys = AccessTools.TypeByName("Meitou.Game.Ui.GameHotkeys") ?? throw new TypeLoadException("Meitou.Game.Ui.GameHotkeys");
        s_hotkeysPlayer = AccessTools.GetDeclaredFields(hotkeys).FirstOrDefault(f => f.FieldType == player)
            ?? throw new MissingFieldException(hotkeys.FullName, "player");
        var key = AccessTools.Method(hotkeys, "Key", [typeof(Key), typeof(bool)]) ?? throw new MissingMethodException(hotkeys.FullName, "Key");
        context.Harmony.Patch(key, prefix: new HarmonyMethod(typeof(Hooks), nameof(BeforeKey)));
    }

    #pragma warning disable IDE1006 // Harmony binds __result / __instance by name
    static void AfterKept(Platoon p, ref bool __result)
    {
        if (!__result && PersistentPrisonersMod.Settings.Protect && PersistentPrisonersMod.Running?.Protects(p) == true) __result = true;
    }

    static bool BeforeKey(object __instance, Key key)
    {
        if (key != Key.P || !PersistentPrisonersMod.Settings.Shortcut) return true;

        var player = s_hotkeysPlayer?.GetValue(__instance);
        if (player is null || s_ctrlField?.GetValue(player) is not true || s_shiftProperty?.GetValue(player) is not true) return true;

        PersistentPrisonersMod.Window.Toggle();
        return false;
    }
    #pragma warning restore IDE1006
}
