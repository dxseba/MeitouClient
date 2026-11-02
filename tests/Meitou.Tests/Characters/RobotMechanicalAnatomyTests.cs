using System.Numerics;
using System.Runtime.InteropServices;
using Meitou.Data;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Data.Ogre;
using Meitou.Rendering;
using Meitou.Rendering.Characters;
using Meitou.Simulation.Ragdolls;

namespace Meitou.Tests.Characters;

public class RobotMechanicalAnatomyTests
{
    const int HeadBone = 4, ForearmBone = 6;

    [Fact]
    public void Robot_modules_fit_their_own_skin_and_contain_circuits_cables_and_metal_without_organic_layers()
    {
        var rig = Rig();
        var skin = Skin(rig);
        var made = Assert.IsType<List<Vertex>>(CharacterMechanicalAnatomy.Build(rig, skin, []));
        Assert.InRange(made.Count, 1000, CharacterMechanicalAnatomy.VertexBudget);
        Assert.Equal(0, made.Count % 3);
        Assert.Contains(made, v => v.Colour.Y > v.Colour.X * 2 && v.Colour.Y > v.Colour.Z);
        Assert.Contains(made, v => v.Colour.X > v.Colour.Y && v.Colour.Y > v.Colour.Z * 2);
        Assert.Contains(made, v => MathF.Abs(v.Colour.X - v.Colour.Y) < .05f && v.Colour.Y > .2f);
        foreach (var vertex in made)
        {
            CheckVertex(vertex, rig.BoneCount);
            Assert.Equal(CharacterMechanicalAnatomy.Layer, vertex.Tangent.W);
            var source = skin.Where(v => v.Bones == vertex.Bones).Select(v => v.Position).ToArray();
            Assert.NotEmpty(source);
            var min = source.Aggregate(new Vector3(float.MaxValue), Vector3.Min);
            var max = source.Aggregate(new Vector3(float.MinValue), Vector3.Max);
            for (int axis = 0; axis < 3; axis++) Assert.InRange(vertex.Position[axis], min[axis], max[axis]);
        }
    }

    [Fact]
    public void Headless_and_detached_robot_assets_keep_only_their_own_mechanical_region_even_without_head_uv_charts()
    {
        var rig = Rig();
        var skin = Skin(rig);
        Assert.All(skin, v => Assert.True(v.Uv.Y >= 0));
        var whole = Assert.IsType<List<Vertex>>(CharacterMechanicalAnatomy.Build(rig, skin, []));
        var body = Assert.IsType<List<Vertex>>(CharacterMechanicalAnatomy.Build(rig, skin, [], headless: true));
        var head = Assert.IsType<List<Vertex>>(CharacterMechanicalAnatomy.Build(rig, skin, [], headOnly: true));
        Assert.NotEmpty(head);
        Assert.NotEmpty(body);
        Assert.All(head, v => Assert.Equal((uint)HeadBone, v.Bones));
        Assert.DoesNotContain(body, v => v.Bones == HeadBone);
        Assert.Equal(whole.Count, head.Count + body.Count);
        Assert.All(whole, v => Assert.True(v.Tangent.W > TorsoCutSurface.Layer));
        Assert.Null(CharacterMechanicalAnatomy.Build(rig, [], []));
    }

