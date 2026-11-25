using System.Globalization;
using Meitou.Content;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Meitou.DesolationSystem;

/// <summary>
/// The original's control bar and monitor (<c>DivineGraceUI.dll</c>): cards stacked at the top centre, 300 by 36 pixels with 4 between, scaled with
/// the screen as the Speed Cap tuner's (scope, attack speed cap, Supreme Realms switch, the inspected character's Ryu style, language), and below
/// them the active perks of the HUD's character with each one's codex text. F8 shows and hides both. Clicks change the settings
/// (<see cref="ModSetting.Set"/>, so they are kept) or queue a <see cref="SetStyle"/>.
/// </summary>
sealed class DesoCards(ModContext context, ModSettingsGroup group) : IUiExtension
{
    const float CardWidth = 300, CardHeight = 36, CardGap = 4, TopMargin = 0.004f, PanelWidth = 520, Line = 20;

    string _card = "", _panel = "", _text = "", _scope = "", _cap = "", _realms = "", _style = "", _language = "";
    string _verdict = "", _config = "", _codex = "", _toggle = "", _button = "";

    /// <summary>What the panel under the cards shows: the monitor, the config window or the codex.</summary>
    volatile Page _page;

    enum Page
    {
        Monitor,
        Config,
        Codex,
    }

    /// <summary>The HUD's character, from the frame callback.</summary>
    internal volatile CharacterIdBox? Inspected;

    public string Id => "dxseba.desolationsystem.cards";

    public void Register(UiRegistration registration)
    {
        _card = registration.RegisterStyle("card", new UiStyle { Skin = "Kenshi_FloatingPanelThinSkin", TextAlign = "Center" });
        _panel = registration.RegisterStyle("panel", new UiStyle { Background = new(.08f, .07f, .06f, .9f) });
        _text = registration.RegisterStyle("text", new UiStyle { Text = new(.91f, .86f, .74f, 1), TextPadding = new(6, 1, 6, 1) });
        _scope = registration.RegisterAction("scope", _ => CycleScope());
        _cap = registration.RegisterAction("cap", _ => CycleCap());
        _realms = registration.RegisterAction("realms", _ => ToggleRealms());
        _style = registration.RegisterAction("style", _ => CycleStyle());
        _language = registration.RegisterAction("language", _ => Set(nameof(DesoSettings.Language),
            Settings.InChinese ? DesoSettings.English : DesoSettings.Chinese));
        _verdict = registration.RegisterAction("verdict", _ => CycleVerdict());
        _config = registration.RegisterAction("config", _ => _page = _page == Page.Config ? Page.Monitor : Page.Config);
        _codex = registration.RegisterAction("codex", _ => _page = _page == Page.Codex ? Page.Monitor : Page.Codex);
        _toggle = registration.RegisterAction("toggle", a => Toggle(a.Argument));
        _button = registration.RegisterStyle("switch", new UiStyle
        {
            Background = new(.20f, .15f, .11f, .95f), Hover = new(.32f, .22f, .16f, 1), Text = new(.91f, .86f, .74f, 1), TextPadding = new(6, 1, 6, 1),
        });
        if (ChineseFont() is { } font) registration.AddFallbackFont(font);
        registration.Contribute(UiSlots.Overlay, Build, order: 44);
    }

    static DesoSettings Settings => DesolationSystemMod.Settings;

    internal void ToggleHidden() => Set(nameof(DesoSettings.Hidden), !Settings.Hidden);

    internal void CycleScope() => Set(nameof(DesoSettings.Scope), DesoSettings.Scopes[(Settings.ScopeIndex + 1) % DesoSettings.Scopes.Length]);

    internal void ToggleRealms() => Set(nameof(DesoSettings.Ascension), !Settings.Ascension);

    void CycleCap()
    {
        var tiers = DesoSettings.CapTiers;
        Set(nameof(DesoSettings.Cap), tiers[(Array.IndexOf(tiers, Settings.Cap) + 1) % tiers.Length]);
        if (Settings.CustomCap) Set(nameof(DesoSettings.CustomCap), false);
    }

    /// <summary>The Ryu card: Gale, Balance, Crush in turn for the inspected character (melee attack 60 and up; the system checks).</summary>
    void CycleStyle()
    {
        if (Inspected is not { } box || DesolationSystemMod.Running?.View.Active.GetValueOrDefault(box.Id) is not { } shown) return;

        context.Enqueue(new SetStyle(box.Id, (shown.Style + 1) % 3) { Tick = context.Tick });
    }

    void CycleVerdict()
    {
        var modes = DesoSettings.VerdictModes;
        Set(nameof(DesoSettings.AssassinateMode), modes[(Array.IndexOf(modes, Settings.AssassinateMode) + 1) % modes.Length]);
    }

    /// <summary>The config window's switch: flips one on/off setting.</summary>
    void Toggle(string key)
    {
        if (group.Settings.FirstOrDefault(s => s.Key == key && s.Kind == ModSettingKind.Switch) is { } setting) setting.Set(!(bool)setting.Get()!);
    }

    void Set(string key, object value) => group.Settings.First(s => s.Key == key).Set(value);

