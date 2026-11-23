using Meitou.Game.Host.Input;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.KenshiCompact;

/// <summary>
/// Port of farghul's Kenshi Compact (GPL-3.0): Shift+/ opens the character editor, in its in-play (recruit) mode, on the character the HUD shows,
/// unless it is in combat mode (melee or ranged). A key action (default /, rebindable on the Controls page) that acts only with Shift held, as the
/// script's handler; it opens the editor through <see cref="ModContext.Interface"/>.
/// </summary>
public sealed class KenshiCompactMod : IMod
{
    internal static readonly CompactSettings Settings = new();

    ModInterface _interface = null!;

    public void Initialize(ModContext context)
    {
        context.AddSettings(Settings);
        _interface = context.Interface;
        context.AddKeyAction("editor", "Character editor (with Shift)", Pressed, Key.Slash);
        context.Log("Kenshi Compact initialized");
    }

    void Pressed(ModKeyContext key)
    {
        if (!Settings.Enabled || !key.Shift || key.Frame?.Shown is not { } shown) return;

        var lead = key.Snapshot.Characters.FirstOrDefault(c => c.Id == shown);
        if (lead is null || lead.CombatMode) return;

        _interface.OpenScreen(ModScreen.CharacterEditor, ScreenTarget.Of(lead.Id));
    }
}

public sealed class CompactSettings
{
    [Setting("Shift+/ opens the character editor", Hint = "Edit the selected character's look in play (not in combat mode).")]
    public bool Enabled { get; set; } = true;
}