    [Fact]
    public void Exposed_robot_modules_follow_their_physical_bone_without_stretching_toward_another_fragment()
    {
        var rig = Rig();
        var made = Assert.IsType<List<Vertex>>(CharacterMechanicalAnatomy.Build(rig, Skin(rig), []));
        var palette = Enumerable.Repeat(Matrix4x4.CreateTranslation(0, 400, 0), rig.BoneCount).ToArray();
        palette[ForearmBone] = Matrix4x4.CreateRotationZ(.8f) * Matrix4x4.CreateTranslation(-100, 8, 20);
        bool forearm = false;
        for (int triangle = 0; triangle < made.Count; triangle += 3)
        {
            var a = made[triangle];
            var b = made[triangle + 1];
            var c = made[triangle + 2];
            Assert.Equal(a.Bones, b.Bones);
            Assert.Equal(a.Bones, c.Bones);
            foreach (var (from, to) in new[] { (a, b), (b, c), (c, a) })
                Assert.Equal(Vector3.Distance(from.Position, to.Position), Vector3.Distance(Pose(from, palette), Pose(to, palette)), 1e-4f);
            if (a.Bones != ForearmBone) continue;
            forearm = true;
            Assert.True(Pose(a, palette).X < -100);
            Assert.True(Pose(a, palette).Y < 50);
        }
        Assert.True(forearm);
    }

    [Fact]
    public void Robot_head_cuts_resolve_the_actual_weighted_head_instead_of_a_human_negative_v_region()
    {
        var rig = Rig();
        var skin = Skin(rig);
        var asset = Asset(rig, skin);
        var source = HeadCutSurface.Skin(asset);
        Assert.Equal(36, source.Length);
        Assert.All(source, v => Assert.Equal((uint)HeadBone, v.Bones));
        var cut = HeadCutSurface.Resolve(asset, new(new(0, 1, 0, -.5f), 1, FullBody: true, HeadOnly: true));
        float headHeight = rig.BindPosition[HeadBone].Y;
        Assert.Equal(headHeight + .6f, -cut.Plane.W / cut.Plane.Y, 1e-5f);
        Assert.True(TorsoCutSurface.Band(new(0, headHeight + 1, 0), 7, cut) > 0);
        Assert.True(TorsoCutSurface.Band(new(0, headHeight, 0), 7, cut) < 0);
        Assert.Equal(1000, TorsoCutSurface.Band(new(0, 0, 0), 0, cut));
    }

    [Fact]
    public void Physical_robot_head_planes_match_rendering_for_positive_v_head_jaw_and_descendant_skin()
    {
        var skeleton = Rig().Skeleton;
        skeleton.Bones.AddRange([
            new() { Name = "Bip01 Jaw", Handle = 9, Parent = HeadBone, Position = new(0, -.6f, .3f) },
            new() { Name = "Robot head shell", Handle = 10, Parent = HeadBone, Position = new(2, .8f, .8f) },
        ]);
        var rig = new SkeletonRig(skeleton);
        var skin = Skin(rig);
        var rendered = Asset(rig, skin);
        var physical = RagdollSkinSurface.Read(Mesh(skin));
        Assert.All(skin, v => Assert.True(v.Uv.Y > 0));
        foreach (var normalized in new[] { new Vector4(0, 1, 0, -.3f), new Vector4(Vector3.Normalize(new(.8f, .3f, -.5f)), .15f) })
        {
            var expected = HeadCutSurface.Resolve(rendered, new(normalized, 1, FullBody: true, HeadOnly: true));
            Assert.True(physical.HeadPlane(rig, normalized, out var actual, mechanical: true));
            Assert.InRange(Vector4.Distance(expected.Plane, actual), 0, 1e-5f);
            Assert.True(physical.HeadPlane(rig, normalized, out var fallback));
            Assert.True(Vector4.Distance(actual, fallback) > .01f);
        }
    }

