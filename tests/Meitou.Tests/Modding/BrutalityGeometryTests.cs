using System.Numerics;
using Meitou.Brutality;
using Meitou.Brutality.Anatomy;
using Meitou.Brutality.Rendering;
using Meitou.Data;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Data.Ogre;
using Meitou.Rendering;

namespace Meitou.Tests.Modding;

public sealed class BrutalityGeometryTests
{
    static readonly SkeletonRig Rig = MakeRig();

    [Theory]
    [InlineData(0, 1, 0, 12.5f)]
    [InlineData(1, .3f, .2f, 3.8f)]
    [InlineData(.2f, .4f, 1, 5.1f)]
    public void A_real_oblique_cut_keeps_both_halfspaces_uvs_and_normalized_weights_without_collapsed_spikes(float x, float y, float z, float offset)
    {
        var normal = Vector3.Normalize(new(x, y, z));
        var plane = new Vector4(normal, -offset);
        var source = MeshCut.FromTriangles(Box(new(-2, 10, -2), new(2, 16, 2), 1));
        var positive = MeshCut.Clip(source, Rig, [new(plane)], capColour: new(.4f, .04f, .04f, 1));
        var negative = MeshCut.Clip(source, Rig, [new(plane, -1)], capColour: new(.4f, .04f, .04f, 1));
        Assert.True(positive.HasTriangles);
        Assert.True(negative.HasTriangles);
        foreach (var (cut, side) in new[] { (positive, 1), (negative, -1) })
        {
            var vertices = Vertices(cut.Skin);
            Assert.NotEmpty(vertices);
            Assert.All(vertices, v =>
            {
                Assert.True(Vector4.Dot(plane, new(v.Position, 1)) * side >= -.0001f);
                Assert.InRange(v.Weights.X + v.Weights.Y + v.Weights.Z + v.Weights.W, .9999f, 1.0001f);
                Assert.Equal(v.Position.X * .25f + .5f, v.Uv.X, .0001f);
                Assert.InRange(v.Position.X, -2.0001f, 2.0001f);
                Assert.InRange(v.Position.Y, 9.9999f, 16.0001f);
            });
            Assert.NotNull(cut.Wound);
            Assert.All(Vertices(cut.Wound!), v => Assert.True(float.IsFinite(v.Position.LengthSquared())));
        }
    }

    [Fact]
    public void Face_poses_are_interpolated_onto_new_edge_vertices_instead_of_disappearing_after_clipping()
    {
        var source = MeshCut.FromTriangles([
            Point(new(-1, 18, 0), 3), Point(new(1, 18, 0), 3), Point(new(0, 20, 0), 3),
        ]);
        var pose = new OgrePose { Name = "head_size", Target = 1 };
        for (uint i = 0; i < 3; i++)
        {
            var p = source.SubMeshes[0].VertexData!.ReadPositions()![i];
            pose.Vertices.Add(new(i, new(p.X * .4f, (p.Y - 18) * .4f, 0), Vector3.Zero));
        }
        source.Poses.Add(pose);
        var cut = MeshCut.Clip(source, Rig, [new(new(1, 0, 0, 0), MorphedDistance: true)], new Dictionary<string, float> { ["head_size"] = .5f });
        var carried = Assert.Single(cut.Skin.Poses);
        Assert.Equal("head_size", carried.Name);
        Assert.Equal((ushort)1, carried.Target);
        var vertices = Vertices(cut.Skin);
        var offsets = carried.Vertices.ToDictionary(p => p.Index, p => p.Offset);
        for (int i = 0; i < vertices.Length; i++)
        {
            var p = vertices[i].Position;
            var expected = new Vector3(p.X * .4f, (p.Y - 18) * .4f, 0);
            Assert.True(Vector3.Distance(expected, offsets.GetValueOrDefault((uint)i)) < .00001f);
        }
        Assert.Contains(vertices, p => MathF.Abs(p.Position.X) < .00001f);
        Assert.Single(source.Poses);
        Assert.Equal((uint)3, source.SubMeshes[0].VertexData!.VertexCount);
    }

