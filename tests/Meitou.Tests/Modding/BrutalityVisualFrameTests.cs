using System.Numerics;
using Meitou.Brutality;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Tests.Combat;
using static Meitou.Tests.Bodies.BodyFixtures;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Modding;

public sealed class BrutalityVisualFrameTests
{
    static BrutalityDurableState Empty => new("visual/target", 0, new(0), null, null, []);
    static BrutalityDurableState Visual(int kind) => kind switch
    {
        0 => Empty with { SevereHead = new(4, SevereHeadWoundKind.JawLost) },
        1 => Empty with { SevereHead = new(4, SevereHeadWoundKind.Scalp) },
        2 => Empty with { SevereHead = new(4, SevereHeadWoundKind.BrokenSkull) },
        3 => Empty with { Head = new(Vector4.UnitX, HeadCutMode.AttachedSplit) },
        4 => Empty with { Torso = new(new(0, 1, 0, -10)) },
        5 => Empty with { Limbs = [new(0, .4f, Vector2.Zero)] },
        _ => Empty with { Wounds = BodyWoundState.Empty with { BellyHits = 1 } },
    };

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void Cuts_severe_wounds_and_bowel_state_publish_without_ordinary_marks_and_clear_without_stale_payloads(int kind)
    {
        var system = new BrutalityGameplaySystem(null, BaseGame, BodyOptions.Default, (RngPurpose)0xB17A1171);
        using var world = new SimWorld(new WorldSettings { PublishSnapshots = false }, new OpenGroundWalkability((_, _) => 0), [system]);
        var target = world.Characters.Spawn(new(), new Combatant().MakeCold("target"), 0);
        var visual = Visual(kind); Assert.True(BrutalityStateCodec.Valid(visual));
        Assert.Equal(1, system.ImportDurable(world, [visual], _ => target)); system.SlowWorld(world);
        Assert.Null(system.WoundFrameAt(0));
        var first = Assert.IsType<BodyWoundFrame>(system.WoundFrameAt(1));
        var shown = Assert.Single(first.Characters); Assert.Equal(target, shown.Key); Assert.False(shown.Value.Wounds.HasAny);
        Assert.Equal(visual.SevereHead, shown.Value.SevereHead); Assert.Equal(visual.Head, shown.Value.Head);
        Assert.Equal(visual.Torso, shown.Value.Torso); Assert.Equal(visual.Limbs, shown.Value.Limbs); Assert.Equal(visual.Wounds.BellyHits, shown.Value.Wounds.BellyHits);
        world.RunTick();
        Assert.Equal(1, system.ImportDurable(world, [Empty], _ => target)); system.SlowWorld(world);
        Assert.Empty(Assert.IsType<BodyWoundFrame>(system.WoundFrameAt(2)).Characters);
        Assert.Single(first.Characters); // Published render snapshots remain immutable across removal/clearing.
        Assert.Same(first, system.WoundFrameAt(1));
    }

    [Fact]
    public void Removing_a_cut_actor_publishes_an_empty_frame_and_progress_alone_creates_no_visual_payload()
    {
        var system = new BrutalityGameplaySystem(null, BaseGame, BodyOptions.Default, (RngPurpose)0xB17A1171);
        using var world = new SimWorld(new WorldSettings { PublishSnapshots = false }, new OpenGroundWalkability((_, _) => 0), [system]);
        var target = world.Characters.Spawn(new(), new Combatant().MakeCold("target"), 0);
        Assert.Equal(1, system.ImportDurable(world, [Empty with { Experience = 123, SevereHead = new(3) }], _ => target)); system.SlowWorld(world);
        Assert.Null(system.WoundFrameAt(1));
        Assert.Equal(1, system.ImportDurable(world, [Visual(4)], _ => target)); system.SlowWorld(world);
        var cutFrame = Assert.IsType<BodyWoundFrame>(system.WoundFrameAt(1)); Assert.Single(cutFrame.Characters);
        world.RunTick(); Assert.True(world.Characters.Remove(target)); system.SlowWorld(world);
        Assert.Empty(Assert.IsType<BodyWoundFrame>(system.WoundFrameAt(2)).Characters);
        Assert.Single(cutFrame.Characters);
    }
}