    [Fact]
    public void Robot_head_torso_and_limb_sections_remain_finite_outward_metal_surfaces_on_both_cut_sides()
    {
        var rig = Rig();
        var body = Skin(rig);
        var head = Asset(rig, body);
        uint[] groups = new uint[8];
        foreach (var bone in rig.Skeleton.Bones)
            groups[bone.Handle / 8] |= (uint)WoundMasks.BodyGroup(bone.Name) << (bone.Handle % 8 * 4);
        var examples = new[]
        {
            (Source: body, Groups: groups, Cut: new CharacterTorsoCut(new(1, 0, 0, 0), 1, FullBody: true)),
            (Source: HeadCutSurface.Skin(head), Groups: HeadCutSurface.Groups(head),
                Cut: HeadCutSurface.Resolve(head, new(new(0, 1, 0, 0), 1, FullBody: true, HeadOnly: true))),
            (Source: body.Where(v => v.Bones == ForearmBone).ToArray(), Groups: groups,
                Cut: new CharacterTorsoCut(new(0, 1, 0, -rig.BindPosition[ForearmBone].Y), 1, FullBody: true)),
        };
        foreach (var example in examples)
        foreach (int side in new[] { -1, 1 })
        {
            var cut = example.Cut with { KeepSide = side };
            var cap = Assert.IsType<Vertex[]>(TorsoCutSurface.Build(example.Source, example.Groups, cut,
                bloodColour: 0xFF0000, mechanical: true));
            Assert.InRange(cap.Length, 3, 32768);
            Assert.Equal(0, cap.Length % 3);
            var outward = new Vector3(cut.Plane.X, cut.Plane.Y, cut.Plane.Z) * -side;
            Assert.All(cap, vertex =>
            {
                CheckVertex(vertex, rig.BoneCount);
                Assert.Equal(TorsoCutSurface.Layer, vertex.Tangent.W);
                Assert.True(vertex.Colour.Y >= vertex.Colour.X, "A robot section contains red flesh or yellow marrow.");
                Assert.True(Vector3.Dot(vertex.Normal, outward) >= -1e-5f);
            });
        }
    }

    [Fact]
    public void Many_robot_modules_stay_within_the_vertex_budget_without_partial_triangles()
    {
        var skeleton = new OgreSkeleton();
        skeleton.Bones.Add(new() { Name = "Bip01 Pelvis", Handle = 0 });
        for (ushort bone = 1; bone < 40; bone++)
            skeleton.Bones.Add(new() { Name = "Bip01 Spine" + bone, Handle = bone, Parent = 0, Position = new(bone * 3, 10, 0) });
        var rig = new SkeletonRig(skeleton);
        var source = Enumerable.Range(0, rig.BoneCount)
            .SelectMany(bone => Box(rig.BindPosition[bone], new(1, 1, 1), (uint)bone)).ToArray();
        var made = Assert.IsType<List<Vertex>>(CharacterMechanicalAnatomy.Build(rig, source, []));
        Assert.InRange(made.Count, 10000, CharacterMechanicalAnatomy.VertexBudget);
        Assert.Equal(0, made.Count % 3);
        Assert.All(made, vertex => CheckVertex(vertex, rig.BoneCount));
    }

    static void CheckVertex(Vertex vertex, int boneCount)
    {
        Assert.True(float.IsFinite(vertex.Position.LengthSquared()));
        Assert.True(float.IsFinite(vertex.Normal.LengthSquared()));
        Assert.InRange(vertex.Normal.Length(), .999f, 1.001f);
        Assert.True(float.IsFinite(vertex.Colour.LengthSquared()));
        Assert.InRange(vertex.Bones, 0u, (uint)boneCount - 1);
        Assert.Equal(Vector4.UnitX, vertex.Weights);
    }

    static Vector3 Pose(Vertex vertex, Matrix4x4[] palette) => Vector3.Transform(vertex.Position, palette[vertex.Bones]);

    static CharacterAsset Asset(SkeletonRig rig, Vertex[] skin)
    {
        var record = new FcsRecord { StringId = "robot-geometry", Name = "Robot", RecordType = FcsRecordType.RACE };
        record.Bools["is robot"] = true;
        var file = new FcsFile();
        file.Records.Add(record);
        var database = new GameDatabase();
        database.Apply(file, "robot-test");
        return new() { Appearance = new() { Race = database.Find(record.StringId)! }, Rig = rig, BodySource = skin, HeadSource = [] };
    }

