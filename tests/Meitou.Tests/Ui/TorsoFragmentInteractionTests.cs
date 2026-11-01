using System.Numerics;
using Meitou.Content;
using Meitou.Data;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Ogre;
using Meitou.Data.Ui;
using Meitou.Engine;
using Meitou.Engine.Input;
using Meitou.Game;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Carrying;
using Meitou.Simulation.Items;
using Meitou.Tests.Simulation;
using static Meitou.Tests.Bodies.BodyFixtures;

namespace Meitou.Tests.Ui;

/// <summary>The actual right-click and held-menu routes retain which independently rendered half was clicked.</summary>
public sealed class TorsoFragmentInteractionTests
{
    internal sealed class Scene : IDisposable
    {
        public WorldSession Session { get; }
        public PlayerInterface Ui { get; }
        public CharacterId First { get; }
        public CharacterId Second { get; }
        public CharacterId Body { get; }
        public int HitSide;
        public Vector2 Pixel { get; }

        public Scene(int side)
        {
            var install = GameInstall.Locate();
            Assert.SkipWhen(install is null, "Kenshi install not found");
            Session = new WorldSession(Vector3.Zero, (-100, -100, 100, 100), (_, _) => 0, systems: [new PlayerSystem()]);
            HitSide = side;
            First = Add("First", -5, true);
            Second = Add("Second", -3, true);
            Body = Add("Cut body", 3, false);
            var cold = Session.World.Characters.Cold(Body.Slot)!;
            cold.TorsoBisection = new(new(0, 1, 0, -12));
            cold.TorsoPositivePosition = new(8, 0, 0);
            cold.Medical!.KillFromBisection([]);
            Session.World.Player.Faction = 0;
            Select(First, Second);
            var resources = OgreScriptResources.ForInstall(install, LoadOrder.FromInstall(install));
            Ui = new PlayerInterface(Session, (_, _) => 0, c => c.Position) { Width = 1600, Height = 900 };
            Ui.LoadUi(UiMode.Faithful, name => resources.Find(name));
            Ui.ConfigureFrontend(SyntheticTown.Database(), () => [], "");
            Ui.Eye = new Vector3(0, 6, -14);
            Ui.ViewProjection = Matrix4x4.CreateLookAt(Ui.Eye, new(0, 1, 0), Vector3.UnitY)
                * Matrix4x4.CreatePerspectiveFieldOfView(1, 1600 / 900f, 0.1f, 1000);
            // Mesh/plane intersection belongs to the renderer; this scene controls its published answer while exercising the real UI event path.
            Ui.MeshHit = (id, _, _) => id == Body ? 10 : float.PositiveInfinity;
            Ui.TorsoSideHit = (_, _, _) => HitSide;
            Pixel = Ui.Project(new Vector3(3, 1, 0))!.Value;
        }

        CharacterId Add(string name, float x, bool player)
        {
            var cold = new CharacterCold
            {
                Name = name, IsPlayer = player, Faction = player ? 0 : 1,
                Race = Human, Stats = new CharacterStats(), Medical = MedicalState.Create(Human, 50), Inventory = new Inventory(),
            };
            return Session.World.Characters.Spawn(new() { Position = new(x, 0, 0), Health = 100 }, cold, 0);
        }

        public void Select(params CharacterId[] selected)
        {
            Session.World.Player.Selection.Clear();
            foreach (var id in selected) Session.World.Player.Selection.Add(id);
            Session.World.RunTick();
        }

        public void Hold(CharacterId carrier, int side)
        {
            var table = Session.World.Characters;
            table.Cold(carrier.Slot)!.Carrying = Body;
            table.Cold(carrier.Slot)!.CarryingTorsoSide = side;
            if (side == 1) table.Cold(Body.Slot)!.TorsoPositiveCarriedBy = carrier;
            else table.Cold(Body.Slot)!.CarriedBy = carrier;
            Session.World.RunTick();
        }

        public IReadOnlyList<SimCommand> Commands() => Session.World.Commands.TakeDue(long.MaxValue);
        public void Dispose() { Ui.DisposeUi(); Session.Dispose(); }
    }

    [Theory, Slow]
    [InlineData(0)]
    [InlineData(1)]
    public void Quick_right_click_picks_the_rendered_half_instead_of_looting_the_shared_corpse(int side)
    {
        using var scene = new Scene(side);
        scene.Ui.MouseDown(MouseButton.Right, scene.Pixel, false);
        scene.Ui.MouseUp(MouseButton.Right, scene.Pixel, false);
        var command = Assert.IsType<PickUpOrder>(Assert.Single(scene.Commands()));
        Assert.Equal(scene.Body, command.Target);
        Assert.Equal(side, command.TorsoSide);
    }

