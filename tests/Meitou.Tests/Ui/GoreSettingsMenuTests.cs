using System.Collections;
using System.Numerics;
using System.Reflection;
using Meitou.Content;
using Meitou.Data;
using Meitou.Data.Ogre;
using Meitou.Data.Ui;
using Meitou.Engine;
using Meitou.Engine.Input;
using Meitou.Game;
using Meitou.Rendering;
using Meitou.Simulation;
using Meitou.Tests.Simulation;

namespace Meitou.Tests.Ui;

public sealed class GoreSettingsMenuTests
{
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory, Slow]
    [InlineData(false)]
    [InlineData(true)]
    public void Gameplay_gore_choice_and_reset_wait_for_a_restart_and_stay_out_of_presets(bool atStartup)
    {
        var install = GameInstall.Locate();
        Assert.SkipWhen(install is null, "Kenshi install not found");
        using var session = new WorldSession(Vector3.Zero, (-100, -100, 100, 100), (_, _) => 0, systems: [new PlayerSystem()]);
        var ui = new PlayerInterface(session, (_, _) => 0, c => c.Position) { GoreEnabled = atStartup };
        var resources = OgreScriptResources.ForInstall(install, LoadOrder.FromInstall(install));
        ui.LoadUi(UiMode.Faithful, name => resources.Find(name));
        ui.ConfigureFrontend(SyntheticTown.Database(), () => [], "");
        try
        {
            var config = new UserConfig { GoreMode = atStartup };
            // Entries and reset use the panel's value callbacks; this test never draws or creates a GPU overlay.
            var values = new SettingsPanel(null!, "", []);
            int applied = 0;
            var menu = new SettingsMenu(ui, values, new InputBindings(), () => { }, new Slider("Frame limit", 0, 240, () => 60, _ => { }), config)
                { GameplayChanged = () => applied++ };
            typeof(SettingsMenu).GetField("page", PrivateInstance)!.SetValue(menu, 6);
            var rows = Assert.IsAssignableFrom<IEnumerable>(Invoke(menu, "Entries"));
            var row = Assert.Single(rows.Cast<object>(), e => (string?)e.GetType().GetProperty("Label")!.GetValue(e) == "Brutality gore (restart required)");
            var toggle = Assert.IsType<Toggle>(row.GetType().GetProperty("Toggle")!.GetValue(row));
            Assert.Contains("restarting the application", Assert.IsType<string>(row.GetType().GetProperty("Hint")!.GetValue(row)));
            Assert.True(toggle.OnOff);
            Assert.False(toggle.Remaster);
            Assert.False(toggle.Optional);
            Assert.Equal(atStartup, toggle.Get());

            toggle.Set(!atStartup);
            Assert.Equal(!atStartup, config.GoreMode);
            Assert.Equal(atStartup, ui.GoreEnabled);
            Assert.Equal(0, applied);

            menu.ShowcaseTier(0);
            menu.ShowcaseTier(2);
            Invoke(menu, "SetFaithful", true);
            Invoke(menu, "SetFaithful", false);
            Assert.Equal(!atStartup, config.GoreMode);
            Assert.Equal(atStartup, ui.GoreEnabled);
            Assert.Equal(0, applied);

            Invoke(menu, "Reset");
            Assert.False(config.GoreMode);
            Assert.False(toggle.Get());
            Assert.Equal(atStartup, ui.GoreEnabled);
        }
        finally { ui.DisposeUi(); }
    }

    static object? Invoke(SettingsMenu menu, string name, params object?[] arguments) =>
        typeof(SettingsMenu).GetMethod(name, PrivateInstance)!.Invoke(menu, arguments);
}
