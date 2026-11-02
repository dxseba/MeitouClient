using System.Numerics;
using Meitou.Content;
using Meitou.Data;
using Meitou.Data.Ogre;
using Meitou.Data.Ui;
using Meitou.Engine;
using Meitou.Engine.Input;
using Meitou.Game;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Tests.Simulation;

namespace Meitou.Tests.Ui;

public sealed class BrutalityUiTests
{
    [Theory, Slow]
    [InlineData(UiMode.Faithful, 1280, 720)]
    [InlineData(UiMode.Faithful, 1600, 900)]
    [InlineData(UiMode.Meitou, 1280, 720)]
    [InlineData(UiMode.Meitou, 1600, 900)]
    public void Custom_skill_is_selectable_reports_progress_and_updates_at_mastery(UiMode mode, int width, int height)
    {
        var install = GameInstall.Locate();
        Assert.SkipWhen(install is null, "Kenshi install not found");
        using var session = new WorldSession(Vector3.Zero, (-100, -100, 100, 100), (_, _) => 0, systems: [new PlayerSystem()]);
        var progress = BrutalityProgress.FromLevel(59.9f);
        var id = session.World.Characters.Spawn(new(), new() { Name = "Fighter", IsPlayer = true, Brutality = progress }, 0);
        session.World.Player.Selection.Add(id);
        session.World.RunTick();
        var resources = OgreScriptResources.ForInstall(install, LoadOrder.FromInstall(install));
        var ui = new PlayerInterface(session, (_, _) => 0, c => c.Position) { Width = width, Height = height };
        ui.LoadUi(mode, name => resources.Find(name));
        ui.ConfigureFrontend(SyntheticTown.Database(), () => [], "");
        try
        {
            ui.ShowUiPanel("stats");
            var row = ui.UiFrame.Single(c => c.Name == "Skills2Panel-label-brutality");
            var panel = ui.UiFrame.Single(c => c.Name == "Skills2Panel");
            Assert.Equal("Brutality", row.Caption);
            Assert.Equal("59", ui.UiFrame.Single(c => c.Name == "Skills2Panel-value-brutality").Caption);
            Assert.True(row.Bounds.Y >= panel.Bounds.Y && row.Bounds.Y + row.Bounds.Height <= panel.Bounds.Y + panel.Bounds.Height);
            var centre = new Vector2(row.Bounds.X + row.Bounds.Width / 2, row.Bounds.Y + row.Bounds.Height / 2);
            ui.MouseDown(MouseButton.Left, centre, false);
            ui.MouseUp(MouseButton.Left, centre, false);

            Assert.Equal("Brutality", ui.UiFrame.Single(c => c.Name == "stat-description-title").Caption);
            Assert.Contains("60:", ui.UiFrame.Single(c => c.Name == "brutality-unlock-cleave").Caption);
            Assert.Contains("0 %", ui.UiFrame.Single(c => c.Name == "stat-description-body").Caption);
            var track = ui.UiFrame.Single(c => c.Name == "stat-next-level-track");
            var fill = ui.UiFrame.Single(c => c.Name == "stat-next-level-fill");
            Assert.Equal(progress.Progress01, fill.Bounds.Width / track.Bounds.Width, 4);

            progress.Award(BrutalityProgress.MaximumExperience);
            session.World.RunTick();
            Assert.Equal("100", ui.UiFrame.Single(c => c.Name == "Skills2Panel-value-brutality").Caption);
            Assert.Equal("Mastered", ui.UiFrame.Single(c => c.Name == "stat-next-level-label").Caption);
            Assert.Equal("100%", ui.UiFrame.Single(c => c.Name == "stat-next-level-percent").Caption);
            Assert.Contains("95", ui.UiFrame.Single(c => c.Name == "stat-description-body").Caption);
        }
        finally { ui.DisposeUi(); }
    }
}
