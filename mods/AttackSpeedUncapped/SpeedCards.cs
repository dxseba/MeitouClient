using System.Globalization;
using Meitou.Content;
using Meitou.Game.Host.Input;
using Meitou.Game.Ui;
using Meitou.Modding;

namespace Meitou.AttackSpeedUncapped;

/// <summary>
/// The original's four cards (<b>Verified</b>, <c>SpeedCapTuner.dll</c>): 300 by 36 pixels with 4 between, times min(width / 1920, height / 1080)
/// held to [1, 3], stacked from 0.4 % of the screen height down, centred; skin <c>Kenshi_FloatingPanelThinSkin</c>. A click cycles the attack cap
/// (1.2, 2.4, 4.8, 9.6, unlimited), the block cap (4, 8, 12, unlimited) or the language, or waits for a new toggle key (Esc cancels, as does a
/// click). The toggle key hides and shows them. Settings are changed through <see cref="ModSetting.Set"/>, so they are kept.
/// </summary>
sealed class SpeedCards(ModSettingsGroup group) : IUiExtension
{
    const float CardWidth = 300, CardHeight = 36, CardGap = 4, TopMargin = 0.004f;

    /// <summary>The toggle key action, for its key's name and the rebind.</summary>
    internal ModKeyAction? Toggle { get; set; }

    /// <summary>Waiting for the toggle key's new key.</summary>
    volatile bool _rebinding;

    string _card = "", _attack = "", _block = "", _language = "", _rebind = "";

    public string Id => "dxseba.attackspeeduncapped.cards";

    public void Register(UiRegistration registration)
    {
        _card = registration.RegisterStyle("card", new UiStyle { Skin = "Kenshi_FloatingPanelThinSkin", TextAlign = "Center" });
        _attack = registration.RegisterAction("attack", _ => Click(() => Cycle(nameof(SpeedCapSettings.AttackCap), SpeedCapSettings.AttackTiers)));
        _block = registration.RegisterAction("block", _ => Click(() => Cycle(nameof(SpeedCapSettings.BlockCap), SpeedCapSettings.BlockTiers)));
        _language = registration.RegisterAction("language", _ => Click(SwitchLanguage));
        _rebind = registration.RegisterAction("rebind", _ => Click(() => _rebinding = true));
        registration.AddKeyInput(Rebind);
        if (ChineseFont() is { } font) registration.AddFallbackFont(font);
        registration.Contribute(UiSlots.Overlay, Build, order: 43);
    }

    /// <summary>The toggle key: the cards hide or show (kept as the Hidden setting).</summary>
    internal void ToggleHidden() => Set(nameof(SpeedCapSettings.Hidden), !AttackSpeedUncappedMod.Settings.Hidden);

    /// <summary>A click on a card; while a new key is awaited it only cancels the wait, as the original's.</summary>
    void Click(Action act)
    {
        if (_rebinding)
        {
            _rebinding = false;
            return;
        }

        act();
    }

    /// <summary>While a new toggle key is awaited the next key is taken for it; Esc cancels.</summary>
    bool Rebind(UiKeyInput input)
    {
        if (!_rebinding || input.Kind != UiKeyInputKind.Down) return false;

        _rebinding = false;
        if (!input.Cancel) Toggle?.Rebind(Binding.Of(input.Key).With(input.Modifiers));
        return true;
    }

    void Cycle(string key, string[] tiers)
    {
        string now = (string)Setting(key).Get()!;
        Set(key, tiers[(Array.IndexOf(tiers, now) + 1) % tiers.Length]);
    }

    void SwitchLanguage()
    {
        var settings = AttackSpeedUncappedMod.Settings;
        Set(nameof(SpeedCapSettings.Language), settings.InChinese ? SpeedCapSettings.English : SpeedCapSettings.Chinese);
    }

    void Set(string key, object value) => Setting(key).Set(value);

    ModSetting Setting(string key) => group.Settings.First(s => s.Key == key);

    UiNode Build(UiFrameContext frame)
    {
        var settings = AttackSpeedUncappedMod.Settings;
        var root = new UiNode("speedcap-cards") { Placement = UiPlacement.Fill, Pickable = false };
        if (settings.Hidden) return root;

        var view = frame.Viewport;
        float scale = Math.Clamp(Math.Min(view.Width / 1920f, view.Height / 1080f), 1, 3);
        float width = (int)(scale * CardWidth + 0.5f), height = (int)(scale * CardHeight + 0.5f), gap = (int)(scale * CardGap + 0.5f);
        float left = (int)((view.Width - width) / 2), top = (int)(view.Height * TopMargin);
        string[] texts = [AttackText(settings), BlockText(settings), settings.InChinese ? "English" : "中文", KeyText(settings)];
        string[] actions = [_attack, _block, _language, _rebind];
        for (int i = 0; i < texts.Length; i++)
        {
            root.Children.Add(new UiNode("speedcap-card" + i, UiNodeKind.Button)
            {
                Placement = new(width, height, Left: left, Top: top + i * (height + gap)), Text = texts[i], Action = actions[i], Style = _card,
                TextScale = scale,
            });
        }

        return root;
    }

    static string AttackText(SpeedCapSettings s)
    {
        if (s.AttackCap == SpeedCapSettings.Unlimited) return s.InChinese ? "攻速上限 无限制" : "Attack Cap UNLIMITED";

        string cap = s.Attack.ToString("0.0", CultureInfo.InvariantCulture);
        return s.InChinese ? $"攻速上限 {cap}x" : $"Attack Cap {cap}x";
    }

    static string BlockText(SpeedCapSettings s)
    {
        if (s.BlockCap == SpeedCapSettings.Unlimited) return s.InChinese ? "格挡上限 无限制" : "Block Cap UNLIMITED";

        string cap = s.Block.ToString("0", CultureInfo.InvariantCulture);
        return s.InChinese ? $"格挡上限 {cap}" : $"Block Cap {cap}";
    }

    string KeyText(SpeedCapSettings s)
    {
        if (_rebinding) return s.InChinese ? "<< 呼出键: 按新键 (Esc取消) <<" : "<< Toggle key: press new key (Esc to cancel) <<";

        string key = Toggle?.Keys is { Count: > 0 } keys ? keys[0].ToString() : "-";
        return s.InChinese ? $"呼出键 [{key}] (点击改键)" : $"Toggle key [{key}] (click to rebind)";
    }

    /// <summary>The game's Chinese font (<c>locale/zh_CN/gui/fonts/TTDaHeiJ.ttf</c>) for the Chinese captions; null without an install.</summary>
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