    [Fact]
    public void Section_caps_do_not_join_disconnected_body_contours_with_a_large_triangle()
    {
        var source = MeshCut.FromTriangles(Box(new(-6, -1, -1), new(-4, 1, 1), 1).Concat(Box(new(4, -1, -1), new(6, 1, 1), 1)).ToArray());
        var result = MeshCut.Clip(source, Rig, [new(new(0, 1, 0, 0))], capColour: new(.4f, .04f, .04f, 1));
        var wound = Vertices(Assert.IsType<OgreMesh>(result.Wound));
        Assert.NotEmpty(wound);
        for (int i = 0; i + 2 < wound.Length; i += 3)
        {
            float min = MathF.Min(wound[i].Position.X, MathF.Min(wound[i + 1].Position.X, wound[i + 2].Position.X));
            float max = MathF.Max(wound[i].Position.X, MathF.Max(wound[i + 1].Position.X, wound[i + 2].Position.X));
            Assert.True(max - min < 2.1f);
            Assert.True(min > 3.9f || max < -3.9f);
        }
    }

    [Fact]
    public void A_detached_head_split_produces_two_real_head_fragments_and_restores_hair_from_the_supplied_natural_look()
    {
        using var scene = new Scene();
        var hair = new CharacterPart { Record = scene.Appearance.Race, Mesh = "hair.mesh", Field = "hair", Mode = AttachMode.SharedSkeleton, Slot = AttachSlot.Hair };
        scene.Appearance.Parts.Add(hair);
        var hairMesh = MeshCut.FromTriangles(Box(new(-1.1f, 18.5f, -.9f), new(1.1f, 19.5f, .9f), 3));
        var state = State(head: new(new(1, 0, 0, 0), HeadCutMode.DetachedSplit));
        var result = scene.Adapter.Build(scene.Appearance, Rig, scene.Body, path => path == "hair.mesh" ? hairMesh : null, state);
        Assert.Equal(2, result.Detached.Count);
        Assert.DoesNotContain(result.Retained.Parts!, part => part.Part == hair);
        foreach (var fragment in result.Detached)
        {
            Assert.Contains(fragment.Geometry.Parts!, part => part.Part == hair);
            Assert.True(fragment.Kind is GeometryFragmentKind.HeadPositive or GeometryFragmentKind.HeadNegative);
            Assert.True(fragment.BindMin.Y > 16);
            int side = fragment.Kind == GeometryFragmentKind.HeadPositive ? 1 : -1;
            Assert.All(Vertices(fragment.Geometry.BodyMesh!), p => Assert.True(Vector4.Dot(fragment.CutPlane, new(p.Position, 1)) * side >= -.0001f));
        }
        Assert.Same(result, scene.Adapter.Build(scene.Appearance, Rig, scene.Body, _ => throw new InvalidOperationException("Cache should avoid mesh reads"),
            state with { Experience = 400, Level = 90, Progress = .5f }));
    }

    [Fact]
    public void Two_limb_cuts_use_their_own_fitted_position_and_angle_and_rigid_parts_follow_only_their_supporting_piece()
    {
        using var scene = new Scene();
        var weapon = new CharacterPart { Record = scene.Appearance.Race, Mesh = "sword.mesh", Field = "sword", Mode = AttachMode.Bone, Point = "left hand" };
        scene.Appearance.AttachmentPoints.Add("left hand", new("left hand", "Bip01 L Hand", Vector3.Zero, Quaternion.Identity));
        scene.Appearance.Parts.Add(weapon);
        LimbCutState left = new(0, .30f, new(.25f, -.15f)), right = new(1, .76f, new(-.2f, .35f));
        var result = scene.Adapter.Build(scene.Appearance, Rig, scene.Body, _ => null, State(limbs: [left, right]));
        var fragments = result.Detached.Where(p => p.Kind == GeometryFragmentKind.Limb).OrderBy(p => p.LimbSlot).ToArray();
        Assert.Equal(2, fragments.Length);
        Assert.NotEqual(fragments[0].CutPlane, fragments[1].CutPlane);
        Assert.DoesNotContain(result.Retained.Parts!, part => part.Part == weapon);
        Assert.Contains(fragments[0].Geometry.Parts!, part => part.Part == weapon);
        Assert.DoesNotContain(fragments[1].Geometry.Parts!, part => part.Part == weapon);
        foreach (var piece in fragments)
        {
            Assert.Equal(10 + piece.LimbSlot, piece.StableKey);
            var vertices = Vertices(piece.Geometry.BodyMesh!);
            Assert.NotEmpty(vertices);
            Assert.All(vertices, vertex =>
            {
                Assert.Equal(piece.LimbSlot + 1, MeshCut.Group(vertex, MeshCut.BoneGroups(Rig)));
                Assert.True(Vector4.Dot(piece.CutPlane, new(vertex.Position, 1)) <= .0001f);
            });
        }
    }

