using HarmonyLib;
using Meitou.Data;
using Meitou.Game.Host.Input;
using Meitou.Modding;

namespace Meitou.GearCompareAlt;

/// <summary>
/// Port of Gear Compare Alt (QoL) (workshop 3812150687, its plugin GearCompare.dll): hold ALT over armour or a weapon and a second panel beside
/// the item's tooltip compares it with what is worn in that slot, with three axes and a verdict; on the Crafting tab two right-clicked recipes
/// compare as A and B. Texts in the game's language from the original's catalogues; the original's settings under Mod settings.
/// </summary>
public sealed class GearCompareAltMod : IMod
{
    ModContext? _context;

    internal GearSettings Settings { get; } = new();

    internal GameDatabase Data => _context!.Data;

    internal string Tr(string english) => _context!.Tr(english);

    internal void Log(string text) => _context?.Log(text);

    internal void LogError(string text) => _context?.LogError(text);

    /// <summary>The original's folder (its catalogues, ini and highlight picture); null when it is not installed.</summary>
    internal string? Original { get; private set; }

    ModKeyAction? _hold;
    HoldKey _holdKey;

    public void Initialize(ModContext context)
    {
        _context = context;
        Original = OriginalFiles.Folder(context);
        if (Original is { } original) context.AddTranslations("gear_compare", original);
        else context.Log("the original mod (Workshop 3812150687) is not installed: texts stay English, the defaults are the port's");

        if (GearIni.Apply(Original, Settings, context.Log) is { } ini) context.Log($"defaults from {Path.GetFileName(ini)}");

        var group = context.AddSettings(Settings);
        AddHoldKey(context);
        group.Changed += setting =>
        {
            if (setting.Key == nameof(GearSettings.HoldKey)) RebindHold();
        };

        context.AddFrame(Frame);
        HoverPanel.Use(this);
        CraftCompare.Use(this);
        Patch(context.Harmony);
        context.Log("Gear Compare Alt initialized");
    }

    /// <summary>
    /// The hold key's action ("Show while held" on the Controls page), bound by default to the <c>HoldKey</c> setting's key or chord; a modifier
    /// alone binds nothing (the frame's modifier state is read instead).
    /// </summary>
    void AddHoldKey(ModContext context)
    {
        _holdKey = HoldKey.Parse(Settings.HoldKey);
        if (_holdKey.Error is { } error) context.Log($"HoldKey {error}: ALT");

        Binding[] defaults = _holdKey.Binding is { } b ? [b] : [];
        _hold = context.AddKeyAction("hold", "Show while held", _ => { }, defaults);
    }

    /// <summary>The setting changed: the action takes its key or chord, or none (on the interface's thread, where settings change).</summary>
    void RebindHold()
    {
        _holdKey = HoldKey.Parse(Settings.HoldKey);
        if (_holdKey.Error is { } error) Log($"HoldKey {error}: ALT");

        bool bound = _holdKey.Binding is { } b ? _hold?.Rebind(b) == true : _hold?.Rebind() == true;
        if (!bound) Log($"HoldKey '{Settings.HoldKey}' could not be bound");
    }

    /// <summary>
    /// Whether the hold key is down: always for NONE; the action's key or chord when it has one (a mouse button's modifiers tested apart); else
    /// every modifier the setting names.
    /// </summary>
    internal bool HoldDown()
    {
        var k = _holdKey;
        if (k.Always) return true;
        if (_hold is { Keys.Count: > 0 } action) return action.Held && (k.Binding is not { IsMouse: true } || ModifiersHeld(k.Modifiers));

        return k.Binding is null && ModifiersHeld(k.Modifiers);
    }

    static bool ModifiersHeld(KeyModifiers m) =>
        (!m.HasFlag(KeyModifiers.Alt) || FrameState.Alt) && (!m.HasFlag(KeyModifiers.Ctrl) || FrameState.Ctrl)
        && (!m.HasFlag(KeyModifiers.Shift) || FrameState.Shift);

    static void Frame(ModFrame frame)
    {
        (FrameState.Snapshot, FrameState.Selected, FrameState.Shown) = (frame.Snapshot, frame.Selected, frame.Shown);
        (FrameState.Alt, FrameState.Ctrl, FrameState.Shift) = (frame.Alt, frame.Ctrl, frame.Shift);
        bool open = frame.IsOpen("overview");
        if (FrameState.OverviewOpen && !open) CraftCompare.Clear();
        FrameState.OverviewOpen = open;
    }

    void Patch(Harmony harmony)
    {
        if (GameUi.HoverReady)
        {
            harmony.Patch(GameUi.ItemLines, postfix: new HarmonyMethod(typeof(HoverPanel), "ItemLinesPostfix"));
            harmony.Patch(GameUi.PlaceTooltip, postfix: new HarmonyMethod(typeof(HoverPanel), "PlacePostfix"));
        }
        else LogError("TooltipRuntime.ItemLines, PlaceTooltip or CombatOf not found: no comparison panel");

        if (GameUi.CraftReady)
        {
            harmony.Patch(GameUi.MouseDown, prefix: new HarmonyMethod(typeof(CraftCompare), "MouseDownPrefix"));
            harmony.Patch(GameUi.AddCraftContent, postfix: new HarmonyMethod(typeof(CraftCompare), "AddCraftContentPostfix"));
        }
        else LogError("the Crafting tab's members were not found: no recipe comparison");
    }
}
