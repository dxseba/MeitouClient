using System.Numerics;
using Meitou.Brutality;
using Meitou.Brutality.Rendering;
using Meitou.Brutality.Rendering.Surface;
using Meitou.Data.Characters;
using Meitou.Data.Ogre;
using Meitou.Rendering;

namespace Meitou.Tests.Modding;

public sealed partial class BrutalityGeometryTests
{
    [Fact]
    public void Absolute_head_bounds_follow_the_same_mutated_pose_without_bind_height_or_ghost_body_bones()
    {
        using var scene = new Scene();
        var mesh = MeshCut.FromTriangles(Box(new(-1, 17, -.8f), new(1, 19.5f, .8f), 3));
        // A fitted full-body cavity is shader-hidden on heads and must not turn into their culling support.
        var cavity = new RuntimeCharacterPart(new() { Record = scene.Appearance.Race, Mesh = "cavity", Field = "anatomy", Mode = AttachMode.SharedSkeleton }, scene.Body);
        var geometry = new RuntimeCharacterGeometry(mesh, [cavity]);
        var pose = PhysicalPose();
        pose.Position[3] = new(2, 1.3f, -3);
        var world = Matrix4x4.CreateTranslation(100, 500, -200);
        var bounds = Assert.IsType<CharacterWorldBounds>(FragmentWorldBounds.For(geometry, scene.Appearance, Rig, scene.Body, pose, world, _ => null));
        Assert.InRange(bounds.Centre.Y, 501.29f, 501.31f);
        Assert.InRange(bounds.Radius, 1, 5);
        ContainsSkinned(bounds, mesh, pose, world);

        // RenderBridge.Mixed reuses this object; a pose-reference cache would leave this sphere behind.
        pose.Position[3] += new Vector3(15, -4, 7);
        pose.Rotation[3] = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, .6f);
        var moved = Assert.IsType<CharacterWorldBounds>(FragmentWorldBounds.For(geometry, scene.Appearance, Rig, scene.Body, pose, world, _ => null));
        Assert.True(Vector3.Distance(moved.Centre - bounds.Centre, new(15, -4, 7)) < .0001f);
        ContainsSkinned(moved, mesh, pose, world);
    }

    [Fact]
    public void Absolute_torso_bounds_cover_rotated_skin_and_the_actual_rigid_weapon_attachment()
    {
        using var scene = new Scene();
        var torso = MeshCut.FromTriangles(Box(new(-1.3f, 11, -1), new(1.3f, 15, 1), 1));
        var sword = MeshCut.FromTriangles(Box(new(-.15f, 0, -.1f), new(.15f, 7, .1f), 0));
        var part = new CharacterPart { Record = scene.Appearance.Race, Mesh = "sword", Field = "sword", Mode = AttachMode.Bone, Point = "hand" };
        scene.Appearance.AttachmentPoints.Add("hand", new("hand", "Bip01 L Hand", new(.4f, 0, 0), Quaternion.Identity));
        var geometry = new RuntimeCharacterGeometry(torso, [new(part)]);
        var pose = PhysicalPose();
        pose.Position[1] = new(-2, 1.8f, 3); pose.Rotation[1] = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 1.3f);
        pose.Position[7] = new(-1, 2.1f, 2.5f); pose.Rotation[7] = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -1.2f);
        var world = Matrix4x4.CreateScale(1.4f) * Matrix4x4.CreateRotationY(.5f) * Matrix4x4.CreateTranslation(20, 500, -40);
        var bounds = Assert.IsType<CharacterWorldBounds>(FragmentWorldBounds.For(geometry, scene.Appearance, Rig, scene.Body, pose, world, name => name == "sword" ? sword : null));
        ContainsSkinned(bounds, torso, pose, world);
        var scratch = new PoseScratch(Rig); scratch.PoseDerived(null, [], pose);
        var attachment = scratch.TagTransform(7, Matrix4x4.CreateTranslation(.4f, 0, 0)) * world;
        foreach (var vertex in Vertices(sword))
            Assert.True(Vector3.Distance(Vector3.Transform(vertex.Position, attachment), bounds.Centre) <= bounds.Radius + .0001f);
        Assert.InRange(bounds.Centre.Y, 490, 510);
        Assert.InRange(bounds.Radius, 5, 25);
    }

    [Fact]
    public void Animated_wounds_keep_native_bounds_instead_of_assuming_a_physical_override()
    {
        using var scene = new Scene();
        var geometry = new RuntimeCharacterGeometry();
        var pose = PhysicalPose(); pose.AbsolutePositions = false;
        Assert.Null(FragmentWorldBounds.For(geometry, scene.Appearance, Rig, scene.Body, pose, Matrix4x4.Identity, _ => null));
        Assert.Null(FragmentWorldBounds.For(geometry, scene.Appearance, Rig, scene.Body, null, Matrix4x4.Identity, _ => null));
    }

    [Fact]
    public void Head_region_views_exclude_the_body_cavity_and_are_reused_across_head_planes()
    {
        using var scene = new Scene(withAnatomy: true);
        var first = scene.Adapter.Build(scene.Appearance, Rig, scene.Body, _ => null,
            State(head: new(new(1, 0, 0, 0), HeadCutMode.DetachedSplit)));
        var heads = first.Detached.Select(piece => Assert.Single(piece.Geometry.Parts!, part => part.Part.Field == "anatomy")).ToArray();
        Assert.Equal(2, heads.Length); Assert.Same(heads[0].Mesh, heads[1].Mesh);
        Assert.All(NativeVertices(heads[0].Mesh!), vertex =>
        {
            Assert.True((vertex.Bones & 255) is 3 or 4);
            Assert.NotEqual(7, vertex.Tangent.W);
        });
        Assert.Contains(NativeVertices(heads[0].Mesh!), vertex => vertex.Tangent.W == 6);
        Assert.All(first.Retained.Parts!.Where(part => part.Part.Field == "anatomy"), part =>
            Assert.All(NativeVertices(part.Mesh!), vertex => Assert.False((vertex.Bones & 255) is 3 or 4)));
        var next = scene.Adapter.Build(scene.Appearance, Rig, scene.Body, _ => null,
            State(head: new(new(0, 0, 1, 0), HeadCutMode.DetachedSplit)));
        Assert.All(next.Detached, piece => Assert.Same(heads[0].Mesh, Assert.Single(piece.Geometry.Parts!, part => part.Part.Field == "anatomy").Mesh));
        Assert.All(first.Detached.SelectMany(piece => piece.Geometry.Parts!).Where(part => part.Part.Field is "anatomy" or "wound-section"),
            part => Assert.InRange(part.MinimumScreenPixels, 1, 179));
        var whole = scene.Adapter.Build(scene.Appearance, Rig, scene.Body, _ => null, State());
        Assert.Equal(180, Assert.Single(whole.Retained.Parts!, part => part.Part.Field == "anatomy").MinimumScreenPixels, .001f);
    }

    [Fact]
    public void Region_partition_keeps_rigid_triangles_and_raw_layer_channels_without_changing_the_fitted_source()
    {
        var source = MeshCut.FromTriangles(Box(new(-1, 10, -1), new(1, 15, 1), 1).Select(vertex => vertex with { Tangent = new(vertex.Position, 7) })
            .Concat(Box(new(-1, 17, -1), new(1, 19, 1), 3).Select(vertex => vertex with { Tangent = new(vertex.Position, 6), Colour = new(.6f, .4f, .4f, 2.5f) })).ToArray(),
            preserveRawVertexChannels: true);
        var original = NativeVertices(source);
        var head = Assert.IsType<OgreMesh>(NativeAnatomyRegions.Select(source, Rig, true));
        var body = Assert.IsType<OgreMesh>(NativeAnatomyRegions.Select(source, Rig, false));
        Assert.All(NativeVertices(head), vertex => { Assert.Equal(3u, vertex.Bones); Assert.Equal(6, vertex.Tangent.W); Assert.Equal(2.5f, vertex.Colour.W); });
        Assert.All(NativeVertices(body), vertex => { Assert.Equal(1u, vertex.Bones); Assert.Equal(7, vertex.Tangent.W); });
        Assert.Equal(original.Length, NativeVertices(head).Length + NativeVertices(body).Length);
        Assert.Equal(original, NativeVertices(source));
        Assert.Same(head, NativeAnatomyRegions.Select(source, Rig, true));
    }

    static BonePose PhysicalPose()
    {
        var pose = new BonePose(Rig.BoneCount) { AbsolutePositions = true };
        for (int i = 0; i < Rig.BoneCount; i++)
        { pose.Set[i] = true; pose.Position[i] = new(-2000, -1000, 3000); pose.Rotation[i] = Quaternion.Identity; }
        return pose;
    }

    static void ContainsSkinned(CharacterWorldBounds bounds, OgreMesh mesh, BonePose pose, Matrix4x4 world)
    {
        var skin = new Matrix4x4[Rig.BoneCount]; new PoseScratch(Rig).Pose(null, [], skin, pose);
        foreach (var vertex in Vertices(mesh))
        {
            var point = Vector3.Zero;
            for (int k = 0; k < 4; k++)
                if (vertex.Weights[k] > 0) point += Vector3.Transform(vertex.Position, skin[vertex.Bones >> (k * 8) & 255]) * vertex.Weights[k];
            Assert.True(Vector3.Distance(Vector3.Transform(point, world), bounds.Centre) <= bounds.Radius + .0001f);
        }
    }
}
