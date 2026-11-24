using System.Globalization;
using Meitou.Content;
using Meitou.Game.Host.Input;
using Meitou.Game.Ui;
using Meitou.Modding;

namespace Meitou.UncapDefence;

/// <summary>
/// The original's six cards (<b>Verified</b>, <c>UncapDefenceSuccessRate.dll</c>): 340 by 36 pixels with 4 between, times min(width / 1920,
/// height / 1080) held to [1, 3], at 0.6 % of the width and 0.4 % of the height from the top left; skin <c>Kenshi_FloatingPanelThinSkin</c>. A click
/// cycles the mode, the scale (the full gap in Threshold mode), the ceiling or the language, or waits for a new toggle key (Esc cancels, as does a
/// click); the sixth shows the last block and dodge chance changed. Settings are changed through <see cref="ModSetting.Set"/>, so they are kept.
/// </summary>
sealed class UncapCards(ModSettingsGroup group) : IUiExtension
{
    const float CardWidth = 340, CardHeight = 36, CardGap = 4, LeftMargin = 0.006f, TopMargin = 0.004f;

    static readonly string[] EnglishModes = ["OFF", "AMPLIFY", "THRESHOLD"], ChineseModes = ["关闭", "放大", "阈值"];

    /// <summary>The toggle key action, for its key's name and the rebind.</summary>
    internal ModKeyAction? Toggle { get; set; }

    volatile bool _rebinding;

    string _card = "", _mode = "", _scale = "", _ceiling = "", _rebind = "", _language = "";

    public string Id => "dxseba.uncapdefence.cards";

    public void Register(UiRegistration registration)
    {
        _card = registration.RegisterStyle("card", new UiStyle { Skin = "Kenshi_FloatingPanelThinSkin", TextAlign = "Center" });
        _mode = registration.RegisterAction("mode", _ => Click(() => Cycle(nameof(UncapSettings.Mode), UncapSettings.Modes)));
        _scale = registration.RegisterAction("scale", _ => Click(CycleScale));
        _ceiling = registration.RegisterAction("ceiling", _ => Click(() => Cycle(nameof(UncapSettings.Ceiling), UncapSettings.Ceilings)));
        _rebind = registration.RegisterAction("rebind", _ => Click(() => _rebinding = true));
        _language = registration.RegisterAction("language", _ => Click(SwitchLanguage));
        registration.AddKeyInput(Rebind);
        if (ChineseFont() is { } font) registration.AddFallbackFont(font);
        registration.Contribute(UiSlots.Overlay, Build, order: 44);
    }

    internal void ToggleHidden() => Set(nameof(UncapSettings.Hidden), !UncapDefenceMod.Settings.Hidden);

    void Click(Action act)
    {
        if (_rebinding)
        {
            _rebinding = false;
            return;
        }

        act();
    }

    bool Rebind(UiKeyInput input)
    {
        if (!_rebinding || input.Kind != UiKeyInputKind.Down) return false;

        _rebinding = false;
        if (!input.Cancel) Toggle?.Rebind(Binding.Of(input.Key).With(input.Modifiers));
        return true;
    }

    /// <summary>The second card: the full gap in Threshold mode, else the pressure scale.</summary>
    void CycleScale()
    {
        if (UncapDefenceMod.Settings.Mode == UncapSettings.Threshold) Cycle(nameof(UncapSettings.FullGap), UncapSettings.Gaps);
        else Cycle(nameof(UncapSettings.Scale), UncapSettings.Scales);
    }

    void Cycle(string key, string[] tiers)
    {
        string now = (string)Setting(key).Get()!;
        Set(key, tiers[(Array.IndexOf(tiers, now) + 1) % tiers.Length]);
    }

    void SwitchLanguage() => Set(nameof(UncapSettings.Language), UncapDefenceMod.Settings.InChinese ? UncapSettings.English : UncapSettings.Chinese);

    void Set(string key, object value) => Setting(key).Set(value);

    ModSetting Setting(string key) => group.Settings.First(s => s.Key == key);

    UiNode Build(UiFrameContext frame)
    {
        var s = UncapDefenceMod.Settings;
        var root = new UiNode("uncap-cards") { Placement = UiPlacement.Fill, Pickable = false };
        if (s.Hidden) return root;

        var view = frame.Viewport;
        float scale = Math.Clamp(Math.Min(view.Width / 1920f, view.Height / 1080f), 1, 3);
        float width = (int)(scale * CardWidth + 0.5f), height = (int)(scale * CardHeight + 0.5f), gap = (int)(scale * CardGap + 0.5f);
        float left = (int)(view.Width * LeftMargin), top = (int)(view.Height * TopMargin);
        string[] texts = [ModeText(s), ScaleText(s), CeilingText(s), KeyText(s), s.InChinese ? "English" : "中文", ReadoutText(s)];
        string[] actions = [_mode, _scale, _ceiling, _rebind, _language, ""];
        for (int i = 0; i < texts.Length; i++)
        {
            root.Children.Add(new UiNode("uncap-card" + i, actions[i].Length > 0 ? UiNodeKind.Button : UiNodeKind.Label)
            {
                Placement = new(width, height, Left: left, Top: top + i * (height + gap)), Text = texts[i], Action = actions[i], Style = _card,
                TextScale = scale,
            });
        }

        return root;
    }

    static string ModeText(UncapSettings s)
    {
        int mode = Math.Max(0, Array.IndexOf(UncapSettings.Modes, s.Mode));
        return s.InChinese ? $"【防御解限】{ChineseModes[mode]}" : $"[Uncap] {EnglishModes[mode]}";
    }

    static string ScaleText(UncapSettings s)
    {
        if (s.Mode == UncapSettings.Threshold) return s.InChinese ? $"满值级差 {s.FullGap} 级" : $"Full-gap {s.FullGap} lv";

        return s.InChinese ? $"压制尺度 {s.Scale} 级" : $"Scale {s.Scale} lv";
    }

    static string CeilingText(UncapSettings s)
    {
        if (s.Ceiling == "100") return s.InChinese ? "天花板 100%" : "Ceiling 100%";

        string value = float.Parse(s.Ceiling, CultureInfo.InvariantCulture).ToString("0.0", CultureInfo.InvariantCulture);
        return s.InChinese ? $"天花板 {value}%" : $"Ceiling {value}%";
    }

    string KeyText(UncapSettings s)
    {
        if (_rebinding) return s.InChinese ? "显隐键 >> 请按键 <<" : "Hotkey >> press a key <<";

        string key = Toggle?.Keys is { Count: > 0 } keys ? keys[0].ToString() : "-";
        return s.InChinese ? $"显隐键 {key} (点击改)" : $"Hotkey {key} (click)";
    }

    static string ReadoutText(UncapSettings s)
    {
        Readout? block = UncapFilter.LastBlock, dodge = UncapFilter.LastDodge;
        if (block is null && dodge is null) return s.InChinese ? "读数: 待战斗" : "Readout: idle";

        string b = Shown(s, block), d = Shown(s, dodge);
        return s.InChinese ? $"格挡 {b}  闪避 {d}" : $"Blk {b}  Ddg {d}";
    }

    /// <summary>One chance: the mod's, and the game's after it when they differ (mode on).</summary>
    static string Shown(UncapSettings s, Readout? r)
    {
        if (r is null) return "-";

        string now = Percent(r.Now);
        if (s.Mode == UncapSettings.Off || r.Now == r.Vanilla) return now;

        return s.InChinese ? $"{now}(原{Percent(r.Vanilla)})" : $"{now}(was {Percent(r.Vanilla)})";
    }

    static string Percent(float fraction) => (fraction * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

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
