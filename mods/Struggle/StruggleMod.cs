using Meitou.Content;
using Meitou.Game.Host.Input;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.Struggle;

/// <summary>
/// Port of 挣扎/Struggle (Workshop 3810561672, <c>Struggle.dll</c> 1.3.1): a carried character gets a "Struggle (Chance NN%)" row on its own and its
/// carrier's right-click menu; a strength and toughness contest frees it or, with failure punishment, gets it slapped and knocked out. Carried NPCs
/// may struggle by themselves. Alt+6 toggles the punishment and Alt+7 NPC struggle (the original's 6+7 and 7+8). README "Facts".
/// </summary>
public sealed class StruggleMod : IMod
{
    internal static readonly StruggleSettings Settings = new();

    /// <summary>The running world's system, for the menu; null before a world starts.</summary>
    internal static volatile StruggleSystem? Running;

    public void Initialize(ModContext context)
    {
        Texts.GameLanguage = context.Language;
        var group = context.AddSettings(Settings);
        context.AddSystem("struggle", _ => Running = new StruggleSystem(context.Population, context.Data, context.RngPurpose), SystemAnchor.End);

        var ui = new StruggleUi(context);
        context.AddUi(ui);
        context.AddFrame(ui.Frame);
        var punish = Binding.Of(Key.D6).With(KeyModifiers.Alt);
        var npc = Binding.Of(Key.D7).With(KeyModifiers.Alt);
        context.AddKeyAction("punish", "Toggle struggle failure punishment",
            _ => ui.Toggle(group, nameof(StruggleSettings.FailPunish), Message.PunishOn, Message.PunishOff), punish);
        context.AddKeyAction("npc", "Toggle NPC struggle", _ => ui.Toggle(group, nameof(StruggleSettings.NpcStruggle), Message.NpcOn, Message.NpcOff), npc);
        MenuPatch.Install(context, ui);
        context.Log("Struggle initialized");
    }
}

/// <summary>
/// The carrier's menu row, the message line (the original's message panel: the last line for the message seconds, top centre) and the toggles.
/// </summary>
sealed class StruggleUi(ModContext context) : IUiExtension
{
    const float ToastWidth = 640, ToastHeight = 40, ToastTop = 0.14f;

    volatile string _toast = "";

    long _toastUntil;

    string _panel = "";

    /// <summary>Whether the player has a selection (a menu row needs one), from the last frame.</summary>
    internal volatile bool HasSelection;

    public string Id => "dxseba.struggle.ui";

    public void Register(UiRegistration registration)
    {
        _panel = registration.RegisterStyle("toast", new UiStyle { Skin = "Kenshi_FloatingPanelThinSkin", TextAlign = "Center" });
        registration.AddCharacterMenuVerbs(CarrierRows);
        if (ChineseFont() is { } font) registration.AddFallbackFont(font);
        registration.Contribute(UiSlots.Overlay, Build, order: 45);
    }

    /// <summary>The carrier's menu: the row of the character it carries.</summary>
    IEnumerable<UiMenuVerb> CarrierRows(UiMenuContext menu)
    {
        var carried = menu.Target.Carrying;
        if (carried.IsNone || Row(carried) is not { } row) return [];

        return [new UiMenuVerb(row.Caption, row.Choose)];
    }

    /// <summary>The Struggle row of <paramref name="victim"/>, or null when it has no offer.</summary>
    internal (string Caption, Action Choose)? Row(CharacterId victim)
    {
        if (!StruggleMod.Settings.Enabled || StruggleMod.Running is not { } system || !system.Offers.TryGetValue(victim, out var offer)) return null;

        return (Texts.Row(offer.Chance, offer.Cooldown), () => context.Enqueue(new StruggleOrder(victim) { Tick = context.Tick }));
    }

    /// <summary>Each frame: the selection, and the system's lines into the message line.</summary>
    internal void Frame(ModFrame frame)
    {
        HasSelection = frame.Selected.Count > 0;
        if (StruggleMod.Running is not { } system) return;

        while (system.Messages.TryDequeue(out var line)) Show(Texts.Of(line.Message, line.Chance, line.Seconds));
    }

    /// <summary>A toggle key: flips the setting (kept) and says its new state.</summary>
    internal void Toggle(ModSettingsGroup group, string key, Message on, Message off)
    {
        var setting = group.Settings.First(s => s.Key == key);
        bool now = !(bool)setting.Get()!;
        setting.Set(now);
        Show(Texts.Of(now ? on : off, 0, 0));
    }

    void Show(string text)
    {
        _toastUntil = Environment.TickCount64 + StruggleMod.Settings.ToastSec * 1000L;
        _toast = text;
    }

    UiNode Build(UiFrameContext frame)
    {
        var root = new UiNode("struggle-toast") { Placement = UiPlacement.Fill, Pickable = false };
        if (_toast.Length == 0 || Environment.TickCount64 > _toastUntil) return root;

        var view = frame.Viewport;
        float scale = Math.Clamp(Math.Min(view.Width / 1920f, view.Height / 1080f), 1, 3);
        float width = ToastWidth * scale, height = ToastHeight * scale;
        root.Children.Add(new UiNode("struggle-toast-line", UiNodeKind.Label)
        {
            Placement = new(width, height, Left: (int)((view.Width - width) / 2), Top: (int)(view.Height * ToastTop)), Text = _toast, Style = _panel,
            TextScale = scale, Pickable = false,
        });
        return root;
    }

    static string? ChineseFont()
    {
        try
        {
            string? path = GameInstall.Locate() is { } install ? Path.Combine(install.Root, "locale", "zh_CN", "gui", "fonts", "TTDaHeiJ.ttf") : null;
            return path is not null && File.Exists(path) ? path : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
