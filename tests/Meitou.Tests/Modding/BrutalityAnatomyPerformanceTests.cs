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

public sealed class BrutalityAnatomyPerformanceTests
{
    [Fact]
    public void Dense_interior_levels_reduce_indices_without_changing_vertices_or_the_cut_boundary()
    {
        var rig = Rig();
        var points = new List<Vertex>();
        Vertex V(int x, int y) => new() { Position = new(x * .01f, 10 + y * .01f, 0), Normal = Vector3.UnitZ,
            Colour = new(.6f, .5f, .3f, 1), Tangent = new(1, 0, 0, 1), Weights = Vector4.UnitX };
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            points.AddRange([V(x, y), V(x + 1, y), V(x + 1, y + 1), V(x, y), V(x + 1, y + 1), V(x, y + 1)]);
        CutHalfspace[] cuts = [new(new(1, 0, 0, -.32f))];
        var mesh = MeshCut.Clip(MeshCut.FromTriangles(points), rig, cuts).Skin;
        var source = mesh.SubMeshes[0].VertexData!.Buffers[0].Data.ToArray();
        Assert.Same(mesh, AnatomyDetail.WithLevels(mesh, rig, cuts));
        Assert.Equal(source, mesh.SubMeshes[0].VertexData!.Buffers[0].Data);
        var levels = MeshLod.Levels(mesh); Assert.Equal(4, levels.Count);
        Assert.True(levels[^1].Indices[0]!.Length < levels[0].Indices[0]!.Length / 2);
        var vertices = Model.Build(mesh, rig.BoneCount).Parts[0].Vertices;
        var used = levels[^1].Indices[0]!.ToHashSet();
        foreach (uint index in levels[0].Indices[0]!)
            if (MathF.Abs(vertices[index].Position.X - .32f) < .0799f) Assert.Contains(index, used);
        Assert.All(levels.SelectMany(level => level.Indices[0]!), index => Assert.True(index < vertices.Length));
    }

    [Fact]
    public void Anatomy_fitting_is_shared_across_outfit_copies_but_distinct_effective_morphs_get_their_own_fit()
    {
        string folder = Directory.CreateTempSubdirectory("meitou-anatomy-cache-").FullName;
        try
        {
            var library = new AnatomyLibrary(new AnatomyAssets(folder)); var rig = Rig();
            var file = new FcsFile(); var race = new FcsRecord { StringId = "robot", Name = "Skeleton", RecordType = FcsRecordType.RACE, Flags = 0x10 };
            race.Bools["is robot"] = true; file.Records.Add(race); var db = new GameDatabase(); db.Apply(file, "test.mod");
            var look = new CharacterAppearance { Race = db.Find("robot")! };
            var body = MeshCut.FromTriangles(Box(new(-1, 9, -1), new(1, 13, 1)));
            var pose = new OgrePose { Name = "face", Target = 1 }; pose.Vertices.Add(new(0, new(-.3f, 0, 0), default)); body.Poses.Add(pose);
            var fitted = AnatomyGeometry.Build(library, look, rig, body); Assert.NotNull(fitted);
            for (int i = 0; i < 100; i++) Assert.Same(fitted, AnatomyGeometry.Build(library, look.WithParts([]), rig, body));
            var changed = new CharacterAppearance { Race = look.Race }; changed.PoseWeights["face"] = 1;
            Assert.NotSame(fitted, AnatomyGeometry.Build(library, changed, rig, body));
            Assert.Same(fitted, AnatomyGeometry.Build(library, look, rig, body));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    static SkeletonRig Rig()
    {
        var skeleton = new OgreSkeleton(); skeleton.Bones.Add(new() { Handle = 0, Name = "Bip01 Pelvis", Position = new(0, 10, 0) });
        skeleton.Bones.Add(new() { Handle = 1, Parent = 0, Name = "Bip01 Neck", Position = new(0, 6.1649f, 0) });
        return new(skeleton);
    }
    static Vertex[] Box(Vector3 min, Vector3 max)
    {
        var result = new List<Vertex>();
        for (int axis = 0; axis < 3; axis++) foreach (int side in new[] { 0, 1 })
        {
            int x = (axis + 1) % 3, y = (axis + 2) % 3;
            Vector3 P(bool a, bool b) { var p = min; p[axis] = side == 0 ? min[axis] : max[axis]; p[x] = a ? max[x] : min[x]; p[y] = b ? max[y] : min[y]; return p; }
            var a = P(false, false); var b = P(true, false); var c = P(true, true); var d = P(false, true); if (side == 0) (b, d) = (d, b);
            var n = Vector3.Zero; n[axis] = side == 0 ? -1 : 1;
            foreach (var p in new[] { a, b, c, a, c, d }) result.Add(new() { Position = p, Normal = n, Colour = Vector4.One, Weights = Vector4.UnitX });
        }
        return result.ToArray();
    }
}