    [Theory]
    [InlineData(SevereHeadWoundKind.JawLost)]
    [InlineData(SevereHeadWoundKind.Scalp)]
    [InlineData(SevereHeadWoundKind.BrokenSkull)]
    public void Severe_head_wounds_open_the_selected_front_surface_and_add_coloured_relief_without_a_posterior_hole(SevereHeadWoundKind kind)
    {
        using var scene = new Scene(withAnatomy: true);
        var severe = new SevereHeadWoundState(4, kind, kind == SevereHeadWoundKind.JawLost ? 0 : 3,
            new(new(0, 18.65f, 0), Vector3.UnitZ, Vector3.UnitX));
        var state = State() with { SevereHead = severe };
        var result = scene.Adapter.Build(scene.Appearance, Rig, scene.Body, _ => null, state);
        var skin = Vertices(result.Retained.BodyMesh!);
        var original = Vertices(scene.Body);
        Assert.True(FrontArea(skin) < FrontArea(original) - .001f);
        Assert.Equal(Posterior(original), Posterior(skin));
        Assert.False(result.FidelityGaps.HasFlag(GeometryFidelityGap.SevereHeadRelief));
        var relief = Assert.Single(result.Retained.Parts!, p => p.Part.Field == "head-relief");
        var tissue = Vertices(relief.Mesh!);
        Assert.NotEmpty(tissue);
        Assert.True(relief.VertexColours);
        Assert.Contains(tissue, p => p.Colour.X > p.Colour.Y * 2 && p.Colour.Y < .2f);
        if (kind != SevereHeadWoundKind.JawLost) Assert.Contains(tissue, p => p.Colour.Y > .2f && p.Colour.Z > .15f);
        Assert.Contains(result.Retained.Parts!, p => p.Part.Field == "anatomy");
        Assert.Same(result, scene.Adapter.Build(scene.Appearance, Rig, scene.Body, _ => null, state with { SevereHead = severe with { HeavyHits = 7 } }));

        static float FrontArea(Vertex[] vertices)
        {
            float area = 0;
            for (int i = 0; i + 2 < vertices.Length; i += 3)
                if (vertices[i].Normal.Z > .9f && vertices[i].Position.Y > 16.3f)
                    area += Vector3.Cross(vertices[i + 1].Position - vertices[i].Position, vertices[i + 2].Position - vertices[i].Position).Length() * .5f;
            return area;
        }
        static Vector3[] Posterior(Vertex[] vertices) => vertices.Where(p => p.Normal.Z < -.9f && p.Position.Y > 16.3f)
            .Select(p => p.Position).OrderBy(p => p.X).ThenBy(p => p.Y).ThenBy(p => p.Z).ToArray();
    }