    [Theory, Slow]
    [InlineData(0)]
    [InlineData(1)]
    public void Ctrl_click_on_the_free_half_does_not_put_down_the_selected_opposite_holder(int side)
    {
        using var scene = new Scene(side);
        scene.Hold(scene.First, 1 - side);
        scene.Ui.MouseDown(MouseButton.Right, scene.Pixel, false, ctrl: true);
        var command = Assert.IsType<PickUpOrder>(Assert.Single(scene.Commands()));
        Assert.Equal(scene.Body, command.Target);
        Assert.Equal(side, command.TorsoSide);
    }

    [Theory, Slow]
    [InlineData(0)]
    [InlineData(1)]
    public void Ctrl_click_puts_down_only_the_holder_of_the_clicked_half(int side)
    {
        using var scene = new Scene(side);
        scene.Hold(scene.First, 0);
        scene.Hold(scene.Second, 1);
        scene.Ui.MouseDown(MouseButton.Right, scene.Pixel, false, ctrl: true);
        var command = Assert.IsType<PutDownOrder>(Assert.Single(scene.Commands()));
        Assert.Equal([side == 1 ? scene.Second : scene.First], command.Carriers);
    }

    [Theory, Slow]
    [InlineData(0)]
    [InlineData(1)]
    public void Alt_right_click_routes_take_all_to_the_clicked_half(int side)
    {
        using var scene = new Scene(side);
        scene.Ui.MouseDown(MouseButton.Right, scene.Pixel, false, alt: true);
        var command = Assert.IsType<LootOrder>(Assert.Single(scene.Commands()));
        Assert.Equal(scene.Body, command.Target);
        Assert.Equal(side, command.TorsoSide);
    }

    [Theory, Slow]
    [InlineData(0)]
    [InlineData(1)]
    public void Held_inventory_menu_routes_opening_to_the_clicked_half(int side)
    {
        using var scene = new Scene(side);
        scene.Ui.MouseDown(MouseButton.Right, scene.Pixel, false);
        Thread.Sleep(TimeSpan.FromSeconds(PlayerInterface.ContextHoldSeconds + 0.05));
        scene.Ui.Steer();
        Assert.True(scene.Ui.ContextMenuOpen);
        var row = Assert.Single(scene.Ui.UiFrame, c => c.Name.StartsWith("context-verb-", StringComparison.Ordinal) && c.Caption == "Loot");
        var at = new Vector2(row.Bounds.X + row.Bounds.Width / 2, row.Bounds.Y + row.Bounds.Height / 2);
        scene.Ui.MouseMove(at);
        scene.Ui.MouseUp(MouseButton.Right, at, false);
        var command = Assert.IsType<OpenLootOrder>(Assert.Single(scene.Commands()));
        Assert.Equal(scene.Body, command.Target);
        Assert.Equal(side, command.TorsoSide);
    }
    [Theory, Slow]
    [InlineData(0)]
    [InlineData(1)]
    public void Held_menu_keeps_its_clicked_half_and_never_offers_whole_person_fixtures(int side)
    {
        using var scene = new Scene(side);
        scene.Hold(scene.First, 1 - side);
        scene.Ui.MouseDown(MouseButton.Right, scene.Pixel, false);
        scene.HitSide = 1 - side; // Moving over the other half after the press must not change the pending menu's target.
        Thread.Sleep(TimeSpan.FromSeconds(PlayerInterface.ContextHoldSeconds + 0.05));
        scene.Ui.Steer();
        Assert.True(scene.Ui.ContextMenuOpen);
        var rows = scene.Ui.UiFrame.Where(c => c.Name.StartsWith("context-verb-", StringComparison.Ordinal)).ToArray();
        var pick = Assert.Single(rows, c => c.Caption == "Pick Up");
        Assert.DoesNotContain(rows, c => c.Caption is "Put Down" or "Put in Bed" or "Put in Cage");
        Assert.Empty(scene.Commands());
        var at = new Vector2(pick.Bounds.X + pick.Bounds.Width / 2, pick.Bounds.Y + pick.Bounds.Height / 2);
        scene.Ui.MouseMove(at);
        scene.Ui.MouseUp(MouseButton.Right, at, false);
        var command = Assert.IsType<PickUpOrder>(Assert.Single(scene.Commands()));
        Assert.Equal(scene.Body, command.Target);
        Assert.Equal(side, command.TorsoSide);
    }
}
