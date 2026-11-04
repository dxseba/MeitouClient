using System.Numerics;
using Meitou.Brutality.Effects;
using Meitou.Brutality.Rendering;
using Meitou.Data;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Data.Ogre;
using Meitou.Rendering;
using Meitou.Simulation;

namespace Meitou.Tests.Modding;

public sealed class BrutalityEffectsTests
{
    [Fact]
    public void Liquid_begins_once_at_both_current_cut_faces_keeps_race_colour_and_does_not_move_while_paused()
    {
        var blood = new LiquidBloodSimulation(new Random(7));
        CharacterId owner = new(4, 2);
        Assert.True(blood.Begin(owner, 0, Vector2.UnitX, 0x407050));
        Assert.False(blood.Begin(owner, 0, Vector2.UnitX, 0x800000));
        CutFluidContact? Contact(CharacterId id, int half) => new(new(8 + half, 20, 6), half == 0 ? -Vector3.UnitY : Vector3.UnitY);
        blood.Advance(0, Contact, _ => 0); blood.Advance(.1, Contact, _ => 0); blood.Advance(.15, Contact, _ => 0);
        Assert.NotEmpty(blood.Streams);
        Assert.Contains(blood.Streams, stream => Vector3.Distance(stream.Head, new(8, 19.94f, 6)) < .0001f);
        Assert.Contains(blood.Streams, stream => Vector3.Distance(stream.Head, new(9, 20.06f, 6)) < .0001f);
        Assert.All(blood.Streams, stream => Assert.True(Vector3.Distance(new(stream.Colour.X, stream.Colour.Y, stream.Colour.Z), new Vector3(64, 112, 80) / 255) < .00001f));
        var streams = blood.Streams.ToArray(); var drops = blood.Drops.ToArray();
        blood.Advance(.15, Contact, _ => 0);
        Assert.Equal(streams, blood.Streams); Assert.Equal(drops, blood.Drops);
        for (int i = 1; i <= 25; i++) blood.Advance(.15 + i * .2, Contact, _ => 0);
        Assert.Equal(0, blood.SourceCount); Assert.Empty(blood.Streams);
    }

    [Fact]
    public void Many_fluid_sources_and_a_slow_frame_remain_bounded_and_never_draw_a_tether_to_an_old_wound_point()
    {
        var blood = new LiquidBloodSimulation(new Random(5));
        for (int i = 0; i < 100; i++) blood.Begin(new(i + 1, 0), 0, Vector2.UnitX, 0x800000);
        Assert.Equal(LiquidBloodSimulation.SourceLimit, blood.SourceCount);
        float moving = 0;
        CutFluidContact? Contact(CharacterId owner, int half) => new(new(moving + owner.Slot, 50, half), Vector3.UnitX);
        blood.Advance(0, Contact, _ => 0); blood.Advance(.1, Contact, _ => 0); blood.Advance(.2, Contact, _ => 0);
        moving = 40; blood.Advance(.7, Contact, _ => 0);
        Assert.InRange(blood.Drops.Count, 0, LiquidBloodSimulation.DropLimit);
        Assert.InRange(blood.Streams.Count, 0, LiquidBloodSimulation.StreamSegmentLimit);
        Assert.InRange(blood.Impacts.Count, 0, LiquidBloodSimulation.ImpactLimit);
        Assert.All(blood.Streams, stream => Assert.InRange(Vector3.Distance(stream.Head, stream.Tail), .005f, 3.0001f));
    }

    [Fact]
    public void Joined_liquid_segments_share_the_same_endpoint_tangent_width_and_have_no_internal_end_cap()
    {
        LiquidBloodStream[] streams = [new(new(1, 4, 0), new(2, 3, 0), .3f, new(.5f, 0, 0, .9f), .25f),
            new(new(2, 3, 0), new(3, 1.5f, .2f), .25f, new(.5f, 0, 0, .9f), .15f)];
        var first = LiquidBloodStripGeometry.Pack(streams, 0, Vector3.Zero);
        var second = LiquidBloodStripGeometry.Pack(streams, 1, Vector3.Zero);
        Assert.Equal(new Vector2(first.Pad0, first.Pad1), new Vector2(second.Height, second.Depth));
        Assert.Equal(first.Width, second.Rotation);
        Assert.True(first.Rotation < 0 && first.Width > 0 && second.Rotation > 0 && second.Width < 0);
        Assert.All(streams, stream => Assert.True(LiquidBloodStripGeometry.Valid(stream)));
        Assert.False(LiquidBloodStripGeometry.Valid(streams[0] with { Head = new(float.NaN, 0, 0) }));
    }