    [Fact]
    public void A_broken_skull_opens_the_bone_cleft_while_the_brain_keeps_its_convoluted_geometry_behind_it()
    {
        using var scene = new Scene();
        var state = new SevereHeadWoundState(4, SevereHeadWoundKind.BrokenSkull, 3, new(new(0, 18.65f, 0), Vector3.UnitZ, Vector3.UnitX));
        var plan = Assert.IsType<HeadReliefPlan>(HeadReliefGeometry.Build(scene.Body, Rig, scene.Appearance, state));
        Assert.NotEmpty(plan.Inside);
        Assert.All(plan.Inside, pocket => Assert.True(pocket.BoneOnly));
        var ivory = new Vector4(.61f, .49f, .32f, 1);
        var pink = new Vector4(.6f, .4f, .4f, 1);
        var source = MeshCut.FromTriangles(Box(new(-1, 16.4f, -.9f), new(1, 19.4f, .9f), 3).Select(v => v with { Colour = ivory })
            .Concat(Box(new(-.7f, 17.5f, -.6f), new(.7f, 19, .6f), 3).Select(v => v with { Colour = pink })).ToArray());
        var cut = MeshCut.Clip(source, Rig, [], removeVolumes: plan.Inside);
        var originalBrain = Vertices(source).Where(v => v.Colour.Z > .38f).Select(v => v.Position).ToArray();
        var remainingBrain = Vertices(cut.Skin).Where(v => v.Colour.Z > .38f).Select(v => v.Position).ToArray();
        Assert.Equal(originalBrain, remainingBrain);
        Assert.True(Vertices(cut.Skin).Length > originalBrain.Length);
        Assert.NotEqual(Vertices(source).Length, Vertices(cut.Skin).Length);
    }

    [Fact]
    public void Forget_retires_both_pieces_and_drops_the_cpu_result_while_the_native_appearance_is_still_alive()
    {
        using var scene = new Scene();
        var state = State(torso: new(new(0, 1, 0, -13)));
        var result = scene.Adapter.Build(scene.Appearance, Rig, scene.Body, _ => null, state);
        Assert.True(scene.Adapter.TryCached(scene.Appearance, Rig, scene.Body, state, out var cached)); Assert.Same(result, cached);
        Assert.NotEmpty(result.Detached);
        scene.Adapter.Forget(scene.Appearance);
        Assert.True(result.Retained.IsRetired); Assert.All(result.Detached, piece => Assert.True(piece.Geometry.IsRetired));
        Assert.False(scene.Adapter.TryCached(scene.Appearance, Rig, scene.Body, state, out _));
        var next = scene.Adapter.Build(scene.Appearance, Rig, scene.Body, _ => null, state);
        Assert.NotSame(result, next); Assert.False(next.Retained.IsRetired);
    }

    static BrutalityActorView State(HeadCutState? head = null, TorsoBisectionState? torso = null, IReadOnlyList<LimbCutState>? limbs = null) =>
        new(default, 0, 100, 0, default, head, torso, limbs ?? []);

