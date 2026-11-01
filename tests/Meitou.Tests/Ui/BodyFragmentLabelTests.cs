using System.Numerics;
using Meitou.Engine.Input;
using Meitou.Game;
using Meitou.Simulation;
using Meitou.Simulation.Carrying;
using Meitou.Simulation.Items;
using Meitou.Simulation.Wounds;

namespace Meitou.Tests.Ui;

public sealed class BodyFragmentLabelTests
{
    static Vector2 Centre(Meitou.Data.Ui.UiRect rect) => new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);

    static void Label(TorsoFragmentInteractionTests.Scene scene, params BodyFragmentLabel[] labels)
    {
        scene.Ui.HighlightFocus = Vector3.Zero;
        scene.Ui.BodyFragmentLabels = () => labels;
        scene.Ui.LayoutBodyFragmentLabels(_ => 220);
    }

    static BodyFragmentLabel Half(TorsoFragmentInteractionTests.Scene scene, int side) =>
        new(100 + side, scene.Body, "Cut body — " + (side == 1 ? "Upper body" : "Lower body"), new(3, 1, 0), BodyFragmentLabelKind.TorsoHalf, side);

    [Theory, Slow]
    [InlineData(0)]
    [InlineData(1)]
    public void Stacked_label_click_keeps_its_half_even_when_the_mesh_reports_the_other_half(int side)
    {
        using var scene = new TorsoFragmentInteractionTests.Scene(1 - side);
        Label(scene, Half(scene, 0), Half(scene, 1));
        var rows = scene.Ui.BodyFragmentLabelsDrawn;
        Assert.Equal(2, rows.Count);
        Assert.False(rows[0].Rect.Intersect(rows[1].Rect) is { Width: > 0, Height: > 0 });
        var at = Centre(rows.Single(row => row.Label.TorsoSide == side).Rect);
        scene.Ui.MouseDown(MouseButton.Left, at, false);
        scene.Ui.MouseUp(MouseButton.Left, at, false);
        var command = Assert.IsType<PickUpOrder>(Assert.Single(scene.Commands()));
        Assert.Equal(scene.Body, command.Target);
        Assert.Equal(side, command.TorsoSide);
    }

    [Fact, Slow]
    public void Alt_label_held_menu_can_pick_up_the_free_positive_half_while_negative_is_held()
    {
        using var scene = new TorsoFragmentInteractionTests.Scene(0);
        scene.Hold(scene.First, 0);
        Label(scene, Half(scene, 1));
        var at = Centre(Assert.Single(scene.Ui.BodyFragmentLabelsDrawn).Rect);
        scene.Ui.MouseDown(MouseButton.Right, at, false, alt: true);
        Thread.Sleep(TimeSpan.FromSeconds(PlayerInterface.ContextHoldSeconds + 0.05));
        scene.Ui.Steer();
        Assert.Empty(scene.Commands());
        var row = Assert.Single(scene.Ui.UiFrame, c => c.Name.StartsWith("context-verb-", StringComparison.Ordinal) && c.Caption == "Pick Up");
        var pickAt = Centre(row.Bounds);
        scene.Ui.MouseMove(pickAt);
        scene.Ui.MouseUp(MouseButton.Right, pickAt, false);
        var command = Assert.IsType<PickUpOrder>(Assert.Single(scene.Commands()));
        Assert.Equal(scene.Body, command.Target);
        Assert.Equal(1, command.TorsoSide);
    }

    [Fact, Slow]
    public void Loose_component_label_inspects_the_owner_without_picking_up_a_torso_half()
    {
        using var scene = new TorsoFragmentInteractionTests.Scene(1);
        Label(scene, Half(scene, 1) with { Name = "Cut body — Left arm", Kind = BodyFragmentLabelKind.Detached, Component = 4 });
        var at = Centre(Assert.Single(scene.Ui.BodyFragmentLabelsDrawn).Rect);
        scene.Ui.MouseDown(MouseButton.Left, at, false);
        scene.Ui.MouseUp(MouseButton.Left, at, false);
        var command = Assert.IsType<InspectCommand>(Assert.Single(scene.Commands()));
        Assert.Equal(scene.Body, command.Target);
    }

    [Theory, Slow]
    [InlineData("key-up")]
    [InlineData("hidden-owner")]
    [InlineData("upper-floor")]
    [InlineData("occluded")]
    [InlineData("out-of-range")]
    public void Unseen_fragment_labels_do_not_leave_clickable_rectangles(string reason)
    {
        using var scene = new TorsoFragmentInteractionTests.Scene(1);
        Label(scene, Half(scene, 1));
        Assert.Single(scene.Ui.BodyFragmentLabelsDrawn);
        switch (reason)
        {
            case "key-up": scene.Ui.HighlightFocus = null; break;
            case "hidden-owner": scene.Ui.HiddenInWorld = _ => true; break;
            case "upper-floor": scene.Ui.AboveViewedFloor = _ => true; break;
            case "occluded": scene.Ui.ClosedBuildingHit = (_, _, _) => 1; break;
            case "out-of-range": scene.Ui.HighlightFocus = new(1000, 0, 0); break;
        }
        scene.Ui.LayoutBodyFragmentLabels(_ => 220);
        Assert.Empty(scene.Ui.BodyFragmentLabelsDrawn);
    }

    [Fact, Slow]
    public void Existing_severed_limb_loot_label_includes_its_owner()
    {
        using var scene = new TorsoFragmentInteractionTests.Scene(0);
        var item = new GroundItemSnapshot(1, "left-arm", "Left Arm", Vector3.Zero, Quaternion.Identity, true,
            Fall: new(scene.Body, "Bip01 L UpperArm", 0));
        Assert.Equal("Cut body — Left Arm", scene.Ui.GroundLabelName(item));
    }

    [Theory]
    [InlineData(0, 1, 0, 1, true, "Upper body")]
    [InlineData(0, 1, 0, 0, true, "Lower body")]
    [InlineData(1, 0, 0, 1, true, "Left body half")]
    [InlineData(1, 0, 0, 1, false, "Right body half")]
    [InlineData(0, 0, 1, 1, true, "Front body half")]
    [InlineData(0, 0, 1, 0, true, "Back body half")]
    [InlineData(0.7f, 0.7f, 0, 1, true, "Body fragment 2")]
    public void Fragment_names_follow_the_cut_and_the_models_actual_left_axis(float x, float y, float z, int side, bool leftPositive, string expected)
    {
        var cut = new TorsoBisectionState(new(Vector3.Normalize(new(x, y, z)), 0), FullBody: true);
        Assert.Equal(expected, BodyFragmentNames.Torso(cut, side, leftPositive));
    }
}