    [Fact]
    public void A_dead_robot_keeps_its_short_circuit_for_twenty_to_thirty_game_seconds_and_smoke_follows_the_moving_fragment()
    {
        var scene = new GoreEffectsSimulation(new Random(1));
        var cut = Cut(new(1, 2), robot: true);
        Assert.True(scene.Begin(cut, 0)); Assert.False(scene.Begin(cut, 0));
        var source = Assert.Single(scene.RobotSources);
        Assert.InRange(source.Expires, 20, 30);
        Vector3 moving = new(2, 5, 3);
        CutFluidContact? Contact(GoreEffectCut _, int half) => new(moving + new Vector3(half, 0, 0), Vector3.UnitY);
        scene.Advance(.1, (_, _) => null, Contact, _ => true, _ => 0);
        var first = Assert.Single(scene.Explosions);
        moving += new Vector3(12, -2, 5);
        scene.Advance(.2, (_, _) => null, Contact, _ => true, _ => 0);
        var after = Assert.Single(scene.Explosions);
        Assert.Equal(first.Position, after.Position);
        Assert.Equal(moving + Vector3.UnitX, after.SmokePosition);
        // The owner validity callback includes medically dead entities; time/slot removal alone ends this effect.
        scene.Advance(source.Expires - .01, (_, _) => null, Contact, _ => true, _ => 0);
        Assert.NotEmpty(scene.Anchors);
        scene.Advance(source.Expires + .001, (_, _) => null, Contact, _ => true, _ => 0);
        Assert.Empty(scene.RobotSources); Assert.Empty(scene.Anchors); Assert.Empty(scene.Explosions);
    }

    [Fact]
    public void An_ordinary_robot_slash_short_circuits_for_twenty_to_thirty_seconds_without_a_break_explosion()
    {
        var scene = new GoreEffectsSimulation(new Random(3));
        var hit = Cut(new(21, 3), robot: true) with { Kind = 3, StableKey = -1, Cut = 30, Rupture = false };
        Assert.True(scene.Begin(hit, 2)); Assert.False(scene.Begin(hit, 2));
        var source = Assert.Single(scene.RobotSources); Assert.InRange(source.Expires - source.Born, 20, 30);
        CutFluidContact? Contact(GoreEffectCut _, int half) => half == 0 ? new(new(4, 9, 2), Vector3.UnitZ) : null;
        scene.Advance(2.1, (_, _) => null, Contact, _ => true, _ => 0);
        Assert.Single(scene.Anchors); Assert.Empty(scene.Explosions); Assert.Equal(0, scene.ExplosionCount);
        Assert.Equal(0, scene.ExplosionHistoryCount); Assert.Equal(0, scene.Blood.SourceCount);
        scene.Advance(source.Expires - .001, (_, _) => null, Contact, _ => true, _ => 0); Assert.Single(scene.Anchors);
        scene.Advance(source.Expires + .001, (_, _) => null, Contact, _ => true, _ => 0); Assert.Empty(scene.Anchors);
    }

    [Fact]
    public void A_blunt_actual_robot_break_explodes_once_without_creating_a_twenty_second_electrical_discharge()
    {
        var scene = new GoreEffectsSimulation(new Random(4));
        var hit = Cut(new(22, 3), robot: true) with { Kind = 1, StableKey = -1, Cut = 0, Rupture = true, Discharge = false };
        Assert.True(scene.Begin(hit, 0)); scene.Begin(hit, 0);
        Assert.Empty(scene.RobotSources); Assert.Equal(1, scene.ExplosionCount); Assert.Equal(1, scene.ExplosionHistoryCount);
        CutFluidContact? Contact(GoreEffectCut _, int half) => half == 0 ? new(new(6, 8, 1), Vector3.UnitY) : null;
        scene.Advance(.1, (_, _) => null, Contact, _ => true, _ => 0);
        Assert.Single(scene.Explosions); Assert.Empty(scene.Anchors); Assert.Equal(0, scene.Blood.SourceCount);
        scene.Advance(RobotBreakExplosionGeometry.Lifetime + .001, (_, _) => null, Contact, _ => true, _ => 0);
        Assert.Empty(scene.Explosions); Assert.Empty(scene.Anchors); Assert.Empty(scene.RobotSources);
    }

    [Fact]
    public void A_robot_crowd_cannot_exceed_source_explosion_smoke_or_arc_budgets_and_a_paused_picture_is_identical()
    {
        var scene = new GoreEffectsSimulation(new Random(2));
        for (int i = 0; i < 500; i++) scene.Begin(Cut(new(i + 1, 3), robot: true), 0);
        Assert.Equal(GoreEffectsSimulation.RobotCapacity, scene.RobotSources.Count);
        Assert.Equal(GoreEffectsSimulation.ExplosionCapacity, scene.ExplosionCount);
        Assert.Equal(GoreEffectsSimulation.ExplosionHistory, scene.ExplosionHistoryCount);
        CutFluidContact? Contact(GoreEffectCut cut, int half) => new(new(cut.Owner.Slot, 10, half), Vector3.UnitY);
        scene.Advance(.2, (_, _) => null, Contact, _ => true, _ => 0);
        Assert.InRange(scene.Anchors.Count, 1, RobotDischargeGeometry.AnchorLimit);
        var anchors = scene.Anchors.ToArray();
        var strokes = new RobotDischargeStroke[2048];
        int count = RobotDischargeGeometry.Build(anchors, strokes);
        Assert.InRange(count, 1, RobotDischargeGeometry.QuadLimit);
        var expected = strokes.AsSpan(0, count).ToArray();
        scene.Advance(.2, (_, _) => null, Contact, _ => true, _ => 0);
        Assert.Equal(anchors, scene.Anchors);
        Assert.Equal(count, RobotDischargeGeometry.Build(scene.Anchors.ToArray(), strokes));
        Assert.Equal(expected, strokes.AsSpan(0, count).ToArray());
        Assert.InRange(RobotDischargeSmokeGeometry.Build(anchors, new RobotSmokePuff[256]), 1, RobotDischargeSmokeGeometry.PuffLimit);
        Assert.InRange(RobotBreakExplosionGeometry.Build(scene.Explosions.ToArray(), new RobotExplosionQuad[512]), 1, RobotBreakExplosionGeometry.QuadLimit);
    }