    sealed class Scene : IDisposable
    {
        readonly string folder = Directory.CreateTempSubdirectory("meitou-brutality-geometry-").FullName;
        public CharacterAppearance Appearance { get; }
        public CharacterGeometryAdapter Adapter { get; }
        public OgreMesh Body { get; } = MeshCut.FromTriangles(Box(new(-1.5f, 10, -1), new(1.5f, 16.4f, 1), 1)
            .Concat(Box(new(-1, 16.4f, -.9f), new(1, 19.4f, .9f), 3))
            .Concat(Box(new(-3, 9.5f, -.5f), new(-2, 16.2f, .5f), 5))
            .Concat(Box(new(2, 9.5f, -.5f), new(3, 16.2f, .5f), 8)).ToArray());
        public Scene(bool withAnatomy = false)
        {
            if (withAnatomy)
            {
                WriteAnatomy("Ecorche.anatomy", 0, Box(new(-1, 16.4f, -.9f), new(1, 19.4f, .9f), 3)
                    .Select(v => v with { Position = v.Position - new Vector3(0, 10, 0) + new Vector3(-.0446148f, 9.956298f, -.227702f) }).ToArray());
                WriteAnatomy("Brain.anatomy", 6, Box(new(-1), new(1), 3));
            }
            var db = new GameDatabase();
            var file = new FcsFile();
            file.Records.Add(new FcsRecord { StringId = "test-race", Name = "test race", RecordType = FcsRecordType.RACE, Flags = 0x10 });
            db.Apply(file, "test.mod");
            Appearance = new CharacterAppearance { Race = db.Find("test-race")! };
            Adapter = new(new AnatomyLibrary(new AnatomyAssets(folder)));
        }
        void WriteAnatomy(string name, int layer, Vertex[] source)
        {
            string path = Path.Combine(folder, "assets", "anatomy", name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var writer = new BinaryWriter(File.Create(path));
            writer.Write(System.Text.Encoding.ASCII.GetBytes("ECR1")); writer.Write(1);
            byte[] bone = System.Text.Encoding.ASCII.GetBytes("Bip01 Head");
            writer.Write(bone.Length); writer.Write(layer); writer.Write(source.Length); writer.Write(bone);
            foreach (var v in source)
            {
                writer.Write(v.Position.X); writer.Write(v.Position.Y); writer.Write(v.Position.Z);
                writer.Write(v.Normal.X); writer.Write(v.Normal.Y); writer.Write(v.Normal.Z); writer.Write(v.Uv.X); writer.Write(v.Uv.Y);
            }
        }
        public void Dispose() => Directory.Delete(folder, recursive: true);
    }

    static Vertex[] Vertices(OgreMesh mesh) => Model.Build(mesh, Rig.BoneCount).Parts.SelectMany(p => p.Vertices).ToArray();
    static Vertex Point(Vector3 p, int bone) => new() { Position = p, Normal = Vector3.UnitZ, Uv = new(p.X * .25f + .5f, p.Y),
        Tangent = new(1, 0, 0, 1), Colour = Vector4.One, Bones = (uint)bone, Weights = Vector4.UnitX };

    static Vertex[] Box(Vector3 min, Vector3 max, int bone)
    {
        var output = new List<Vertex>();
        for (int axis = 0; axis < 3; axis++)
            foreach (int side in new[] { 0, 1 })
            {
                int x = (axis + 1) % 3, y = (axis + 2) % 3;
                Vector3 P(bool a, bool b)
                {
                    var p = min; p[axis] = side == 0 ? min[axis] : max[axis]; p[x] = a ? max[x] : min[x]; p[y] = b ? max[y] : min[y]; return p;
                }
                var a = P(false, false); var b = P(true, false); var c = P(true, true); var d = P(false, true);
                if (side == 0) (b, d) = (d, b);
                var normal = Vector3.Zero; normal[axis] = side == 0 ? -1 : 1;
                foreach (var p in new[] { a, b, c, a, c, d }) output.Add(Point(p, bone) with { Normal = normal });
            }
        return output.ToArray();
    }

    static SkeletonRig MakeRig()
    {
        var skeleton = new OgreSkeleton();
        void Bone(ushort handle, string name, Vector3 p, ushort? parent = null) => skeleton.Bones.Add(new() { Handle = handle, Name = name, Position = p, Parent = parent });
        Bone(0, "Bip01 Pelvis", new(0, 10, 0));
        Bone(1, "Bip01 Spine1", new(0, 3, 0), 0);
        Bone(2, "Bip01 Neck", new(0, 3.1649f, 0), 1);
        Bone(3, "Bip01 Head", new(0, 2, 0), 2);
        Bone(4, "Bip01 Jaw", new(0, -.5f, .2f), 3);
        Bone(5, "Bip01 L UpperArm", new(-2.5f, 0, 0), 2);
        Bone(6, "Bip01 L Forearm", new(0, -3, 0), 5);
        Bone(7, "Bip01 L Hand", new(0, -3, 0), 6);
        Bone(8, "Bip01 R UpperArm", new(2.5f, 0, 0), 2);
        Bone(9, "Bip01 R Forearm", new(0, -3, 0), 8);
        Bone(10, "Bip01 R Hand", new(0, -3, 0), 9);
        return new(skeleton);
    }
}