    static SkeletonRig Rig()
    {
        var skeleton = new OgreSkeleton();
        skeleton.Bones.AddRange([
            new() { Name = "Bip01 Pelvis", Handle = 0, Position = new(0, 10, 0) },
            new() { Name = "Bip01 Spine", Handle = 1, Parent = 0, Position = new(0, 2, 0) },
            new() { Name = "Bip01 Spine2", Handle = 2, Parent = 1, Position = new(0, 2, 0) },
            new() { Name = "Bip01 Neck", Handle = 3, Parent = 2, Position = new(0, 2, 0) },
            new() { Name = "Bip01 Head", Handle = 4, Parent = 3, Position = new(0, 2, 0) },
            new() { Name = "Bip01 L UpperArm", Handle = 5, Parent = 2, Position = new(-2, 1, 0) },
            new() { Name = "Bip01 L Forearm", Handle = 6, Parent = 5, Position = new(-2, -1, 0) },
            new() { Name = "Bip01 L Thigh", Handle = 7, Parent = 0, Position = new(-1, -3, 0) },
            new() { Name = "Bip01 L Calf", Handle = 8, Parent = 7, Position = new(0, -3, 0) },
        ]);
        return new(skeleton);
    }

    static Vertex[] Skin(SkeletonRig rig) => Enumerable.Range(0, rig.BoneCount).Where(bone => bone != 3)
        .SelectMany(bone => Box(rig.BindPosition[bone], bone == HeadBone ? new(.9f, 1.2f, .8f)
            : bone >= 5 ? new(.45f, .8f, .45f) : new(1.4f, .65f, .7f), (uint)bone)).ToArray();

    static OgreMesh Mesh(Vertex[] skin)
    {
        var positions = skin.Select(v => v.Position).ToArray();
        var uv = skin.Select(v => v.Uv).ToArray();
        var data = new OgreVertexData { VertexCount = (uint)skin.Length };
        data.Elements.Add(new(0, OgreVertexElementType.Float3, OgreVertexSemantic.Position, 0, 0));
        data.Elements.Add(new(1, OgreVertexElementType.Float2, OgreVertexSemantic.TextureCoordinates, 0, 0));
        data.Buffers[0] = new(12, MemoryMarshal.AsBytes(positions.AsSpan()).ToArray());
        data.Buffers[1] = new(8, MemoryMarshal.AsBytes(uv.AsSpan()).ToArray());
        var sub = new OgreSubMesh { VertexData = data, Indices = new(false, Enumerable.Range(0, skin.Length).Select(i => (uint)i).ToArray()) };
        for (uint i = 0; i < skin.Length; i++) sub.BoneAssignments.Add(new(i, (ushort)skin[i].Bones, 1));
        var mesh = new OgreMesh();
        mesh.SubMeshes.Add(sub);
        return mesh;
    }

    static Vertex[] Box(Vector3 centre, Vector3 half, uint bone)
    {
        var vertices = new List<Vertex>();
        Vertex At(int x, int y, int z) => new() { Position = centre + half * new Vector3(x, y, z), Normal = Vector3.UnitZ,
            Uv = new(.5f, .5f), Bones = bone, Weights = Vector4.UnitX };
        void Quad(Vertex a, Vertex b, Vertex c, Vertex d) => vertices.AddRange([a, b, c, b, d, c]);
        Quad(At(-1, -1, -1), At(1, -1, -1), At(-1, 1, -1), At(1, 1, -1));
        Quad(At(-1, -1, 1), At(-1, 1, 1), At(1, -1, 1), At(1, 1, 1));
        Quad(At(-1, -1, -1), At(-1, 1, -1), At(-1, -1, 1), At(-1, 1, 1));
        Quad(At(1, -1, -1), At(1, -1, 1), At(1, 1, -1), At(1, 1, 1));
        Quad(At(-1, -1, -1), At(-1, -1, 1), At(1, -1, -1), At(1, -1, 1));
        Quad(At(-1, 1, -1), At(1, 1, -1), At(-1, 1, 1), At(1, 1, 1));
        return vertices.ToArray();
    }
}