    [Fact]
    public void Cut_contacts_follow_current_bones_and_carried_rotation_and_the_geometry_cache_is_bounded()
    {
        Vertex[] rim = [new() { Position = new(1, 2, 0), Normal = Vector3.UnitY, Bones = 1, Weights = Vector4.UnitX }];
        Matrix4x4[] skin = [Matrix4x4.Identity, Matrix4x4.CreateTranslation(2, 4, 3)];
        var world = Matrix4x4.CreateRotationZ(MathF.PI * .5f) * Matrix4x4.CreateTranslation(10, 8, 6);
        var first = Assert.IsType<CutFluidContact>(CutAnchorGeometry.Pose(rim, skin, world));
        Assert.True(Vector3.Distance(Vector3.Transform(new Vector3(3, 6, 3), world), first.Position) < .0001f);
        Assert.True(Vector3.Distance(-Vector3.UnitX, first.Normal) < .0001f);
        skin[1] = Matrix4x4.CreateTranslation(8, 1, 2);
        var after = Assert.IsType<CutFluidContact>(CutAnchorGeometry.Pose(rim, skin, world));
        Assert.True(Vector3.Distance(Vector3.Transform(new Vector3(9, 3, 2), world), after.Position) < .0001f);

        var rig = new SkeletonRig(new OgreSkeleton { Bones = { new() { Handle = 0, Name = "Bip01" } } });
        var file = new FcsFile(); file.Records.Add(new() { StringId = "race", Name = "race", Flags = 0x10, RecordType = FcsRecordType.RACE });
        var db = new GameDatabase(); db.Apply(file, "test.mod");
        var look = new CharacterAppearance { Race = db.Find("race")! };
        var cache = new CutAnchorGeometry(); RuntimeCharacterGeometry? latest = null;
        for (int i = 0; i < CutAnchorGeometry.CacheLimit + 20; i++)
        {
            latest = new(MeshCut.FromTriangles([new() { Position = new(0, 0, 0), Normal = -Vector3.UnitY, Weights = Vector4.UnitX },
                new() { Position = new(1, 0, 0), Normal = -Vector3.UnitY, Weights = Vector4.UnitX }, new() { Position = new(0, 0, 1), Normal = -Vector3.UnitY, Weights = Vector4.UnitX }]));
            Assert.InRange(cache.Samples(latest, new(0, 1, 0, 0), 1, rig, look).Length, 1, CutAnchorGeometry.SampleLimit);
        }
        Assert.Equal(CutAnchorGeometry.CacheLimit, cache.CacheCount);
        Assert.Same(cache.Samples(latest!, new(0, 1, 0, 0), 1, rig, look), cache.Samples(latest!, new(0, 1, 0, 0), 1, rig, look));
    }

    [Fact]
    public void Mechanical_scrap_is_emitted_once_falls_and_rests_on_the_floor_and_stays_bounded_in_a_crowd()
    {
        var scrap = new RobotScrapSimulation();
        RobotScrapMesh[] meshes = [new("metal.mesh", new(1, .2f, .7f))];
        var cut = Cut(new(1, 0), robot: true);
        Assert.True(scrap.Begin(cut, new(0, 10, 0), 0, 0, 91, meshes));
        Assert.InRange(scrap.Count, 3, 7);
        Assert.False(scrap.Begin(cut, new(0, 10, 0), 0, 0, 91, meshes));
        scrap.Advance(0, 40);
        for (int i = 1; i <= 50; i++) scrap.Advance(i * .1, 40);
        Assert.All(scrap.Drawn, part => Assert.InRange(part.Position.Y, 0, .5f));
        var held = scrap.Drawn.ToArray(); scrap.Advance(5, 40); Assert.Equal(held, scrap.Drawn);
        for (int i = 0; i < 150; i++) scrap.Begin(Cut(new(i + 10, 0), robot: true), new(0, 10, 0), 0, 5, 91, meshes);
        Assert.Equal(RobotScrapSimulation.PieceLimit, scrap.Count);
        scrap.Advance(46, 40); Assert.Equal(0, scrap.Count);
    }

    static GoreEffectCut Cut(CharacterId id, bool robot) => new(id, 3, 2, 1, 0, Vector2.UnitX, 80, 0x800000,
        (ulong)(id.Slot + 7919), robot, new(0, 12, 0)) { ExplosionSeed = (uint)(id.Slot + 123) };
}
