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
using Meitou.Simulation.Combat;
using Meitou.Simulation.Items;
using Meitou.Simulation.Locks;
using Meitou.Tests.Simulation;
using Meitou.Ui;
using static Meitou.Tests.Bodies.BodyFixtures;

namespace Meitou.Tests.Ui;

public sealed class DownedAttackMenuTests
{
    sealed class Scene : IDisposable
    {
        public WorldSession Session { get; }
        public PlayerInterface Ui { get; }
        public CharacterId Attacker { get; }
        public CharacterId Target { get; }
        public Vector2 Pixel { get; }

        public Scene(string state, bool hostile = true)
        {
            var install = GameInstall.Locate();
            Assert.SkipWhen(install is null, "Kenshi install not found");
            Session = new(Vector3.Zero, (-100, -100, 100, 100), (_, _) => 0, systems: [new PlayerSystem()]);
            Attacker = Add("Player", -3, true);
            Target = Add("Target", 3, false);
            var target = Session.World.Characters.Cold(Target.Slot)!;
            switch (state)
            {
                case "ko": target.Medical!.KnockOut(1000); break;
                case "dying":
                    target.Medical!.KnockOut(1000);
                    target.Medical.Parts.Single(p => p.Template.StringId == Chest.StringId).Flesh = -40;
                    Assert.False(target.Medical.Dead);
                    break;
                case "sleep": target.InSomething = HeldState.Bed; target.InWhat = "test-bed"; break;
                case "playing-dead":
                    target.Medical!.KnockOut(0.01f);
                    target.Medical.Tick(0.1f, Context() with { PlayDead = true });
                    Assert.True(target.Medical.PlayingDead);
                    break;
                case "prone": target.Medical!.StayingLow = true; break;
                case "corpse": target.Medical!.KillFromBisection([]); break;
                case "carried":
                    target.Medical!.KnockOut(1000);
                    target.CarriedBy = Attacker;
                    Session.World.Characters.Cold(Attacker.Slot)!.Carrying = Target;
                    break;
                case "caged":
                    target.Medical!.KnockOut(1000);
                    target.InSomething = HeldState.Imprisoned;
                    target.InWhat = "test-cage";
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(state));
            }
            Session.World.Player.Faction = 0;
            Session.World.Player.Selection.Add(Attacker);
            Session.World.RunTick();
            // Hostility is a published relation result. Keep this UI test independent of town/faction generation.
            Session.CurrentSnapshot.Characters.Single(c => c.Id == Target).HostileToPlayer = hostile;
            var resources = OgreScriptResources.ForInstall(install, LoadOrder.FromInstall(install));
            Ui = new PlayerInterface(Session, (_, _) => 0, c => c.Position) { Width = 1600, Height = 900 };
            Ui.LoadUi(UiMode.Faithful, name => resources.Find(name));
            Ui.ConfigureFrontend(SyntheticTown.Database(), () => [], "");
            Ui.Eye = new Vector3(0, 6, -14);
            Ui.ViewProjection = Matrix4x4.CreateLookAt(Ui.Eye, new(0, 1, 0), Vector3.UnitY)
                * Matrix4x4.CreatePerspectiveFieldOfView(1, 1600 / 900f, 0.1f, 1000);
            Ui.MeshHit = (id, _, _) => id == Target ? 10 : float.PositiveInfinity;
            Pixel = Ui.Project(new Vector3(3, 1, 0))!.Value;
        }

        CharacterId Add(string name, float x, bool player) => Session.World.Characters.Spawn(new() { Position = new(x, 0, 0), Health = 100 },
            new() { Name = name, IsPlayer = player, Faction = player ? 0 : 1, Race = Human, Stats = new CharacterStats(), Medical = MedicalState.Create(Human, 50), Inventory = new Inventory() }, 0);

        public UiControl[] Menu()
        {
            Ui.MouseDown(MouseButton.Right, Pixel, false);
            Thread.Sleep(TimeSpan.FromSeconds(PlayerInterface.ContextHoldSeconds + 0.05));
            Ui.Steer();
            Assert.True(Ui.ContextMenuOpen);
            return Ui.UiFrame.Where(c => c.Name.StartsWith("context-verb-", StringComparison.Ordinal)).ToArray();
        }

        public static Vector2 Centre(UiControl row) => new(row.Bounds.X + row.Bounds.Width / 2, row.Bounds.Y + row.Bounds.Height / 2);

        public UiControl[] ChooseParent(UiControl row)
        {
            var at = Centre(row);
            Ui.MouseMove(at);
            Ui.MouseUp(MouseButton.Right, at, false);
            Assert.True(Ui.ContextMenuOpen);
            Assert.Equal(0, Session.World.Commands.Count);
            return Ui.UiFrame.Where(c => c.Name.StartsWith("context-verb-", StringComparison.Ordinal)).ToArray();
        }

