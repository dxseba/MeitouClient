using System.Numerics;
using Meitou.Brutality.Effects;
using Meitou.Brutality.Rendering;
using Meitou.Data;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Data.Ogre;
using Meitou.Rendering;

namespace Meitou.Tests.Modding;

public sealed class BrutalityEffectsAnchorTests
{
    [Theory]
    [InlineData(32)]
    [InlineData(35)]
    public void Contact_samples_only_the_raw_outer_flesh_rim_not_brain_backing_or_inner_contours(int layer)
    {
        var (rig, look) = Fixture();
        Vertex V(Vector3 p, Vector2 uv) => new() { Position = p, Normal = -Vector3.UnitY,
            Tangent = new(p, layer), Uv = uv, Weights = Vector4.UnitX };
        Vertex[] rim = [V(Vector3.Zero, Vector2.Zero), V(Vector3.UnitX, Vector2.Zero), V(Vector3.UnitZ, Vector2.Zero)];
        var all = new List<Vertex>(rim);
        foreach (var uv in new[] { new Vector2(3, 0), new Vector2(4, 0), new Vector2(0, .75f) })
            all.AddRange(new[] { V(new(30, 0, 0), uv), V(new(31, 0, 0), uv), V(new(30, 0, 1), uv) });
        var cap = MeshCut.FromTriangles(all, preserveRawVertexChannels: true);
        var geometry = new RuntimeCharacterGeometry(parts: [new(new CharacterPart
        { Record = look.Race, Mesh = "test-section", Field = "wound-section", Mode = AttachMode.SharedSkeleton }, cap, true)
        { PreserveRawVertexChannels = true }]);
        var samples = new CutAnchorGeometry().Samples(geometry, new(0, 1, 0, 0), 1, rig, look);
        Assert.Equal(3, samples.Length); Assert.All(samples, vertex => Assert.Equal(layer, vertex.Tangent.W));
        var contact = Assert.IsType<CutFluidContact>(CutAnchorGeometry.Pose(samples,
            [Matrix4x4.Identity, Matrix4x4.Identity], Matrix4x4.Identity));
        Assert.True(Vector3.Distance(new(1f / 3, 0, 1f / 3), contact.Position) < .00001f);
        Assert.Equal(-Vector3.UnitY, contact.Normal);
    }

    [Fact]
    public void Blood_anchor_uses_stat_shaped_skin_and_lift_while_moving_carried_and_returning_to_native_placement()
    {
        var (rig, look) = Fixture();
        var ordinary = rig.Shape(new Dictionary<string, Vector3>(), new Dictionary<string, Vector3>(), 1);
        var muscular = rig.Shape(new Dictionary<string, Vector3> { ["spine"] = new(2, 1, 1) }, new Dictionary<string, Vector3>(), 1);
        var setup = new CharacterPoseSetup { Rig = rig, Shape = ordinary, GroundOffset = .4f };
        int reshapes = 0;
        var pose = new EffectSkinPose(rig, (_, build) => { reshapes++; Assert.Equal(1, build.Muscle); return (muscular, 2); });
        Vertex[] rim = [new() { Position = new(1, 5, 0), Normal = Vector3.UnitY, Bones = 1, Weights = Vector4.UnitX }];
        var actor = new CharacterInstance(7, look, new(10, 20, 30), 0, [])
        { Build = new(1, 0, 0), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI * .5f) };
        var blood = new LiquidBloodSimulation(new Random(8)); blood.Begin(new(7, 1), 0, Vector2.UnitX, 0x800000);
        for (int frame = 0; frame < 3; frame++)
        {
            actor = actor with { Position = new(10 + frame * .2f, 20, 30) };
            pose.Update(actor, setup); new PoseScratch(rig).Pose(pose.Shape, [], pose.Bones);
            var contact = Assert.IsType<CutFluidContact>(CutAnchorGeometry.Pose(rim, pose.Bones, pose.World));
            Assert.True(Vector3.Distance(new(7 + frame * .2f, 22, 30), contact.Position) < .0001f);
            Assert.True(Vector3.Distance(-Vector3.UnitX, contact.Normal) < .0001f);
            blood.Advance(frame * .1, (_, _) => contact, _ => -100);
            if (frame == 2) Assert.Contains(blood.Streams, stream => Vector3.Distance(stream.Head, contact.Position + contact.Normal * .06f) < .0001f);
        }
        Assert.Equal(1, reshapes);
        var physicalWorld = Matrix4x4.CreateRotationY(.3f) * Matrix4x4.CreateTranslation(4, 8, 12);
        pose.Update(actor with { World = physicalWorld }, setup); Assert.Equal(physicalWorld, pose.World);
        pose.Update(actor with { Build = null, Rotation = null, World = null }, setup);
        Assert.Same(ordinary, pose.Shape); Assert.Equal(actor.Position.Y - .4f, pose.World.M42, .0001f);
        Assert.Equal(1, reshapes);
    }

    static (SkeletonRig Rig, CharacterAppearance Look) Fixture()
    {
        var rig = new SkeletonRig(new OgreSkeleton { Bones = {
            new() { Handle = 0, Name = "root" }, new() { Handle = 1, Name = "spine", Parent = 0, Position = new(0, 5, 0) } } });
        var file = new FcsFile(); file.Records.Add(new() { StringId = "race", Name = "race", Flags = 0x10, RecordType = FcsRecordType.RACE });
        var db = new GameDatabase(); db.Apply(file, "anchor.mod");
        return (rig, new() { Race = db.Find("race")! });
    }
}