    UiNode Build(UiFrameContext frame)
    {
        var root = new UiNode("deso-cards") { Placement = UiPlacement.Fill, Pickable = false };
        if (Settings.Hidden) return root;

        var view = frame.Viewport;
        float scale = Math.Clamp(Math.Min(view.Width / 1920f, view.Height / 1080f), 1, 3);
        float width = (int)(scale * CardWidth + 0.5f), height = (int)(scale * CardHeight + 0.5f), gap = (int)(scale * CardGap + 0.5f);
        float left = (int)((view.Width - width) / 2), top = (int)(view.Height * TopMargin);
        bool zh = Settings.InChinese;
        var shown = Inspected is { } box ? DesolationSystemMod.Running?.View.Active.GetValueOrDefault(box.Id) : null;
        string[] texts =
        [
            Texts.Scope(Settings.ScopeIndex, zh), Texts.Cap(Settings.AttackCap, zh), Texts.Realms(Settings.Ascension, zh),
            Texts.Style(shown?.Style ?? -1, zh), Texts.Verdict(Settings.AssassinateMode, zh), Texts.Config(zh), Texts.Codex(zh), Texts.Language(zh),
        ];
        string[] actions = [_scope, _cap, _realms, _style, _verdict, _config, _codex, _language];
        for (int i = 0; i < texts.Length; i++)
        {
            root.Children.Add(new UiNode("deso-card" + i, UiNodeKind.Button)
            {
                Placement = new(width, height, Left: left, Top: top + i * (height + gap)), Text = texts[i], Action = actions[i], Style = _card,
                TextScale = scale,
            });
        }

        var panel = Window(top + texts.Length * (height + gap), view.Width);
        panel.Children.Add(Label("deso-title", Texts.Panel(zh) + (shown is null ? "" : " - " + shown.Name)));
        switch (_page)
        {
            case Page.Config:
                ConfigPage(panel);
                break;
            case Page.Codex:
                CodexPage(panel, shown, zh);
                break;
            default:
                Monitor(panel, shown, zh);
                break;
        }

        root.Children.Add(panel);
        return root;
    }

    UiNode Window(float top, float screenWidth) => new("deso-panel", UiNodeKind.ScrollView)
    {
        Placement = new(PanelWidth, 420, Left: (int)((screenWidth - PanelWidth) / 2), Top: top), Style = _panel, Flow = UiFlow.Column, Padding = 4,
        Gap = 2,
    };

    /// <summary>The monitor: the inspected character's active perks with their codex text.</summary>
    void Monitor(UiNode panel, Shown? shown, bool zh)
    {
        if (shown is null)
        {
            panel.Children.Add(Label("deso-none", Texts.NoCharacter(zh)));
            return;
        }

        if (shown.Perks.Length == 0) panel.Children.Add(Label("deso-none", Texts.NoPerks(zh)));
        for (int i = 0; i < shown.Perks.Length; i++) panel.Children.Add(Perk("deso-perk" + i, shown.Perks[i], "", zh));
    }

    /// <summary>The config window: every on/off setting; a click flips it (kept as the setting).</summary>
    void ConfigPage(UiNode panel)
    {
        var switches = group.Settings.Where(s => s.Kind == ModSettingKind.Switch && s.Key != nameof(DesoSettings.Hidden)).ToList();
        for (int i = 0; i < switches.Count; i++)
        {
            var s = switches[i];
            string state = s.Get() is true ? "#80E080[ON]  #D0C8B0" : "#E08080[OFF] #D0C8B0";
            panel.Children.Add(new UiNode("deso-switch" + i, UiNodeKind.Button)
            {
                Placement = new(UiLength.Fill, Line + 2), Text = state + context.Tr(s.Label) + "  (" + s.Hint + ")", Action = _toggle,
                Argument = s.Key, Style = _button,
            });
        }
    }

    /// <summary>The codex: every perk the port knows, marked when the inspected character has it.</summary>
    void CodexPage(UiNode panel, Shown? shown, bool zh)
    {
        var ids = Texts.Ids;
        for (int i = 0; i < ids.Count; i++)
        {
            bool has = shown?.Perks.Contains(ids[i]) == true;
            panel.Children.Add(Perk("deso-codex" + i, ids[i], has ? "#80E080+ " : "#808080- ", zh));
        }
    }

    UiNode Perk(string node, string id, string mark, bool zh)
    {
        string text = Texts.Description(id, zh).Replace("|", ": ");
        return Label(node, mark + "#E0C070" + Texts.Name(id, zh) + "#D0C8B0" + (text.Length > 0 ? "  " + text : ""));
    }

    UiNode Label(string id, string text) =>
        new(id, UiNodeKind.Label) { Placement = new(UiLength.Fill, Line * Lines(text)), Text = text, Style = _text };

    static int Lines(string text) => Math.Clamp(1 + text.Length / 80, 1, 6);

    /// <summary>The game's Chinese font for the Chinese captions; null without an install.</summary>
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

/// <summary>A character id boxed for a volatile field.</summary>
sealed record CharacterIdBox(CharacterId Id);