        public AttackOrder Attack(UiControl row, string? part = null)
        {
            if (part is not null)
            {
                var bodyRow = Assert.Single(ChooseParent(row), c => c.Caption == part);
                var at = Centre(bodyRow);
                Ui.MouseDown(MouseButton.Left, at, false);
                Ui.MouseUp(MouseButton.Left, at, false);
            }
            else
            {
                var at = Centre(row);
                Ui.MouseMove(at);
                Ui.MouseUp(MouseButton.Right, at, false);
            }
            Assert.False(Ui.ContextMenuOpen);
            return Assert.IsType<AttackOrder>(Assert.Single(Session.World.Commands.TakeDue(long.MaxValue)));
        }

        public void Dispose() { Ui.DisposeUi(); Session.Dispose(); }
    }

    [Theory, Slow]
    [InlineData("ko")]
    [InlineData("sleep")]
    [InlineData("playing-dead")]
    [InlineData("dying")]
    public void Downed_living_enemy_menu_chooses_an_exact_health_part_after_right_release(string state)
    {
        using var scene = new Scene(state);
        var attackRow = Assert.Single(scene.Menu(), c => c.Caption == "Attack body part");
        Assert.Equal(0, scene.Session.World.Commands.Count);
        var attack = scene.Attack(attackRow, "Chest");
        Assert.Equal(scene.Target, attack.Target);
        Assert.Equal([scene.Attacker], attack.Attackers);
        Assert.True(attack.Picked);
        Assert.True(attack.AllowIncapacitated);
        Assert.Equal(Chest.StringId, attack.TargetBodyPart);
    }

    [Fact, Slow]
    public void Conscious_prone_enemy_retains_its_single_ordinary_attack_order()
    {
        using var scene = new Scene("prone");
        var attackRow = Assert.Single(scene.Menu(), c => c.Caption == "Attack Target");
        var attack = scene.Attack(attackRow);
        Assert.Equal(scene.Target, attack.Target);
        Assert.True(attack.Picked);
        Assert.False(attack.AllowIncapacitated);
    }

    [Theory, Slow]
    [InlineData("corpse", true)]
    [InlineData("ko", false)]
    [InlineData("carried", true)]
    [InlineData("caged", true)]
    public void Corpse_friendly_carried_and_caged_targets_do_not_offer_the_downed_attack_order(string state, bool hostile)
    {
        using var scene = new Scene(state, hostile);
        Assert.DoesNotContain(scene.Menu(), c => c.Caption is "Attack body part" or "Attack Target");
        Assert.Equal(0, scene.Session.World.Commands.Count);
    }

    [Fact, Slow]
    public void Body_part_menu_uses_present_anatomy_and_excludes_a_severed_arm()
    {
        using var scene = new Scene("ko");
        var target = scene.Session.CurrentSnapshot.Characters.Single(c => c.Id == scene.Target);
        target.HealthParts = target.HealthParts.Select(p => p.Id == LeftArm.StringId ? p with { Limb = LimbState.Stump } : p).ToArray();
        var rows = scene.ChooseParent(Assert.Single(scene.Menu(), c => c.Caption == "Attack body part"));
        Assert.Equal(6, rows.Length);
        Assert.DoesNotContain(rows, c => c.Caption == "Left Arm");
        Assert.Contains(rows, c => c.Caption == "Right Arm");
        var at = Scene.Centre(Assert.Single(rows, c => c.Caption == "Head"));
        scene.Ui.MouseDown(MouseButton.Left, at, false);
        scene.Ui.MouseUp(MouseButton.Left, at, false);
        var attack = Assert.IsType<AttackOrder>(Assert.Single(scene.Session.World.Commands.TakeDue(long.MaxValue)));
        Assert.Equal(Head.StringId, attack.TargetBodyPart);
    }

    [Theory, Slow]
    [InlineData(true)]
    [InlineData(false)]
    public void Body_part_menu_cancels_without_an_order(bool escape)
    {
        using var scene = new Scene("ko");
        scene.ChooseParent(Assert.Single(scene.Menu(), c => c.Caption == "Attack body part"));
        if (escape) Assert.True(scene.Ui.UiKey(Key.Escape));
        else
        {
            scene.Ui.MouseDown(MouseButton.Left, Vector2.One, false);
            scene.Ui.MouseUp(MouseButton.Left, Vector2.One, false);
        }
        Assert.False(scene.Ui.ContextMenuOpen);
        Assert.Equal(0, scene.Session.World.Commands.Count);
    }

    [Fact, Slow]
    public void Target_that_died_before_body_part_confirmation_does_not_create_a_corpse_attack()
    {
        using var scene = new Scene("ko");
        var rows = scene.ChooseParent(Assert.Single(scene.Menu(), c => c.Caption == "Attack body part"));
        var target = scene.Session.CurrentSnapshot.Characters.Single(c => c.Id == scene.Target);
        target.Body = target.Body!.Value with { Dead = true };
        var at = Scene.Centre(Assert.Single(rows, c => c.Caption == "Chest"));
        scene.Ui.MouseDown(MouseButton.Left, at, false);
        scene.Ui.MouseUp(MouseButton.Left, at, false);
        Assert.False(scene.Ui.ContextMenuOpen);
        Assert.Equal(0, scene.Session.World.Commands.Count);
    }
}
