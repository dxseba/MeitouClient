using HarmonyLib;
using Meitou.Game.Host.Input;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Editing;

namespace Meitou.KenshiCompact;

/// <summary>
/// Port of farghul's Kenshi Compact (GPL-3.0): Shift+/ opens the character editor, in its in-play (recruit) mode, on the main selected character,
/// unless it is in combat mode (melee or ranged). A Harmony prefix on the interface's <c>GameHotkeys.Key</c>, which every game key passes through;
/// the key is not swallowed.
/// </summary>
public sealed class KenshiCompactMod : IMod
{
    internal static readonly CompactSettings Settings = new();

    static Type s_player = null!;
    static System.Reflection.FieldInfo s_owner = null!;

    public void Initialize(ModContext context)
    {
        context.AddSettings(Settings);

        var hotkeys = AccessTools.TypeByName("Meitou.Game.Ui.GameHotkeys") ?? throw new TypeLoadException("Meitou.Game.Ui.GameHotkeys");
        s_player = AccessTools.TypeByName("Meitou.Game.PlayerInterface") ?? throw new TypeLoadException("Meitou.Game.PlayerInterface");
        s_owner = AccessTools.GetDeclaredFields(hotkeys).FirstOrDefault(f => f.FieldType == s_player)
            ?? throw new MissingFieldException(hotkeys.FullName, "player");

        var key = AccessTools.Method(hotkeys, "Key", [typeof(Key), typeof(bool)]) ?? throw new MissingMethodException(hotkeys.FullName, "Key");
        context.Harmony.Patch(key, prefix: new HarmonyMethod(typeof(KenshiCompactMod), nameof(BeforeKey)));
        context.Log("Kenshi Compact initialized");
    }

    #pragma warning disable IDE1006 // Harmony binds __result / __instance by name
    static bool BeforeKey(object __instance, Key key, bool shift)
    #pragma warning restore IDE1006
    {
        if (!Settings.Enabled || key != Key.Slash || !shift) return true;

        var player = s_owner.GetValue(__instance)!;
        if (Traverse.Create(player).Method("Lead").GetValue() is not CharacterSnapshot { CombatMode: false } lead) return true;

        var editor = Traverse.Create(player).Property("CharacterEditor").GetValue()!;
        Traverse.Create(editor).Method("OpenEditor", [typeof(EditKind), typeof(IReadOnlyList<CharacterId>), typeof(CharacterId), typeof(bool)])
            .GetValue(EditKind.Recruit, (IReadOnlyList<CharacterId>)[lead.Id], default(CharacterId), false);
        return true;   // the original's key handler only listens: the key goes on to the game as well
    }
}

public sealed class CompactSettings
{
    [Setting("Shift+/ opens the character editor", Hint = "Edit the selected character's look in play (not in combat mode).")]
    public bool Enabled { get; set; } = true;
}
